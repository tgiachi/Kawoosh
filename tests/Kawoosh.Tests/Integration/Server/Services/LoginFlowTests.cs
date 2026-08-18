using System.Text;
using System.Threading.Channels;
using Kawoosh.Server.Data.Commands;
using Kawoosh.Server.Data.Network;
using Kawoosh.Server.Networking;
using Kawoosh.Server.Screens;
using Kawoosh.Server.Services;
using Kawoosh.Tests.Support;

namespace Kawoosh.Tests.Integration.Server.Services;

/// <summary>
/// The login conversation as a player experiences it, driven through the game loop rather
/// than through whatever object happens to implement it. This is the net the screen refactor
/// is carried out under: every test here must stay green through it, unedited.
/// </summary>
public class LoginFlowTests
{
    private const int TimeoutMilliseconds = 5000;
    private const string NamePrompt = "Name? ";
    private const string PasswordPrompt = "Password: ";

    // IAC WILL ECHO and IAC WONT ECHO. The server takes over echoing so the password never
    // appears, then hands it back.
    private static readonly byte[] SuppressEcho = [255, 251, 1];
    private static readonly byte[] RestoreEcho = [255, 252, 1];

    private LoopbackConnection _connection = null!;
    private CancellationTokenSource _cancellation = null!;
    private TempScreenDirectory _screenDirectory = null!;
    private TempMessageDirectory _messageDirectory = null!;
    private GameLoopService _loop = null!;
    private TelnetSession _session = null!;
    private Task _sessionTask = null!;
    private Task _loopTask = null!;

    [SetUp]
    public void SetUp()
    {
        _connection = new LoopbackConnection();
        _cancellation = new CancellationTokenSource(TimeoutMilliseconds);

        _screenDirectory = new TempScreenDirectory();
        _screenDirectory.Write("greeting.sgs", "GREETING");

        _messageDirectory = new TempMessageDirectory();
        _messageDirectory.Write(
            "login.sgm",
            $"login.name-prompt = {NamePrompt}",
            "login.name-too-short = Too short.",
            "login.name-too-long = Too long.",
            "login.name-invalid = Letters only.",
            $"login.password-prompt = {PasswordPrompt}",
            "login.welcome = Welcome, {playerName}."
        );

        var screens = new ScreenService(new VariableService());
        screens.Load(_screenDirectory.RootPath, ["greeting"]);

        var messages = new MessageService(new VariableService());
        messages.Load(_messageDirectory.RootPath, []);

        _loop = new GameLoopService(
            screens,
            new ScreenManager(
                [new GreetingScreen(), new NameScreen(messages), new PasswordScreen(messages), new WorldScreen(messages)]
            )
        );
        _session = new TelnetSession(_connection.Server, Channel.CreateUnbounded<Command>().Writer);
        _sessionTask = _session.StartAsync(_cancellation.Token);
        _loopTask = _loop.ProcessAsync(_cancellation.Token);
    }

    [TearDown]
    public void TearDown()
    {
        _cancellation.Cancel();
        _loop.Dispose();
        _session.Dispose();
        _connection.Dispose();
        _screenDirectory.Dispose();
        _messageDirectory.Dispose();
        _cancellation.Dispose();
    }

    [Test]
    public async Task AConnectingSession_SeesTheGreetingThenTheNamePrompt()
    {
        _loop.Enqueue(new SessionConnectedCommand(_session));

        var expected = $"GREETING\r\n{NamePrompt}";
        var received = await ReadTextAsync(expected);

        Assert.That(received, Is.EqualTo(expected));
    }

    [Test]
    public async Task AUsableName_StopsTheClientEchoingBeforeThePasswordPrompt()
    {
        await ArriveAtTheNamePromptAsync();

        _loop.Enqueue(new PlayerInputCommand(_session, "Thorin"));

        var negotiation = await ReadBytesAsync(SuppressEcho.Length);
        var prompt = await ReadTextAsync(PasswordPrompt);

        Assert.Multiple(
            () =>
            {
                // Before the prompt, or the first characters are on screen by the time the
                // client stops echoing.
                Assert.That(negotiation, Is.EqualTo(SuppressEcho));
                Assert.That(prompt, Is.EqualTo(PasswordPrompt));
            }
        );
    }

    [Test]
    public async Task AShortName_IsRejectedAndAskedAgain()
    {
        await ArriveAtTheNamePromptAsync();

        _loop.Enqueue(new PlayerInputCommand(_session, "ab"));

        var expected = $"Too short.\r\n{NamePrompt}";
        var received = await ReadTextAsync(expected);

        Assert.That(received, Is.EqualTo(expected));
    }

    [Test]
    public async Task APassword_GivesEchoingBackAndWelcomesThePlayer()
    {
        await ArriveAtThePasswordPromptAsync();

        _loop.Enqueue(new PlayerInputCommand(_session, "anything"));

        var negotiation = await ReadBytesAsync(RestoreEcho.Length);
        var welcome = await ReadTextAsync("Welcome, Thorin.\r\n");

        Assert.Multiple(
            () =>
            {
                Assert.That(negotiation, Is.EqualTo(RestoreEcho));
                Assert.That(welcome, Is.EqualTo("Welcome, Thorin.\r\n"));
            }
        );
    }

    [Test]
    public async Task ALineOnceInTheWorld_IsEchoedBack()
    {
        await ArriveInTheWorldAsync();

        _loop.Enqueue(new PlayerInputCommand(_session, "look"));

        var received = await ReadTextAsync("echo: look\r\n");

        Assert.That(received, Is.EqualTo("echo: look\r\n"));
    }

    private async Task ArriveAtTheNamePromptAsync()
    {
        _loop.Enqueue(new SessionConnectedCommand(_session));

        await ReadTextAsync($"GREETING\r\n{NamePrompt}");
    }

    private async Task ArriveAtThePasswordPromptAsync()
    {
        await ArriveAtTheNamePromptAsync();

        _loop.Enqueue(new PlayerInputCommand(_session, "Thorin"));

        await ReadBytesAsync(SuppressEcho.Length);
        await ReadTextAsync(PasswordPrompt);
    }

    private async Task ArriveInTheWorldAsync()
    {
        await ArriveAtThePasswordPromptAsync();

        _loop.Enqueue(new PlayerInputCommand(_session, "anything"));

        await ReadBytesAsync(RestoreEcho.Length);
        await ReadTextAsync("Welcome, Thorin.\r\n");
    }

    private async Task<string> ReadTextAsync(string expected)
    {
        var bytes = await ReadBytesAsync(Encoding.UTF8.GetByteCount(expected));

        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// Reads exactly <paramref name="wanted" /> bytes. One socket read can deliver the next
    /// message too, and an assertion about the next N bytes must not see them — so each read
    /// goes straight into <paramref name="wanted" />-bounded span of the result. The OS can
    /// never hand back more than that span can hold, so nothing arrives that has to be
    /// discarded or held over: whatever the next message is stays in the socket's receive
    /// buffer until the next call reads it.
    /// </summary>
    private async Task<byte[]> ReadBytesAsync(int wanted)
    {
        var result = new byte[wanted];
        var total = 0;

        var stream = _connection.Client.GetStream();

        while (total < wanted)
        {
            var read = await stream.ReadAsync(result.AsMemory(total), _cancellation.Token);

            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total == wanted ? result : result[..total];
    }
}
