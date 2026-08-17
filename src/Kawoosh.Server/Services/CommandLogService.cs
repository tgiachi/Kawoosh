using System.Threading.Channels;
using Kawoosh.Server.Data.Network;
using Serilog;

namespace Kawoosh.Server.Services;

/// <summary>
/// Placeholder consumer of the shared command channel: logs every line and echoes it back
/// to its session. Replaced by the game loop once that exists.
/// </summary>
public class CommandLogService
{
    private readonly ILogger _logger = Log.ForContext<CommandLogService>();

    public async Task PumpAsync(ChannelReader<Command> commands, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var command in commands.ReadAllAsync(cancellationToken))
            {
                _logger.Information(
                    "Session {SessionId} sent {Text}",
                    command.Session.Id,
                    command.Text
                );

                command.Session.Send($"echo: {command.Text}");
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown, not a fault.
        }
    }
}
