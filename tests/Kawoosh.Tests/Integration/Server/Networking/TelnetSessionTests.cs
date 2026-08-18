using System.Text;
using System.Threading.Channels;
using Kawoosh.Server.Data.Network;
using Kawoosh.Server.Data.World;
using Kawoosh.Server.Networking;
using Kawoosh.Tests.Support;
using Serilog;

namespace Kawoosh.Tests.Integration.Server.Networking;

/// <summary>
/// Socket-backed tests for one telnet session, driven over a real loopback connection.
/// </summary>
public class TelnetSessionTests
{
    private const int TimeoutMilliseconds = 5000;
    private const int OutboundOverflow = 8;
    private const int OversizedLineFactor = 2;
    private const string DropTemplate = "Session {SessionId} is closing, dropped an outbound message";
    private const byte Iac = 255;
    private const byte Will = 251;
    private const byte OptionEcho = 1;

    private LoopbackConnection _connection = null!;
    private Channel<Command> _commands = null!;
    private CancellationTokenSource _cancellation = null!;
    private CapturingLogSink _logSink = null!;

    [Test]
    public void Character_OnANewSession_IsNull()
    {
        using var session = CreateSession();

        Assert.That(session.Character, Is.Null);
    }

    [Test]
    public void Character_OnceAssigned_IsKept()
    {
        using var session = CreateSession();
        var character = new Character { Name = "Thorin" };

        session.Character = character;

        Assert.That(session.Character, Is.SameAs(character));
    }

    [Test]
    public void Id_OnTwoSessions_IsUnique()
    {
        using var first = CreateSession();
        using var second = new TelnetSession(_connection.Client, _commands.Writer);

        Assert.That(first.Id, Is.Not.EqualTo(second.Id));
    }

    [Test]
    public void RunAsync_ClientDisconnects_CompletesWithoutThrowing()
    {
        using var session = CreateSession();
        var run = session.RunAsync(_cancellation.Token);

        _connection.Client.Close();

        Assert.That(async () => await run, Throws.Nothing);
    }

    [Test]
    public async Task RunAsync_ClientSendsMoreThanMaxLineLengthWithNoNewline_EndsTheSessionWithoutACommand()
    {
        using var session = CreateSession();
        var run = session.RunAsync(_cancellation.Token);

        var flood = new string('A', TelnetSession.MaxLineLength * OversizedLineFactor);
        _connection.SendRaw(Encoding.UTF8.GetBytes(flood));

        await run;

        Assert.Multiple(
            () =>
            {
                // Without the cap the pipe keeps buying segments and only the test timeout
                // ends this, so an uncancelled token is what proves the cap did the work.
                Assert.That(_cancellation.IsCancellationRequested, Is.False);
                Assert.That(_commands.Reader.Count, Is.Zero);
            }
        );
    }

    [Test]
    public async Task RunAsync_ClientSendsOneLine_PublishesOneCommand()
    {
        using var session = CreateSession();
        var run = session.RunAsync(_cancellation.Token);

        _connection.SendRaw("look\r\n"u8.ToArray());
        var command = await ReadCommandAsync();

        Assert.Multiple(
            () =>
            {
                Assert.That(command.Text, Is.EqualTo("look"));
                Assert.That(command.Session, Is.SameAs(session));
            }
        );

        await _cancellation.CancelAsync();
        await run;
    }

    [Test]
    public async Task RunAsync_LineSplitAcrossTwoWrites_PublishesOneCommand()
    {
        using var session = CreateSession();
        var run = session.RunAsync(_cancellation.Token);

        _connection.SendRaw("no"u8.ToArray());
        _connection.SendRaw("rth\n"u8.ToArray());
        var command = await ReadCommandAsync();

        Assert.That(command.Text, Is.EqualTo("north"));

        await _cancellation.CancelAsync();
        await run;
    }

    [Test]
    public async Task RunAsync_LineWithNegotiation_StripsIt()
    {
        using var session = CreateSession();
        var run = session.RunAsync(_cancellation.Token);

        _connection.SendRaw(Iac, Will, OptionEcho, (byte)'h', (byte)'i', (byte)'\r', (byte)'\n');
        var command = await ReadCommandAsync();

        Assert.That(command.Text, Is.EqualTo("hi"));

        await _cancellation.CancelAsync();
        await run;
    }

    [Test]
    public async Task RunAsync_TwoLinesInOneWrite_PublishesTwoCommands()
    {
        using var session = CreateSession();
        var run = session.RunAsync(_cancellation.Token);

        _connection.SendRaw("north\nsouth\n"u8.ToArray());
        var first = await ReadCommandAsync();
        var second = await ReadCommandAsync();

        Assert.Multiple(
            () =>
            {
                Assert.That(first.Text, Is.EqualTo("north"));
                Assert.That(second.Text, Is.EqualTo("south"));
            }
        );

        await _cancellation.CancelAsync();
        await run;
    }

    [Test]
    public async Task Send_AfterStart_WritesCrLfTerminatedUtf8ToTheClient()
    {
        using var session = CreateSession();
        var start = session.StartAsync(_cancellation.Token);

        const string expected = "Welcome to the café\r\n";

        session.Send("Welcome to the café");

        // The accented character is two bytes, so the read has to be told how many to wait
        // for rather than settling for whatever the first ReadAsync happens to hand back.
        var received = await ReadFromClientAsync(Encoding.UTF8.GetByteCount(expected));

        Assert.That(received, Is.EqualTo(expected));

        await _cancellation.CancelAsync();
        await start;
    }

    [Test]
    public async Task Send_AMultiLineMessage_EndsEveryLineWithCarriageReturnLineFeed()
    {
        using var session = CreateSession();
        var start = session.StartAsync(_cancellation.Token);

        // A screen is one Send of many lines. Terminating only the last one would leave a
        // telnet client staircasing the rest.
        session.Send("first\nsecond");
        var received = await ReadFromClientAsync("first\r\nsecond\r\n".Length);

        Assert.That(received, Is.EqualTo("first\r\nsecond\r\n"));

        await _cancellation.CancelAsync();
        await start;
    }

    [Test]
    public async Task Send_AMessageAlreadyUsingCarriageReturns_IsNotDoubled()
    {
        using var session = CreateSession();
        var start = session.StartAsync(_cancellation.Token);

        session.Send("first\r\nsecond");
        var received = await ReadFromClientAsync("first\r\nsecond\r\n".Length);

        Assert.That(received, Is.EqualTo("first\r\nsecond\r\n"));

        await _cancellation.CancelAsync();
        await start;
    }

    [Test]
    public async Task SendPrompt_AMessage_ArrivesWithoutALineTerminator()
    {
        using var session = CreateSession();
        var start = session.StartAsync(_cancellation.Token);

        // The cursor has to sit after the space, on the same line, or the player types their
        // password on the row below the word asking for it.
        session.SendPrompt("Password: ");
        var received = await ReadFromClientAsync("Password: ".Length);

        Assert.That(received, Is.EqualTo("Password: "));

        await _cancellation.CancelAsync();
        await start;
    }

    [Test]
    public async Task SendPrompt_ThenSend_KeepsBothInOrderAndOnlyTerminatesTheSecond()
    {
        using var session = CreateSession();
        var start = session.StartAsync(_cancellation.Token);

        session.SendPrompt("Name: ");
        session.Send("done");
        var received = await ReadFromClientAsync("Name: done\r\n".Length);

        Assert.That(received, Is.EqualTo("Name: done\r\n"));

        await _cancellation.CancelAsync();
        await start;
    }

    [Test]
    public async Task Send_TwoMessages_ArriveInOrder()
    {
        using var session = CreateSession();
        var start = session.StartAsync(_cancellation.Token);

        session.Send("first");
        session.Send("second");
        var received = await ReadFromClientAsync("first\r\nsecond\r\n".Length);

        Assert.That(received, Is.EqualTo("first\r\nsecond\r\n"));

        await _cancellation.CancelAsync();
        await start;
    }

    [Test]
    public void Send_WhenTheOutboundQueueIsFull_DropsTheNewMessageInsteadOfFailingTheWrite()
    {
        using var logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(_logSink).CreateLogger();
        var previous = Log.Logger;
        Log.Logger = logger;

        try
        {
            // The session binds its logger at construction, so the sink has to be live first.
            var session = CreateSession();

            for (var i = 0; i < TelnetSession.OutboundCapacity + OutboundOverflow; i++)
            {
                session.Send($"message {i}");
            }

            var whileFull = _logSink.Count(DropTemplate);

            // Disposing completes the channel, the one case where TryWrite really fails.
            session.Dispose();
            session.Send("after the session closed");

            Assert.Multiple(
                () =>
                {
                    // DropWrite discards the incoming message and still reports success.
                    // Under FullMode.Wait every overflow write would report a failure here.
                    Assert.That(whileFull, Is.Zero);
                    Assert.That(_logSink.Count(DropTemplate), Is.EqualTo(1));
                }
            );
        }
        finally
        {
            Log.Logger = previous;
        }
    }

    [SetUp]
    public void SetUp()
    {
        _logSink = new();
        _connection = new();
        _commands = Channel.CreateUnbounded<Command>();
        _cancellation = new(TimeoutMilliseconds);
    }

    [Test]
    public async Task StartAsync_Cancelled_CompletesBothLoops()
    {
        using var session = CreateSession();
        var start = session.StartAsync(_cancellation.Token);

        await _cancellation.CancelAsync();

        Assert.That(async () => await start, Throws.Nothing);
    }

    [Test]
    public async Task StartAsync_ClientSendsLine_StillPublishesCommands()
    {
        using var session = CreateSession();
        var start = session.StartAsync(_cancellation.Token);

        _connection.SendRaw("look\r\n"u8.ToArray());
        var command = await ReadCommandAsync();

        Assert.That(command.Text, Is.EqualTo("look"));

        await _cancellation.CancelAsync();
        await start;
    }

    [Test]
    public async Task StartAsync_SendRacesClientDisconnectTeardown_CompletesWithoutThrowing()
    {
        using var session = CreateSession();
        var start = session.StartAsync(_cancellation.Token);

        _connection.Client.Close();

        for (var i = 0; i < 200; i++)
        {
            session.Send($"message {i}");
        }

        await _cancellation.CancelAsync();

        Assert.That(async () => await start, Throws.Nothing);
    }

    [TearDown]
    public void TearDown()
    {
        _cancellation.Dispose();
        _connection.Dispose();
    }

    private TelnetSession CreateSession()
        => new(_connection.Server, _commands.Writer);

    private async Task<Command> ReadCommandAsync()
        => await _commands.Reader.ReadAsync(_cancellation.Token);

    [Test]
    public async Task SendRaw_ABytePastAscii_ReachesTheClientUntouched()
    {
        using var session = CreateSession();
        var start = session.StartAsync(_cancellation.Token);

        // 255 is IAC. Put through UTF-8 encoding it would arrive as two bytes and mean
        // nothing to the client.
        session.SendRaw([255, 251, 1]);
        var received = await ReadBytesFromClientAsync(3);

        Assert.That(received, Is.EqualTo(new byte[] { 255, 251, 1 }));

        await _cancellation.CancelAsync();
        await start;
    }

    [Test]
    public async Task HideInput_TellsTheClientTheServerWillEcho()
    {
        using var session = CreateSession();
        var start = session.StartAsync(_cancellation.Token);

        // IAC WILL ECHO. The client answers by stopping its own echo, and since the server
        // then sends nothing back, what the player types is invisible.
        session.HideInput();
        var received = await ReadBytesFromClientAsync(3);

        Assert.That(received, Is.EqualTo(new byte[] { 255, 251, 1 }));

        await _cancellation.CancelAsync();
        await start;
    }

    [Test]
    public async Task ShowInput_HandsEchoingBackToTheClient()
    {
        using var session = CreateSession();
        var start = session.StartAsync(_cancellation.Token);

        session.ShowInput();
        var received = await ReadBytesFromClientAsync(3);

        Assert.That(received, Is.EqualTo(new byte[] { 255, 252, 1 }));

        await _cancellation.CancelAsync();
        await start;
    }

    [Test]
    public async Task SendRaw_ThenSend_KeepsTheOrder()
    {
        using var session = CreateSession();
        var start = session.StartAsync(_cancellation.Token);

        session.SendRaw([255, 251, 1]);
        session.Send("after");
        var received = await ReadBytesFromClientAsync(3 + "after\r\n".Length);

        Assert.That(
            received,
            Is.EqualTo(new byte[] { 255, 251, 1, (byte)'a', (byte)'f', (byte)'t', (byte)'e', (byte)'r', 13, 10 })
        );

        await _cancellation.CancelAsync();
        await start;
    }

    private async Task<byte[]> ReadBytesFromClientAsync(int wanted)
    {
        var stream = _connection.Client.GetStream();
        var buffer = new byte[512];
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

        // Exactly what was asked for: one socket read can deliver more than that, and the
        // assertion is about the next N bytes, not about everything that happened to arrive.
        return buffer[..Math.Min(total, wanted)];
    }

    private async Task<string> ReadFromClientAsync(int expectedLength = 0)
    {
        var stream = _connection.Client.GetStream();
        var buffer = new byte[256];
        var total = 0;

        while (total == 0 || expectedLength > 0 && total < expectedLength)
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
