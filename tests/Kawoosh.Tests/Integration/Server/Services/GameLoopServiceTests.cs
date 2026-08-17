using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using Kawoosh.Server.Data.Commands;
using Kawoosh.Server.Data.Network;
using Kawoosh.Server.Networking;
using Kawoosh.Server.Services;
using Kawoosh.Tests.Support;

namespace Kawoosh.Tests.Integration.Server.Services;

/// <summary>
/// Tests for the game loop scheduler. Socket-backed because the loop's only observable effect
/// today is the text it writes back to a session, and a real session is cheaper to reason
/// about than a fake that would only prove the fake works.
/// </summary>
public class GameLoopServiceTests
{
    private const int TimeoutMilliseconds = 5000;

    private LoopbackConnection _connection = null!;
    private CancellationTokenSource _cancellation = null!;
    private GameLoopService _gameLoop = null!;
    private TelnetSession _session = null!;
    private Task _sessionTask = null!;

    [SetUp]
    public void SetUp()
    {
        _connection = new LoopbackConnection();
        _cancellation = new CancellationTokenSource(TimeoutMilliseconds);
        _gameLoop = new GameLoopService();
        _session = new TelnetSession(_connection.Server, Channel.CreateUnbounded<Command>().Writer);
        _sessionTask = _session.StartAsync(_cancellation.Token);
    }

    [TearDown]
    public void TearDown()
    {
        _cancellation.Cancel();
        _gameLoop.Dispose();
        _session.Dispose();
        _connection.Dispose();
        _cancellation.Dispose();
    }

    [Test]
    public async Task ProcessAsync_CommandWithNoDelay_RunsWithinATickOrTwo()
    {
        var processing = _gameLoop.ProcessAsync(_cancellation.Token);
        var elapsed = Stopwatch.StartNew();

        _gameLoop.Enqueue(new PlayerInputCommand(_session, "look"));
        var received = await ReadFromClientAsync("echo: look\r\n");
        elapsed.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(received, Is.EqualTo("echo: look\r\n"));

            // The tick is 10 ms. The generous bound still proves the command did not wait
            // for the 250 ms world pulse, which is the regression this design removes.
            Assert.That(elapsed.ElapsedMilliseconds, Is.LessThan(200));
        });

        await _cancellation.CancelAsync();
        await processing;
    }

    [Test]
    public async Task ProcessAsync_CommandWithADelay_RunsCloseToItsDueTime()
    {
        var processing = _gameLoop.ProcessAsync(_cancellation.Token);
        var elapsed = Stopwatch.StartNew();

        _gameLoop.Enqueue(new PlayerInputCommand(_session, "slow"), 300);
        await ReadFromClientAsync("echo: slow\r\n");
        elapsed.Stop();

        Assert.Multiple(() =>
        {
            // Never early: the delay is a contract, not a hint.
            Assert.That(elapsed.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(280));

            // Never much late either. The generous ceiling still fails if a delayed command
            // were held to the 250 ms world pulse boundary instead of its own due time.
            Assert.That(elapsed.ElapsedMilliseconds, Is.LessThan(500));
        });

        await _cancellation.CancelAsync();
        await processing;
    }

    [Test]
    public async Task ProcessAsync_DelayedCommand_DoesNotHoldBackOneDueSooner()
    {
        var processing = _gameLoop.ProcessAsync(_cancellation.Token);
        var elapsed = Stopwatch.StartNew();

        // The long delay is queued first: a scheduler that ran its queue in arrival order
        // would make "fast" wait 400 ms behind it.
        _gameLoop.Enqueue(new PlayerInputCommand(_session, "slow"), 400);
        _gameLoop.Enqueue(new PlayerInputCommand(_session, "fast"));

        await ReadFromClientAsync("echo: fast\r\n");
        var fastAt = elapsed.ElapsedMilliseconds;

        await ReadFromClientAsync("echo: slow\r\n");
        var slowAt = elapsed.ElapsedMilliseconds;

        Assert.Multiple(() =>
        {
            Assert.That(fastAt, Is.LessThan(200));
            Assert.That(slowAt, Is.GreaterThanOrEqualTo(380));
        });

        await _cancellation.CancelAsync();
        await processing;
    }

    [Test]
    public async Task ProcessAsync_SeveralDifferentDelays_RunInAscendingDueOrder()
    {
        var processing = _gameLoop.ProcessAsync(_cancellation.Token);

        // Queued longest-first, so arrival order and due order disagree completely.
        _gameLoop.Enqueue(new PlayerInputCommand(_session, "third"), 300);
        _gameLoop.Enqueue(new PlayerInputCommand(_session, "first"), 60);
        _gameLoop.Enqueue(new PlayerInputCommand(_session, "second"), 180);

        const string expected = "echo: first\r\necho: second\r\necho: third\r\n";
        var received = await ReadFromClientAsync(expected);

        Assert.That(received, Is.EqualTo(expected));

        await _cancellation.CancelAsync();
        await processing;
    }

    [Test]
    public async Task ProcessAsync_DelayedCommand_RunsOnceAndNotOnEveryTickAfterItIsDue()
    {
        var processing = _gameLoop.ProcessAsync(_cancellation.Token);

        _gameLoop.Enqueue(new PlayerInputCommand(_session, "once"), 60);
        _gameLoop.Enqueue(new PlayerInputCommand(_session, "sentinel"), 400);

        // The sentinel lands ~34 ticks after "once" came due. If a due command were dispatched
        // on every later tick rather than dequeued, those repeats would sit between the two
        // and this exact-match assertion would fail.
        const string expected = "echo: once\r\necho: sentinel\r\n";
        var received = await ReadFromClientAsync(expected);

        Assert.That(received, Is.EqualTo(expected));

        await _cancellation.CancelAsync();
        await processing;
    }

    [Test]
    public async Task ProcessAsync_DelayIsMeasuredFromEnqueue_NotFromTheStartOfTheLoop()
    {
        var processing = _gameLoop.ProcessAsync(_cancellation.Token);

        await Task.Delay(200, _cancellation.Token);

        var elapsed = Stopwatch.StartNew();
        _gameLoop.Enqueue(new PlayerInputCommand(_session, "late"), 200);
        await ReadFromClientAsync("echo: late\r\n");
        elapsed.Stop();

        // A due time computed from the loop's start would already be in the past here, and
        // the command would run immediately.
        Assert.That(elapsed.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(180));

        await _cancellation.CancelAsync();
        await processing;
    }

    [Test]
    public async Task ProcessAsync_CommandsQueuedOutOfDueOrder_RunInDueOrder()
    {
        var processing = _gameLoop.ProcessAsync(_cancellation.Token);

        _gameLoop.Enqueue(new PlayerInputCommand(_session, "slow"), 250);
        _gameLoop.Enqueue(new PlayerInputCommand(_session, "fast"), 20);

        const string expected = "echo: fast\r\necho: slow\r\n";
        var received = await ReadFromClientAsync(expected);

        Assert.That(received, Is.EqualTo(expected));

        await _cancellation.CancelAsync();
        await processing;
    }

    [Test]
    public async Task ProcessAsync_CommandsDueAtTheSameInstant_KeepTheirEnqueueOrder()
    {
        var processing = _gameLoop.ProcessAsync(_cancellation.Token);

        // Same delay means the same due time: the tie must break on arrival order, or a
        // player's "north" then "look" could reach the world reversed.
        _gameLoop.Enqueue(new PlayerInputCommand(_session, "one"));
        _gameLoop.Enqueue(new PlayerInputCommand(_session, "two"));
        _gameLoop.Enqueue(new PlayerInputCommand(_session, "three"));

        const string expected = "echo: one\r\necho: two\r\necho: three\r\n";
        var received = await ReadFromClientAsync(expected);

        Assert.That(received, Is.EqualTo(expected));

        await _cancellation.CancelAsync();
        await processing;
    }

    [Test]
    public async Task ProcessAsync_CommandThatThrows_DoesNotStopTheLoop()
    {
        var processing = _gameLoop.ProcessAsync(_cancellation.Token);

        // A null session makes dispatch throw. One bad command must not take the world down.
        _gameLoop.Enqueue(new PlayerInputCommand(null!, "boom"));
        _gameLoop.Enqueue(new PlayerInputCommand(_session, "survivor"));

        var received = await ReadFromClientAsync("echo: survivor\r\n");

        Assert.Multiple(() =>
        {
            Assert.That(received, Is.EqualTo("echo: survivor\r\n"));
            Assert.That(processing.IsFaulted, Is.False);
        });

        await _cancellation.CancelAsync();
        await processing;
    }

    [Test]
    public async Task ProcessAsync_WorldPulse_KeepsReschedulingItself()
    {
        var processing = _gameLoop.ProcessAsync(_cancellation.Token);

        await Task.Delay(700, _cancellation.Token);

        Assert.Multiple(() =>
        {
            // The pulse is 250 ms and reschedules from its own handler, so ~700 ms must have
            // produced more than one. A pulse that fired once and stopped would read 1.
            Assert.That(_gameLoop.WorldPulses, Is.GreaterThanOrEqualTo(2));

            // And it must honour its own 250 ms delay rather than riding the 10 ms tick:
            // that mistake would show up here as roughly seventy pulses, not three.
            Assert.That(_gameLoop.WorldPulses, Is.LessThanOrEqualTo(5));
        });

        await _cancellation.CancelAsync();
        await processing;
    }

    [Test]
    public async Task ProcessAsync_Cancelled_CompletesWithoutThrowing()
    {
        var processing = _gameLoop.ProcessAsync(_cancellation.Token);

        await _cancellation.CancelAsync();

        Assert.That(async () => await processing, Throws.Nothing);
    }

    [Test]
    public void Enqueue_WhileTheLoopIsRunning_ReturnsAHandle()
    {
        _ = _gameLoop.ProcessAsync(_cancellation.Token);

        Assert.That(_gameLoop.Enqueue(new PlayerInputCommand(_session, "look")), Is.Not.Zero);
    }

    [Test]
    public async Task Enqueue_AfterTheLoopStopped_ReturnsNotScheduled()
    {
        var processing = _gameLoop.ProcessAsync(_cancellation.Token);

        await _cancellation.CancelAsync();
        await processing;

        Assert.That(
            _gameLoop.Enqueue(new PlayerInputCommand(_session, "look")),
            Is.EqualTo(GameLoopService.NotScheduled)
        );
    }

    [Test]
    public async Task Cancel_BeforeTheCommandIsDue_StopsItFromRunning()
    {
        var processing = _gameLoop.ProcessAsync(_cancellation.Token);

        var handle = _gameLoop.Enqueue(new PlayerInputCommand(_session, "cancelled"), 150);
        _gameLoop.Enqueue(new PlayerInputCommand(_session, "sentinel"), 400);

        var cancelled = _gameLoop.Cancel(handle);

        // The sentinel is due long after the cancelled command was. If cancellation had not
        // taken, "echo: cancelled" would sit ahead of it and this exact match would fail.
        const string expected = "echo: sentinel\r\n";
        var received = await ReadFromClientAsync(expected);

        Assert.Multiple(() =>
        {
            Assert.That(cancelled, Is.True);
            Assert.That(received, Is.EqualTo(expected));
        });

        await _cancellation.CancelAsync();
        await processing;
    }

    [Test]
    public async Task Cancel_OneOfSeveral_LeavesTheOthersAlone()
    {
        var processing = _gameLoop.ProcessAsync(_cancellation.Token);

        _gameLoop.Enqueue(new PlayerInputCommand(_session, "first"), 60);
        var handle = _gameLoop.Enqueue(new PlayerInputCommand(_session, "second"), 120);
        _gameLoop.Enqueue(new PlayerInputCommand(_session, "third"), 180);

        _gameLoop.Cancel(handle);

        const string expected = "echo: first\r\necho: third\r\n";
        var received = await ReadFromClientAsync(expected);

        Assert.That(received, Is.EqualTo(expected));

        await _cancellation.CancelAsync();
        await processing;
    }

    [Test]
    public void Cancel_APendingCommand_ReportsThatItCancelledSomething()
    {
        _ = _gameLoop.ProcessAsync(_cancellation.Token);
        var handle = _gameLoop.Enqueue(new PlayerInputCommand(_session, "later"), 5000);

        Assert.That(_gameLoop.Cancel(handle), Is.True);
    }

    [Test]
    public void Cancel_AnUnknownHandle_ReportsThatNothingWasCancelled()
    {
        _ = _gameLoop.ProcessAsync(_cancellation.Token);

        Assert.Multiple(() =>
        {
            Assert.That(_gameLoop.Cancel(999_999), Is.False);
            Assert.That(_gameLoop.Cancel(GameLoopService.NotScheduled), Is.False);
        });
    }

    [Test]
    public void Cancel_TheSameHandleTwice_ReportsNothingTheSecondTime()
    {
        _ = _gameLoop.ProcessAsync(_cancellation.Token);
        var handle = _gameLoop.Enqueue(new PlayerInputCommand(_session, "later"), 5000);

        Assert.Multiple(() =>
        {
            Assert.That(_gameLoop.Cancel(handle), Is.True);
            Assert.That(_gameLoop.Cancel(handle), Is.False);
        });
    }

    [Test]
    public async Task Cancel_AfterTheCommandAlreadyRan_ReportsNothingCancelled()
    {
        var processing = _gameLoop.ProcessAsync(_cancellation.Token);

        var handle = _gameLoop.Enqueue(new PlayerInputCommand(_session, "done"));
        await ReadFromClientAsync("echo: done\r\n");

        // A handle whose command has run is forgotten, so cancelling it is a no-op rather
        // than an entry that accumulates for the lifetime of the process.
        Assert.That(_gameLoop.Cancel(handle), Is.False);

        await _cancellation.CancelAsync();
        await processing;
    }

    [Test]
    public void Enqueue_WithANegativeDelay_IsRejected()
    {
        Assert.That(
            () => _gameLoop.Enqueue(new PlayerInputCommand(_session, "look"), -1),
            Throws.InstanceOf<ArgumentOutOfRangeException>()
        );
    }

    [Test]
    public void Dispose_OnALoopThatNeverRan_DoesNotThrow()
    {
        var loop = new GameLoopService();

        Assert.That(loop.Dispose, Throws.Nothing);
    }

    [Test]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var loop = new GameLoopService();
        loop.Dispose();

        Assert.That(loop.Dispose, Throws.Nothing);
    }

    /// <summary>Reads exactly as many bytes as <paramref name="expected" /> occupies in UTF-8.</summary>
    private async Task<string> ReadFromClientAsync(string expected)
    {
        var stream = _connection.Client.GetStream();
        var buffer = new byte[512];
        var wanted = Encoding.UTF8.GetByteCount(expected);
        var total = 0;

        while (total < wanted)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), _cancellation.Token);

            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return Encoding.UTF8.GetString(buffer, 0, total);
    }
}
