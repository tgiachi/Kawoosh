using System.Text;
using System.Threading.Channels;
using Kawoosh.Server.Data.Commands;
using Kawoosh.Server.Data.Network;
using Kawoosh.Server.Interfaces;
using Kawoosh.Server.Networking;
using Kawoosh.Server.Services;
using Kawoosh.Tests.Support;
using Serilog;

namespace Kawoosh.Tests.Integration.Server.Screens;

/// <summary>
/// How the loop moves a session between screens. Socket-backed like the rest of the suite:
/// the hooks report themselves by writing to the client, so the assertions read the same
/// thing a player would.
/// </summary>
public class ScreenSwitchingTests
{
    private const int TimeoutMilliseconds = 5000;
    private const string IgnoredTemplate = "Session {SessionId} is on no screen; ignoring {Line}";

    private LoopbackConnection _connection = null!;
    private CancellationTokenSource _cancellation = null!;
    private TempScreenDirectory _screenDirectory = null!;
    private TempMessageDirectory _messageDirectory = null!;
    private ScreenService _art = null!;
    private TelnetSession _session = null!;
    private Task _sessionTask = null!;
    private CapturingLogSink _logSink = null!;

    [SetUp]
    public void SetUp()
    {
        _connection = new LoopbackConnection();
        _cancellation = new CancellationTokenSource(TimeoutMilliseconds);

        _screenDirectory = new TempScreenDirectory();

        // Art for "slow" only. Every other screen in these tests has none, which is the case
        // that must take the same path.
        _screenDirectory.Write("slow.sgs", "ART", "@delay 3000", "MORE");

        _messageDirectory = new TempMessageDirectory();
        _messageDirectory.Write("login.sgm", "login.name-prompt = Name? ");

        _art = new ScreenService(new VariableService());
        _art.Load(_screenDirectory.RootPath, []);

        _session = new TelnetSession(_connection.Server, Channel.CreateUnbounded<Command>().Writer);
        _sessionTask = _session.StartAsync(_cancellation.Token);
        _logSink = new();
    }

    [TearDown]
    public void TearDown()
    {
        _cancellation.Cancel();
        _session.Dispose();
        _connection.Dispose();
        _screenDirectory.Dispose();
        _messageDirectory.Dispose();
        _cancellation.Dispose();
    }

    [Test]
    public async Task Switch_FromNothing_EntersWithoutExitingAnything()
    {
        using var loop = NewLoop(new RecordingScreen("one"));
        var processing = loop.ProcessAsync(_cancellation.Token);

        loop.Enqueue(new SwitchScreenCommand(_session, "one"));

        var received = await ReadTextAsync("one:enter\r\n");

        Assert.Multiple(
            () =>
            {
                Assert.That(received, Is.EqualTo("one:enter\r\n"));
                Assert.That(_session.ScreenName, Is.EqualTo("one"));
            }
        );

        await _cancellation.CancelAsync();
        await processing;
    }

    [Test]
    public async Task Switch_BetweenScreens_ExitsTheOldBeforeEnteringTheNew()
    {
        using var loop = NewLoop(new RecordingScreen("one"), new RecordingScreen("two"));
        var processing = loop.ProcessAsync(_cancellation.Token);

        loop.Enqueue(new SwitchScreenCommand(_session, "one"));
        await ReadTextAsync("one:enter\r\n");

        loop.Enqueue(new SwitchScreenCommand(_session, "two"));

        var expected = "one:exit\r\ntwo:enter\r\n";
        var received = await ReadTextAsync(expected);

        Assert.That(received, Is.EqualTo(expected));

        await _cancellation.CancelAsync();
        await processing;
    }

    [Test]
    public async Task Switch_ToAnUnknownName_KeepsTheCurrentScreen()
    {
        // A session left on no screen is a live socket that answers nothing, which is worse
        // than ignoring a bad switch.
        using var loop = NewLoop(new RecordingScreen("one"));
        var processing = loop.ProcessAsync(_cancellation.Token);

        loop.Enqueue(new SwitchScreenCommand(_session, "one"));
        await ReadTextAsync("one:enter\r\n");

        loop.Enqueue(new SwitchScreenCommand(_session, "nowhere"));
        loop.Enqueue(new PlayerInputCommand(_session, "still here"));

        // No one:exit in between: the failed switch left the screen alone.
        var received = await ReadTextAsync("one:input:still here\r\n");

        Assert.Multiple(
            () =>
            {
                Assert.That(received, Is.EqualTo("one:input:still here\r\n"));
                Assert.That(_session.ScreenName, Is.EqualTo("one"));
            }
        );

        await _cancellation.CancelAsync();
        await processing;
    }

    [Test]
    public async Task Switch_ToAScreenWithArt_EntersOnlyOnceTheArtHasPlayed()
    {
        using var loop = NewLoop(new RecordingScreen("slow"));
        var processing = loop.ProcessAsync(_cancellation.Token);

        loop.Enqueue(new SwitchScreenCommand(_session, "slow"));

        // ART, then a three second gap, then MORE, and only then the hook. If OnEnter ran
        // when the screen was selected rather than when it was shown, a prompt would land on
        // top of the art.
        var expected = "ART\r\nMORE\r\nslow:enter\r\n";
        var received = await ReadTextAsync(expected);

        Assert.That(received, Is.EqualTo(expected));

        await _cancellation.CancelAsync();
        await processing;
    }

    [Test]
    public async Task Input_WhileArtIsPlaying_SkipsItAndStillEntersTheScreen()
    {
        // Load-bearing. Skipping flushes the rest of the art and runs the playback's
        // continuation, and that continuation is what enters the screen. Break it and a
        // player who presses enter through a banner lands on a session that stopped.
        using var loop = NewLoop(new RecordingScreen("slow"));
        var processing = loop.ProcessAsync(_cancellation.Token);

        loop.Enqueue(new SwitchScreenCommand(_session, "slow"));
        await ReadTextAsync("ART\r\n");

        loop.Enqueue(new PlayerInputCommand(_session, ""));

        var expected = "MORE\r\nslow:enter\r\n";
        var received = await ReadTextAsync(expected);

        Assert.That(received, Is.EqualTo(expected));

        await _cancellation.CancelAsync();
        await processing;
    }

    [Test]
    public async Task Switch_CalledInsideOnInput_HappensAfterOnInputReturns()
    {
        var one = new RecordingScreen("one");
        one.OnInputAction = (context, _) => context.Switch("two");

        using var loop = NewLoop(one, new RecordingScreen("two"));
        var processing = loop.ProcessAsync(_cancellation.Token);

        loop.Enqueue(new SwitchScreenCommand(_session, "one"));
        await ReadTextAsync("one:enter\r\n");

        loop.Enqueue(new PlayerInputCommand(_session, "go"));

        // The input marker comes first: the switch was queued, so OnExit could not run inside
        // the OnInput that asked for it.
        var expected = "one:input:go\r\none:exit\r\ntwo:enter\r\n";
        var received = await ReadTextAsync(expected);

        Assert.That(received, Is.EqualTo(expected));

        await _cancellation.CancelAsync();
        await processing;
    }

    [Test]
    public async Task Switch_ToTheScreenAlreadyCurrent_ExitsAndEntersItAgain()
    {
        // Re-showing a screen is a real thing to want, and treating it as a no-op would make
        // "switch to where I am" silently do nothing.
        using var loop = NewLoop(new RecordingScreen("one"));
        var processing = loop.ProcessAsync(_cancellation.Token);

        loop.Enqueue(new SwitchScreenCommand(_session, "one"));
        await ReadTextAsync("one:enter\r\n");

        loop.Enqueue(new SwitchScreenCommand(_session, "one"));

        var expected = "one:exit\r\none:enter\r\n";
        var received = await ReadTextAsync(expected);

        Assert.That(received, Is.EqualTo(expected));

        await _cancellation.CancelAsync();
        await processing;
    }

    [Test]
    public async Task OnInput_ThatThrows_DoesNotStopTheLoop()
    {
        var one = new RecordingScreen("one");
        one.OnInputAction = (_, _) => throw new InvalidOperationException("boom");

        using var loop = NewLoop(one);
        var processing = loop.ProcessAsync(_cancellation.Token);

        loop.Enqueue(new SwitchScreenCommand(_session, "one"));
        await ReadTextAsync("one:enter\r\n");

        loop.Enqueue(new PlayerInputCommand(_session, "first"));
        await ReadTextAsync("one:input:first\r\n");

        // One player's broken screen must not stop the world for everyone else.
        loop.Enqueue(new PlayerInputCommand(_session, "second"));

        var received = await ReadTextAsync("one:input:second\r\n");

        Assert.That(received, Is.EqualTo("one:input:second\r\n"));

        await _cancellation.CancelAsync();
        await processing;
    }

    [Test]
    public async Task OnEnter_ThatThrows_DoesNotStopTheLoop()
    {
        var one = new RecordingScreen("one");
        one.OnEnterAction = _ => throw new InvalidOperationException("boom");

        using var loop = NewLoop(one, new RecordingScreen("two"));
        var processing = loop.ProcessAsync(_cancellation.Token);

        loop.Enqueue(new SwitchScreenCommand(_session, "one"));
        await ReadTextAsync("one:enter\r\n");

        // The loop survived a screen that blew up on the way in.
        loop.Enqueue(new SwitchScreenCommand(_session, "two"));

        var expected = "one:exit\r\ntwo:enter\r\n";
        var received = await ReadTextAsync(expected);

        Assert.That(received, Is.EqualTo(expected));

        await _cancellation.CancelAsync();
        await processing;
    }

    [Test]
    public async Task OnExit_ThatThrows_AbandonsTheSwitchAndLeavesTheSessionWhereItWas()
    {
        // Dispatch catches the throw, which aborts SwitchScreen before the session's name is
        // reassigned. So the switch does not happen — and that is the safe outcome: the
        // session stays somewhere real rather than halfway between two screens. Asserted
        // because it is a consequence worth knowing, not because it was designed for.
        var one = new RecordingScreen("one");
        one.OnExitAction = _ => throw new InvalidOperationException("boom");

        using var loop = NewLoop(one, new RecordingScreen("two"));
        var processing = loop.ProcessAsync(_cancellation.Token);

        loop.Enqueue(new SwitchScreenCommand(_session, "one"));
        await ReadTextAsync("one:enter\r\n");

        loop.Enqueue(new SwitchScreenCommand(_session, "two"));
        await ReadTextAsync("one:exit\r\n");

        loop.Enqueue(new PlayerInputCommand(_session, "still working"));

        // Still "one": two never entered, and the loop is still running for everyone.
        var received = await ReadTextAsync("one:input:still working\r\n");

        Assert.Multiple(
            () =>
            {
                Assert.That(received, Is.EqualTo("one:input:still working\r\n"));
                Assert.That(_session.ScreenName, Is.EqualTo("one"));
            }
        );

        await _cancellation.CancelAsync();
        await processing;
    }

    [Test]
    public async Task Switch_AgainBeforeItsEnterRuns_DoesNotEnterTheAbandonedScreen()
    {
        // A screen with no art has nothing to wait on, so SwitchScreen enqueues its
        // ScreenEnteredCommand rather than running OnEnter there and then. Both switches are
        // queued from inside one hook rather than as two loop.Enqueue calls raced against the
        // loop's own timer from the test thread: that keeps the test deterministic, since both
        // writes land during the same dispatch and are only drained on the tick after. This
        // reproduces the race the guard in EnterScreen defends: without it, "one" would enter
        // after the session had already moved on to "two".
        var start = new RecordingScreen("start");
        start.OnInputAction = (context, _) =>
        {
            context.Switch("one");
            context.Switch("two");
        };

        using var loop = NewLoop(start, new RecordingScreen("one"), new RecordingScreen("two"));
        var processing = loop.ProcessAsync(_cancellation.Token);

        loop.Enqueue(new SwitchScreenCommand(_session, "start"));
        await ReadTextAsync("start:enter\r\n");

        loop.Enqueue(new PlayerInputCommand(_session, "go"));

        // No one:enter: the session left "one" before its own entry could run. "one:exit"
        // still shows up — switching straight through to "two" exits "one" on the way, even
        // though "one" itself never entered; see IScreen.OnExit for why that is expected.
        var expected = "start:input:go\r\nstart:exit\r\none:exit\r\ntwo:enter\r\n";
        var received = await ReadTextAsync(expected);

        Assert.That(received, Is.EqualTo(expected));

        await _cancellation.CancelAsync();
        await processing;
    }

    [Test]
    public async Task Switch_TwiceToTheSameScreenInOneDispatch_EntersOnlyOnce()
    {
        // A guard keyed on the screen's name cannot tell a stale entry from the current one
        // when both switches named the same screen — "one" equals "one" either way. Only the
        // generation, incremented on every switch regardless of where it goes, can: the first
        // switch's entry is behind by the time it is dispatched, the second is not.
        var start = new RecordingScreen("start");
        start.OnInputAction = (context, _) =>
        {
            context.Switch("one");
            context.Switch("one");
        };

        using var loop = NewLoop(start, new RecordingScreen("one"));
        var processing = loop.ProcessAsync(_cancellation.Token);

        loop.Enqueue(new SwitchScreenCommand(_session, "start"));
        await ReadTextAsync("start:enter\r\n");

        loop.Enqueue(new PlayerInputCommand(_session, "go"));

        // A read bounded to exactly one one:enter would still pass if a second one followed
        // right behind it — the extra bytes would simply be left unread, past the boundary
        // this call stops at. So the real check is what comes next: a second read, bounded
        // to the input marker alone. A stray one:enter sitting ahead of it in the stream
        // would push that marker out of the window and turn up here as a mismatch instead.
        await ReadTextAsync("start:input:go\r\nstart:exit\r\none:exit\r\none:enter\r\n");

        loop.Enqueue(new PlayerInputCommand(_session, "still here"));

        var expected = "one:input:still here\r\n";
        var received = await ReadTextAsync(expected);

        Assert.That(received, Is.EqualTo(expected));

        await _cancellation.CancelAsync();
        await processing;
    }

    [Test]
    public async Task Input_BeforeAnySwitch_IsIgnored()
    {
        // A session with no screen has nowhere to send a line. It must not throw, and it must
        // not stop the loop for anyone else. Asserting on the client's bytes alone cannot
        // distinguish that from a NullReferenceException Dispatch's catch-all swallowed —
        // both leave the client with nothing but "one:enter\r\n" — so this also has to see
        // the debug line that only the clean ignore path writes.
        using var logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(_logSink).CreateLogger();
        var previous = Log.Logger;
        Log.Logger = logger;

        try
        {
            // The loop binds its logger at construction, so the sink has to be live first.
            using var loop = NewLoop(new RecordingScreen("one"));
            var processing = loop.ProcessAsync(_cancellation.Token);

            loop.Enqueue(new PlayerInputCommand(_session, "into the void"));
            loop.Enqueue(new SwitchScreenCommand(_session, "one"));

            // Only the switch produced output: the earlier line went nowhere.
            var received = await ReadTextAsync("one:enter\r\n");

            Assert.Multiple(
                () =>
                {
                    Assert.That(received, Is.EqualTo("one:enter\r\n"));
                    Assert.That(_logSink.Count(IgnoredTemplate), Is.EqualTo(1));
                }
            );

            await _cancellation.CancelAsync();
            await processing;
        }
        finally
        {
            Log.Logger = previous;
        }
    }

    private GameLoopService NewLoop(params IScreen[] screens)
    {
        return new GameLoopService(_art, new ScreenManager(screens));
    }

    private async Task<string> ReadTextAsync(string expected)
    {
        var stream = _connection.Client.GetStream();
        var wanted = Encoding.UTF8.GetByteCount(expected);
        var received = new byte[wanted];
        var total = 0;

        while (total < wanted)
        {
            // Bounded by what is left to want, so the OS cannot hand back more than was asked
            // for. An oversized buffer would swallow whatever the server wrote next and the
            // call that wanted those bytes would block until the test's cancellation fired.
            var read = await stream.ReadAsync(received.AsMemory(total), _cancellation.Token);

            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return Encoding.UTF8.GetString(received, 0, total);
    }
}
