using Kawoosh.Server.Data.Commands;
using Kawoosh.Server.Interfaces;

namespace Kawoosh.Tests.Support;

/// <summary>
/// A game loop that runs nothing and remembers what it was asked to run. Screens are tested
/// through their hooks, so what matters is which command a screen queued, not when it ran.
/// </summary>
public sealed class RecordingGameLoop : IGameLoopService
{
    private readonly List<GameLoopCommand> _commands = [];

    public IReadOnlyList<GameLoopCommand> Commands => _commands;

    public WorldTickCommand? LastPulse => null;

    public long Enqueue(GameLoopCommand command, int delayMilliseconds = 0)
    {
        _commands.Add(command);

        return _commands.Count;
    }

    public bool Cancel(long handle)
    {
        return false;
    }

    public Task ProcessAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
