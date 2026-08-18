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

namespace Kawoosh.Server.Services;

/// <summary>
/// The game loop, as a scheduler. Commands arrive from any thread carrying the delay they
/// need, and run when they come due. The timer only sets the resolution: a command that asks
/// for no delay runs at the next tick instead of waiting for the world pulse.
/// World state is only ever touched from here, one command at a time.
/// </summary>
public sealed class GameLoopService : IGameLoopService, IDisposable
{
    private const string GreetingScreen = "greeting";

    private const int TickIntervalMilliseconds = 10;
    private const int WorldPulseMilliseconds = 250;

    private readonly ILogger _logger = Log.ForContext<GameLoopService>();
    private readonly IScreenService _screens;
    private readonly ISessionFlowService _flow;
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

    public GameLoopService(IScreenService screens, ISessionFlowService flow, IScreenManager screenManager)
    {
        _screens = screens;
        _flow = flow;
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
                case BeginSessionFlowCommand begin:
                    _flow.Begin(begin.Session);

                    break;
                case SessionConnectedCommand connected:
                    ShowGreeting(connected.Session);

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
        if (!_screens.TryGetArt(show.ScreenName, out var art))
        {
            _logger.Warning("No screen named {ScreenName} to show", show.ScreenName);

            return;
        }

        Play(show.Session, Compile(show.ScreenName, art.ClearsScreen), null);
    }

    private void ShowGreeting(TelnetSession session)
    {
        if (!_screens.TryGetArt(GreetingScreen, out var art))
        {
            // No banner is survivable; a session left with no prompt is not.
            _logger.Warning("No screen named {ScreenName} to show", GreetingScreen);
            _flow.Begin(session);

            return;
        }

        Play(session, Compile(GreetingScreen, art.ClearsScreen), new BeginSessionFlowCommand(session));
    }

    private void Input(TelnetSession session, string line)
    {
        if (_screenManager.TryGetScreen(session.ScreenName, out var screen))
        {
            screen.OnInput(new ScreenContext(this, session), line);

            return;
        }

        // Task 5 deletes this arm: until the four screens are wired, a session that has not
        // been switched anywhere is still driven by the old flow.
        _flow.Handle(session, line);
    }

    private void SwitchScreen(SwitchScreenCommand command)
    {
        if (!_screenManager.TryGetScreen(command.ScreenName, out var next))
        {
            // A session on no screen is a live socket that answers nothing, so a bad switch
            // leaves it where it was.
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
            current.OnExit(context);
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
    /// Renders a screen and compiles it. The clear goes in before compiling, so it belongs to
    /// the first step and cannot be seen apart from the art it clears for.
    /// </summary>
    private TextScript Compile(string screenName, bool clearsScreen)
    {
        var text = _screens.Render(screenName);

        return TextScriptCompiler.Compile(clearsScreen ? AnsiControl.ClearScreen + text : text);
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
