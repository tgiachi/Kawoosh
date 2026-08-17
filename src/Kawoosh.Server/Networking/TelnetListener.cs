using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Kawoosh.Server.Data.Network;
using Serilog;

namespace Kawoosh.Server.Networking;

/// <summary>
/// Accepts telnet clients and gives each one a <see cref="TelnetSession" />. Session tasks are
/// tracked so shutdown can await them, but the accept loop never waits on a session.
/// </summary>
public sealed class TelnetListener : IDisposable
{
    public const int DefaultPort = 4000;

    private readonly ILogger _logger = Log.ForContext<TelnetListener>();
    private TcpListener _listener;
    private readonly ConcurrentDictionary<Guid, Task> _sessions = new();

    /// <summary>
    /// The bound port. Equals the requested port, or the ephemeral port the OS assigned when
    /// 0 was requested. Bound in the constructor, so callers never race the accept loop.
    /// </summary>
    public int Port { get; set; }


    public void Start(int port = DefaultPort)
    {
        _listener = new(IPAddress.Any, port);

        // Bind here rather than in StartAsync: a caller that starts the accept loop as a
        // task must be able to read the assigned port immediately.
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    public void Dispose()
        => _listener.Dispose();

    public async Task StartAsync(ChannelWriter<Command> commands, CancellationToken cancellationToken)
    {
        _logger.Information("Telnet listener accepting connections on port {Port}", Port);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(cancellationToken);
                Accept(client, commands, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown, not a fault.
        }
        catch (SocketException exception)
        {
            _logger.Error(exception, "Telnet listener stopped accepting on port {Port}", Port);
        }
        finally
        {
            _listener.Stop();
            await DrainSessionsAsync();

            _logger.Information("Telnet listener on port {Port} stopped", Port);
        }
    }

    private void Accept(TcpClient client, ChannelWriter<Command> commands, CancellationToken cancellationToken)
    {
        var session = new TelnetSession(client, commands);

        _logger.Debug("Session {SessionId} accepted", session.Id);

        // Deliberately not awaited: one talkative client must not stall the accept loop.
        var task = session.StartAsync(cancellationToken);
        _sessions[session.Id] = task;

        task.ContinueWith(
            completed =>
            {
                _sessions.TryRemove(session.Id, out _);
                session.Dispose();

                // Reading Exception also observes it: an unobserved session fault would
                // otherwise be swallowed by the runtime with no trace at any level.
                if (completed.Exception is not null)
                {
                    _logger.Error(completed.Exception, "Session {SessionId} ended with a fault", session.Id);
                }
            },
            TaskScheduler.Default
        );
    }

    /// <summary>
    /// Waits for every tracked session, absorbing faults. This runs in the accept loop's
    /// finally block, where an escaping exception would replace whatever brought the loop
    /// down and skip the shutdown logging that explains it.
    /// </summary>
    private async Task DrainSessionsAsync()
    {
        try
        {
            await Task.WhenAll(_sessions.Values.ToArray());
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Telnet listener on port {Port} had a session fail during shutdown", Port);
        }
    }
}
