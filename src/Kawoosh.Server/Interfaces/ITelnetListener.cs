using System.Threading.Channels;
using Kawoosh.Server.Data.Network;
using Kawoosh.Server.Networking;

namespace Kawoosh.Server.Interfaces;

/// <summary>
/// Accepts telnet clients and gives each one a session, without the accept loop ever waiting
/// on one of them.
/// </summary>
public interface ITelnetListener : IDisposable
{
    /// <summary>The port used when none is given.</summary>
    const int DefaultPort = 4000;

    /// <summary>
    /// Raised on the accept loop for each new client, before it can send anything. The
    /// listener does not know or care what a subscriber does with the session; this is how
    /// the game layer learns about a connection without the telnet layer learning about the
    /// game.
    /// </summary>
    event Action<TelnetSession>? SessionAccepted;

    /// <summary>
    /// The bound port: the requested one, or the ephemeral port the OS assigned when 0 was
    /// requested. Zero until <see cref="Start" /> has run.
    /// </summary>
    int Port { get; }

    /// <summary>Binds the socket. Must run before <see cref="StartAsync" />.</summary>
    /// <param name="port">Port to bind, or 0 to take an ephemeral one.</param>
    void Start(int port = DefaultPort);

    /// <summary>Accepts clients until cancelled, then waits for the sessions still running.</summary>
    /// <param name="commands">The channel every session publishes its lines to.</param>
    /// <param name="cancellationToken">Stops the accept loop.</param>
    Task StartAsync(ChannelWriter<Command> commands, CancellationToken cancellationToken);
}
