using System.Diagnostics;
using System.Threading.Channels;
using Serilog;
using Kawoosh.Server.Data.Commands;
using Kawoosh.Server.Internal;

namespace Kawoosh.Server.Services;

/// <summary>
/// The game loop, as a scheduler. Commands arrive from any thread carrying the delay they
/// need, and run when they come due. The timer only sets the resolution: a command that asks
/// for no delay runs at the next tick instead of waiting for the world pulse.
/// World state is only ever touched from here, one command at a time.
/// </summary>
public sealed class GameLoopService : IDisposable
{
    private const int TickIntervalMilliseconds = 10;
    private const int WorldPulseMilliseconds = 250;

    private readonly ILogger _logger = Log.ForContext<GameLoopService>();

    // The channel is the thread-safe doorway; the queue behind it is touched only by the loop,
    // so the scheduling order needs no lock.
    private readonly Channel<ScheduledCommand> _inbox = Channel.CreateUnbounded<ScheduledCommand>(
        new UnboundedChannelOptions { SingleReader = true }
    );

    private readonly PriorityQueue<GameLoopCommand, (long DueAt, long Sequence)> _pending = new();
    private readonly PeriodicTimer _timer = new(TimeSpan.FromMilliseconds(TickIntervalMilliseconds));
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private long _sequence;

    /// <summary>
    /// World pulses run since the loop started. Monotonic, and the cheapest health signal the
    /// server has: a number that stops climbing means the loop stopped.
    /// </summary>
    public long WorldPulses { get; private set; }

    /// <summary>
    /// Schedules a command. Returns false once the loop has stopped, which is the caller's
    /// signal that the command will never run.
    /// </summary>
    /// <param name="command">The command to run.</param>
    /// <param name="delayMilliseconds">How long to wait first. 0 runs it at the next tick.</param>
    public bool Enqueue(GameLoopCommand command, int delayMilliseconds = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(delayMilliseconds);

        var scheduled = new ScheduledCommand(
            command,
            _clock.ElapsedMilliseconds + delayMilliseconds,
            Interlocked.Increment(ref _sequence)
        );

        if (_inbox.Writer.TryWrite(scheduled))
        {
            return true;
        }

        _logger.Warning("Game loop has stopped, dropped {CommandType}", command.GetType().Name);

        return false;
    }

    /// <summary>
    /// Runs until cancelled. Ticks are paced by <see cref="PeriodicTimer" />, which drops
    /// missed ticks rather than accumulating a backlog when a tick overruns.
    /// </summary>
    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        Enqueue(new WorldTickCommand(), WorldPulseMilliseconds);

        try
        {
            while (await _timer.WaitForNextTickAsync(cancellationToken))
            {
                Tick();
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown, not a fault.
        }
        finally
        {
            // Reject late arrivals instead of letting them queue for a tick that never comes.
            _inbox.Writer.TryComplete();
        }
    }

    private void Tick()
    {
        while (_inbox.Reader.TryRead(out var scheduled))
        {
            _pending.Enqueue(scheduled.Command, (scheduled.DueAtMilliseconds, scheduled.Sequence));
        }

        var now = _clock.ElapsedMilliseconds;

        while (_pending.TryPeek(out _, out var due) && due.DueAt <= now)
        {
            Dispatch(_pending.Dequeue());
        }
    }

    private void Dispatch(GameLoopCommand command)
    {
        // One player's bad command must never stop the world for everyone else.
        try
        {
            switch (command)
            {
                case PlayerInputCommand input:
                    input.Session.Send($"echo: {input.Text}");

                    break;
                case WorldTickCommand:
                    Pulse();

                    break;
                default:
                    _logger.Warning("Game loop has no handler for {CommandType}", command.GetType().Name);

                    break;
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Game loop command {CommandType} failed", command.GetType().Name);
        }
    }

    private void Pulse()
    {
        WorldPulses++;

        // Periodic world updates — regeneration, mob movement, weather — belong here.

        Enqueue(new WorldTickCommand(), WorldPulseMilliseconds);
    }

    public void Dispose()
    {
        _timer.Dispose();
    }
}
