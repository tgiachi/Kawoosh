using System.Threading.Channels;
using Kawoosh.Server.Data.Network;
using Kawoosh.Server.Networking;

namespace Kawoosh.Server.Interfaces;

/// <summary>
/// Carries lines from the shared network channel onto the game loop's queue, and is the only
/// place that knows both the telnet layer and the game.
/// </summary>
public interface ISessionInputRouter
{
    /// <summary>
    /// Queues the greeting for a session that has just connected. Subscribe this to
    /// <see cref="ITelnetListener.SessionAccepted" />.
    /// </summary>
    void OnSessionAccepted(TelnetSession session);

    /// <summary>Reads until the channel completes or the token is cancelled.</summary>
    /// <param name="commands">The channel every session publishes its lines to.</param>
    /// <param name="cancellationToken">Stops the pump.</param>
    Task PumpAsync(ChannelReader<Command> commands, CancellationToken cancellationToken);
}
