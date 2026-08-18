using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Serilog;
using Kawoosh.Server.Data.Commands;
using Kawoosh.Server.Data.Screens;
using Kawoosh.Server.Interfaces;
using Kawoosh.Server.Data.Text;
using Kawoosh.Server.Internal;
using Kawoosh.Server.Networking;
using Kawoosh.Server.Networking.Internal;
using Kawoosh.Server.Screens;

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
    private readonly IScreenManager _screenManager;

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
    private TimeSpan _lastPulseAt;

    public GameLoopService(IScreenService screens, IScreenManager screenManager)
    {
        _screens = screens;
        _screenManager = screenManager;
    }

    /// <inheritdoc />
    public WorldTickCommand? LastPulse { get; private set; }

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
        Enqueue(new WorldPulseDue(), WorldPulseMilliseconds);

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
                case PlayerInputCommand input when input.Session.Playback is { } playback:
                    Skip(input.Session, playback);

                    break;
                case PlayerInputCommand input:
                    Input(input.Session, input.Text);

                    break;
                case SwitchScreenCommand switchTo:
                    SwitchScreen(switchTo);

                    break;
                case ScreenEnteredCommand entered:
                    EnterScreen(entered);

                    break;
                case PlayScriptCommand play:
                    Advance(play);

                    break;
                case SessionConnectedCommand connected:
                    SwitchScreen(new SwitchScreenCommand(connected.Session, GreetingScreen.ScreenName));

                    break;
                case ShowScreenCommand show:
                    ShowScreen(show);

                    break;
                case WorldPulseDue:
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

    private void ShowScreen(ShowScreenCommand show)
    {
        if (!_screens.TryGetArt(show.ScreenName, out _))
        {
            _logger.Warning("No screen named {ScreenName} to show", show.ScreenName);

            return;
        }

        Play(show.Session, ArtFor(show.ScreenName), null);
    }

    private void Input(TelnetSession session, string line)
    {
        // One or more switches were queued for this session but have not fully landed yet.
        // Delivering the line now could hand it to the screen the session is leaving, or to
        // the one it is going to before that screen's own OnEnter — and therefore its prompt
        // — has run. So it goes back on the queue instead, behind whichever step of the
        // switch is still ahead of it; each step it waits behind is one it cannot loop past,
        // so this terminates rather than requeuing forever.
        if (session.SwitchesInFlight > 0)
        {
            Enqueue(new PlayerInputCommand(session, line));

            return;
        }

        if (!_screenManager.TryGetScreen(session.ScreenName, out var screen))
        {
            _logger.Debug("Session {SessionId} is on no screen; ignoring {Line}", session.Id, line);

            return;
        }

        screen.OnInput(new ScreenContext(this, session), line);
    }

    private void SwitchScreen(SwitchScreenCommand command)
    {
        if (!_screenManager.TryGetScreen(command.ScreenName, out var next))
        {
            // A session on no screen is a live socket that answers nothing, so a bad switch
            // leaves it where it was. Decremented here because a failed switch has no OnEnter
            // coming to account for its increment later: nothing else is left in flight to
            // wait on.
            ResolveSwitch(command.Session);

            _logger.Error(
                "No screen named {ScreenName}; session {SessionId} stays on {CurrentScreen}",
                command.ScreenName,
                command.Session.Id,
                command.Session.ScreenName
            );

            return;
        }

        var context = new ScreenContext(this, command.Session);

        if (_screenManager.TryGetScreen(command.Session.ScreenName, out var current))
        {
            try
            {
                current.OnExit(context);
            }
            catch (Exception exception)
            {
                // Dispatch's own catch would otherwise swallow this and skip everything below
                // it, including the decrement — stranding the session with input held back
                // forever. Logged and decremented here instead, so a throwing OnExit abandons
                // the switch without freezing the session it belongs to.
                _logger.Error(
                    exception,
                    "Screen {ScreenName} threw from OnExit; session {SessionId} stays on it",
                    current.Name,
                    command.Session.Id
                );

                ResolveSwitch(command.Session);

                return;
            }
        }

        // The screen's own name, not the one asked for, so the session records it in one case.
        command.Session.ScreenName = next.Name;
        command.Session.ScreenGeneration++;

        Play(
            command.Session,
            ArtFor(next.Name),
            new ScreenEnteredCommand(command.Session, next.Name, command.Session.ScreenGeneration)
        );
    }

    private void EnterScreen(ScreenEnteredCommand command)
    {
        // Decremented first, stale or not: this entry still holds the increment Switch made
        // for it, and a stale entry has no other point in its life where that gets undone. If
        // this ran after the guard below instead, a stale entry would return before reaching
        // it and the count would never work its way back down to zero.
        ResolveSwitch(command.Session);

        // This is the reason ScreenGeneration exists: the session may have switched again
        // since this entry was queued — possibly back to a screen of the same name — and a
        // name alone cannot tell "still the switch that queued me" from "a later switch that
        // happens to agree on where it went". Only the generation can, because it changes on
        // every switch regardless of the name involved.
        if (command.Session.ScreenGeneration != command.Generation)
        {
            return;
        }

        if (_screenManager.TryGetScreen(command.ScreenName, out var screen))
        {
            screen.OnEnter(new ScreenContext(this, command.Session));
        }
    }

    /// <summary>
    /// Resolves one switch's share of <see cref="TelnetSession.SwitchesInFlight" />, floored
    /// at zero. A switch queued directly rather than through <see cref="ScreenContext.Switch" />
    /// — a session's very first, or one built straight from a command in a test — never
    /// incremented the count in the first place. Without the floor, resolving it would still
    /// decrement, leaving the count permanently negative; a later, real switch would then
    /// have to climb out of that hole before the count could read positive again, so a second
    /// switch queued alongside it could resolve first and read the count as idle while the
    /// first was still outstanding.
    /// </summary>
    private static void ResolveSwitch(TelnetSession session)
    {
        if (session.SwitchesInFlight > 0)
        {
            session.SwitchesInFlight--;
        }
    }

    /// <summary>
    /// Compiles the art for a screen. A screen with no .sgs compiles an empty string, which
    /// is an empty script — and Play runs the continuation immediately for one of those, so a
    /// screen with art and a screen without take the same path.
    /// </summary>
    private TextScript ArtFor(string screenName)
    {
        if (!_screens.TryGetArt(screenName, out var art))
        {
            return TextScriptCompiler.Compile("");
        }

        var text = _screens.Render(screenName);

        return TextScriptCompiler.Compile(art.ClearsScreen ? AnsiControl.ClearScreen + text : text);
    }

    /// <summary>
    /// Starts a script. An instant one is sent here and now, which keeps the ordering a
    /// caller has today; anything timed goes through the scheduler.
    /// </summary>
    private void Play(TelnetSession session, TextScript script, GameLoopCommand? then)
    {
        // Whatever this session was showing is abandoned. Without this its pending step would
        // find the new playback on the session and write its own position into it.
        if (session.Playback is { } previous)
        {
            Cancel(previous.Handle);
            session.Playback = null;
        }

        if (script.Steps.Count == 0)
        {
            RunContinuation(then);

            return;
        }

        if (script.IsInstant)
        {
            SendStep(session, script.Steps[0]);
            RunContinuation(then);

            return;
        }

        var playback = new Playback(script, then);
        session.Playback = playback;

        playback.Handle = Enqueue(
            new PlayScriptCommand(session, script, 0, then),
            script.Steps[0].DelayMilliseconds
        );
    }

    private void Advance(PlayScriptCommand play)
    {
        // A playback the session no longer owns was skipped or replaced: its remaining text
        // has already been dealt with, and sending it again would double it.
        if (play.Session.Playback is not { } playback
            || !ReferenceEquals(playback.Script, play.Script)
            || playback.Handle == IGameLoopService.NotScheduled)
        {
            return;
        }

        SendStep(play.Session, play.Script.Steps[play.Index]);

        var next = play.Index + 1;

        if (next >= play.Script.Steps.Count)
        {
            play.Session.Playback = null;
            RunContinuation(play.Then);

            return;
        }

        playback.NextIndex = next;
        playback.Handle = Enqueue(
            new PlayScriptCommand(play.Session, play.Script, next, play.Then),
            play.Script.Steps[next].DelayMilliseconds
        );
    }

    /// <summary>
    /// Shows everything a playback has left, at once, and runs its continuation. The line
    /// that caused this is not a command: someone pressing enter to get past an intro did not
    /// mean to type anything.
    /// </summary>
    private void Skip(TelnetSession session, Playback playback)
    {
        Cancel(playback.Handle);
        session.Playback = null;

        for (var index = playback.NextIndex; index < playback.Script.Steps.Count; index++)
        {
            SendStep(session, playback.Script.Steps[index]);
        }

        RunContinuation(playback.Then);
    }

    private void RunContinuation(GameLoopCommand? then)
    {
        if (then is not null)
        {
            Enqueue(then);
        }
    }

    private static void SendStep(TelnetSession session, TextStep step)
    {
        if (step.Terminate)
        {
            session.Send(step.Text);

            return;
        }

        session.SendPrompt(step.Text);
    }

    private void Pulse()
    {
        // Measured now, not when this pulse was scheduled: a pulse the loop ran late reports
        // the time that really passed, so simulation stays proportional to it.
        var uptime = _clock.Elapsed;
        var sincePrevious = uptime - _lastPulseAt;
        _lastPulseAt = uptime;

        var tick = new WorldTickCommand((LastPulse?.PulseNumber ?? 0) + 1, uptime, sincePrevious);
        LastPulse = tick;

        // Periodic world updates — regeneration, mob movement, weather — belong here, and
        // scale by tick.SincePrevious rather than by the number of pulses.

        Enqueue(new WorldPulseDue(), WorldPulseMilliseconds);
    }

    public void Dispose()
    {
        _timer.Dispose();
    }
}
