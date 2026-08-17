using System.Threading.Channels;
using Kawoosh.Server.Data.Commands;
using Kawoosh.Server.Data.Network;

namespace Kawoosh.Server.Services;

/// <summary>
/// Carries lines from the shared network channel onto the game loop's queue. This is the seam
/// that keeps the telnet layer ignorant of game types: it is the only place that knows both.
/// </summary>
public class SessionInputRouter
{
    private readonly GameLoopService _gameLoop;

    public SessionInputRouter(GameLoopService gameLoop)
    {
        _gameLoop = gameLoop;
    }

    public async Task PumpAsync(ChannelReader<Command> commands, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var command in commands.ReadAllAsync(cancellationToken))
            {
                // No delay: a typed line should reach the world at the next tick, not at the
                // next world pulse.
                _gameLoop.Enqueue(new PlayerInputCommand(command.Session, command.Text));
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown, not a fault.
        }
    }
}
