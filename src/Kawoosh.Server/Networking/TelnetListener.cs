using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Serilog;
using Kawoosh.Server.Data.Network;

namespace Kawoosh.Server.Networking;

/// <summary>
/// Accepts telnet clients and gives each one a <see cref="TelnetSession"/>. Session tasks are
/// tracked so shutdown can await them, but the accept loop never waits on a session.
/// </summary>
public sealed class TelnetListener : IDisposable
{
    public const int DefaultPort = 4000;

    private readonly ILogger _logger = Log.ForContext<TelnetListener>();
    private readonly TcpListener _listener;
    private readonly ConcurrentDictionary<Guid, Task> _sessions = new();

    /// <summary>
    /// The bound port. Equals the requested port, or the ephemeral port the OS assigned when
    /// 0 was requested. Bound in the constructor, so callers never race the accept loop.
    /// </summary>
    public int Port { get; }

    public TelnetListener(int port = DefaultPort)
    {
        _listener = new TcpListener(IPAddress.Any, port);

        // Bind here rather than in StartAsync: a caller that starts the accept loop as a
        // task must be able to read the assigned port immediately.
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

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
            await Task.WhenAll(_sessions.Values.ToArray());

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
            _ =>
            {
                _sessions.TryRemove(session.Id, out _);
                session.Dispose();
            },
            TaskScheduler.Default
        );
    }

    public void Dispose()
    {
        _listener.Dispose();
    }
}
