using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Serilog;
using Kawoosh.Server.Data.Network;
using Kawoosh.Server.Interfaces;

namespace Kawoosh.Server.Networking;

/// <summary>
/// Accepts telnet clients and gives each one a <see cref="TelnetSession" />. Session tasks are
/// tracked so shutdown can await them, but the accept loop never waits on a session.
/// </summary>
public sealed class TelnetListener : ITelnetListener
{
    private readonly ILogger _logger = Log.ForContext<TelnetListener>();
    private readonly ConcurrentDictionary<Guid, Task> _sessions = new();

    // Null until Start binds it: the type is resolved from the service provider, which needs a
    // parameterless constructor, so construction and binding cannot be the same step.
    private TcpListener? _listener;

    /// <inheritdoc />
    public event Action<TelnetSession>? SessionAccepted;

    /// <summary>
    /// The bound port: the requested port, or the ephemeral port the OS assigned when 0 was
    /// requested. Zero until <see cref="Start" /> has run. Bound there rather than in
    /// <see cref="StartAsync" /> so a caller that launches the accept loop as an un-awaited
    /// task can still read the port immediately.
    /// </summary>
    public int Port { get; private set; }

    /// <summary>Binds the socket. Must run before <see cref="StartAsync" />.</summary>
    /// <exception cref="InvalidOperationException">The listener is already bound.</exception>
    public void Start(int port = ITelnetListener.DefaultPort)
    {
        if (_listener is not null)
        {
            throw new InvalidOperationException($"Telnet listener is already started on port {Port}.");
        }

        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();

        _listener = listener;
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <exception cref="InvalidOperationException"><see cref="Start" /> has not run.</exception>
    public async Task StartAsync(ChannelWriter<Command> commands, CancellationToken cancellationToken)
    {
        // Fail here rather than on a null dereference deeper in: the caller's mistake is
        // having skipped Start, and the message should say so.
        var listener = _listener ??
                       throw new InvalidOperationException(
                           "Telnet listener must be started with Start before StartAsync is called."
                       );

        _logger.Information("Telnet listener accepting connections on port {Port}", Port);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken);
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
            listener.Stop();
            await DrainSessionsAsync();

            _logger.Information("Telnet listener on port {Port} stopped", Port);
        }
    }

    private void Accept(TcpClient client, ChannelWriter<Command> commands, CancellationToken cancellationToken)
    {
        var session = new TelnetSession(client, commands);

        _logger.Debug("Session {SessionId} accepted", session.Id);

        // Before the read loop starts, so a greeting cannot lose a race with the first line
        // an eager client sends.
        RaiseAccepted(session);

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

    private void RaiseAccepted(TelnetSession session)
    {
        // A subscriber that throws must not kill the accept loop for everyone else.
        try
        {
            SessionAccepted?.Invoke(session);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "A SessionAccepted subscriber failed for session {SessionId}", session.Id);
        }
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

    public void Dispose()
    {
        _listener?.Dispose();
    }
}
