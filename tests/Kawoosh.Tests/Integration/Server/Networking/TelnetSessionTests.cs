using System.Text;
using System.Threading.Channels;
using Kawoosh.Server.Data.Network;
using Kawoosh.Server.Data.World;
using Kawoosh.Server.Networking;
using Kawoosh.Tests.Support;

namespace Kawoosh.Tests.Integration.Server.Networking;

/// <summary>
/// Socket-backed tests for one telnet session, driven over a real loopback connection.
/// </summary>
public class TelnetSessionTests
{
    private const int TimeoutMilliseconds = 5000;
    private const byte Iac = 255;
    private const byte Will = 251;
    private const byte OptionEcho = 1;

    private LoopbackConnection _connection = null!;
    private Channel<Command> _commands = null!;
    private CancellationTokenSource _cancellation = null!;

    [SetUp]
    public void SetUp()
    {
        _connection = new LoopbackConnection();
        _commands = Channel.CreateUnbounded<Command>();
        _cancellation = new CancellationTokenSource(TimeoutMilliseconds);
    }

    [TearDown]
    public void TearDown()
    {
        _cancellation.Dispose();
        _connection.Dispose();
    }

    private TelnetSession CreateSession()
    {
        return new TelnetSession(_connection.Server, _commands.Writer);
    }

    private async Task<Command> ReadCommandAsync()
    {
        return await _commands.Reader.ReadAsync(_cancellation.Token);
    }

    [Test]
    public void Id_OnTwoSessions_IsUnique()
    {
        using var first = CreateSession();
        using var second = new TelnetSession(_connection.Client, _commands.Writer);

        Assert.That(first.Id, Is.Not.EqualTo(second.Id));
    }

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
    public async Task RunAsync_ClientSendsOneLine_PublishesOneCommand()
    {
        using var session = CreateSession();
        var run = session.RunAsync(_cancellation.Token);

        _connection.SendRaw("look\r\n"u8.ToArray());
        var command = await ReadCommandAsync();

        Assert.Multiple(() =>
        {
            Assert.That(command.Text, Is.EqualTo("look"));
            Assert.That(command.Session, Is.SameAs(session));
        });

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

        Assert.Multiple(() =>
        {
            Assert.That(first.Text, Is.EqualTo("north"));
            Assert.That(second.Text, Is.EqualTo("south"));
        });

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
    public void RunAsync_ClientDisconnects_CompletesWithoutThrowing()
    {
        using var session = CreateSession();
        var run = session.RunAsync(_cancellation.Token);

        _connection.Client.Close();

        Assert.That(async () => await run, Throws.Nothing);
    }

    [Test]
    public async Task Send_AfterStart_WritesCrLfTerminatedUtf8ToTheClient()
    {
        using var session = CreateSession();
        var start = session.StartAsync(_cancellation.Token);

        session.Send("Benvenuto à Kawoosh");
        var received = await ReadFromClientAsync();

        Assert.That(received, Is.EqualTo("Benvenuto à Kawoosh\r\n"));

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

    private async Task<string> ReadFromClientAsync(int expectedLength = 0)
    {
        var stream = _connection.Client.GetStream();
        var buffer = new byte[256];
        var total = 0;

        while (total == 0 || (expectedLength > 0 && total < expectedLength))
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
