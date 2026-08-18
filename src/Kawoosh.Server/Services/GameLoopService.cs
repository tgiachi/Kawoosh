using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Serilog;
using Kawoosh.Server.Data.Commands;
using Kawoosh.Server.Interfaces;
using Kawoosh.Server.Internal;

namespace Kawoosh.Server.Services;

/// <summary>
/// The game loop, as a scheduler. Commands arrive from any thread carrying the delay they
/// need, and run when they come due. The timer only sets the resolution: a command that asks
/// for no delay runs at the next tick instead of waiting for the world pulse.
/// World state is only ever touched from here, one command at a time.
/// </summary>
public sealed class GameLoopService : IGameLoopService, IDisposable
{
    private const int TickIntervalMilliseconds = 10;
    private const int WorldPulseMilliseconds = 250;

    private readonly ILogger _logger = Log.ForContext<GameLoopService>();
    private readonly IScreenService _screens;

    // The channel is the thread-safe doorway; the queue behind it is touched only by the loop,
    // so the scheduling order needs no lock.
    private readonly Channel<ScheduledCommand> _inbox = Channel.CreateUnbounded<ScheduledCommand>(
        new UnboundedChannelOptions { SingleReader = true }
    );

    // Everything still pending, so a caller holding a handle can reach its entry from any
    // thread. Entries leave when they come due or when they are cancelled, never later.
    private readonly ConcurrentDictionary<long, ScheduledEntry> _live = new();

    private readonly PriorityQueue<ScheduledEntry, (long DueAt, long Handle)> _pending = new();
    private readonly PeriodicTimer _timer = new(TimeSpan.FromMilliseconds(TickIntervalMilliseconds));
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private long _sequence;

    public GameLoopService(IScreenService screens)
    {
        _screens = screens;
    }

    /// <summary>
    /// World pulses run since the loop started. Monotonic, and the cheapest health signal the
    /// server has: a number that stops climbing means the loop stopped.
    /// </summary>
    public long WorldPulses { get; private set; }

    /// <summary>
    /// Schedules a command and returns the handle that cancels it. Returns
    /// <see cref="NotScheduled" /> once the loop has stopped, which is the caller's signal
    /// that the command will never run.
    /// </summary>
    /// <param name="command">The command to run.</param>
    /// <param name="delayMilliseconds">How long to wait first. 0 runs it at the next tick.</param>
    public long Enqueue(GameLoopCommand command, int delayMilliseconds = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(delayMilliseconds);

        var entry = new ScheduledEntry(Interlocked.Increment(ref _sequence), command);
        _live[entry.Handle] = entry;

        var scheduled = new ScheduledCommand(entry, _clock.ElapsedMilliseconds + delayMilliseconds);

        if (_inbox.Writer.TryWrite(scheduled))
        {
            return entry.Handle;
        }

        _live.TryRemove(entry.Handle, out _);
        _logger.Warning("Game loop has stopped, dropped {CommandType}", command.GetType().Name);

        return IGameLoopService.NotScheduled;
    }

    /// <summary>
    /// Cancels a scheduled command. Returns false when the handle is unknown, already
    /// cancelled, or belongs to a command that has already run — in every one of those cases
    /// there is nothing left to stop.
    /// </summary>
    public bool Cancel(long handle)
    {
        if (!_live.TryRemove(handle, out var entry))
        {
            return false;
        }

        // The entry stays in the priority queue; the loop drops it when its turn comes. Taking
        // it out of a heap would cost more than skipping it once.
        entry.Cancel();

        return true;
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
            _pending.Enqueue(scheduled.Entry, (scheduled.DueAtMilliseconds, scheduled.Entry.Handle));
        }

        var now = _clock.ElapsedMilliseconds;

        while (_pending.TryPeek(out _, out var due) && due.DueAt <= now)
        {
            var entry = _pending.Dequeue();
            _live.TryRemove(entry.Handle, out _);

            if (entry.IsCancelled)
            {
                continue;
            }

            Dispatch(entry.Command);
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
                case ShowScreenCommand show:
                    show.Session.Send(_screens.Render(show.ScreenName));

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
