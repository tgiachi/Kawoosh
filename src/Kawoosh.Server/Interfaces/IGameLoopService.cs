using Kawoosh.Server.Data.Commands;

namespace Kawoosh.Server.Interfaces;

/// <summary>
/// The game loop, as a scheduler: commands arrive from any thread carrying the delay they
/// need, and run when they come due on the single thread that owns world state.
/// </summary>
public interface IGameLoopService
{
    /// <summary>The handle returned when a command could not be scheduled.</summary>
    const long NotScheduled = 0;

    /// <summary>World pulses run since the loop started; a number that stops climbing means the loop stopped.</summary>
    long WorldPulses { get; }

    /// <summary>
    /// Schedules a command and returns the handle that cancels it, or
    /// <see cref="NotScheduled" /> once the loop has stopped.
    /// </summary>
    /// <param name="command">The command to run.</param>
    /// <param name="delayMilliseconds">How long to wait first. 0 runs it at the next tick.</param>
    long Enqueue(GameLoopCommand command, int delayMilliseconds = 0);

    /// <summary>
    /// Cancels a scheduled command. Returns false when the handle is unknown, already
    /// cancelled, or belongs to a command that has already run.
    /// </summary>
    /// <param name="handle">The handle returned by <see cref="Enqueue" />.</param>
    bool Cancel(long handle);

    /// <summary>Runs until cancelled.</summary>
    Task ProcessAsync(CancellationToken cancellationToken);
}
