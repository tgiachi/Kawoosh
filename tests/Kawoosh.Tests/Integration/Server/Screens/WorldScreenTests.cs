using System.Text;
using System.Threading.Channels;
using Kawoosh.Server.Data.Network;
using Kawoosh.Server.Data.Screens;
using Kawoosh.Server.Data.World;
using Kawoosh.Server.Networking;
using Kawoosh.Server.Screens;
using Kawoosh.Server.Services;
using Kawoosh.Tests.Support;

namespace Kawoosh.Tests.Integration.Server.Screens;

/// <summary>
/// The game itself. Thin for now — the welcome, and saying lines back — but this is where
/// real commands will be read, so its shape matters more than its current content.
/// </summary>
public class WorldScreenTests
{
    private const int TimeoutMilliseconds = 5000;

    private LoopbackConnection _connection = null!;
    private CancellationTokenSource _cancellation = null!;
    private TempMessageDirectory _directory = null!;
    private TelnetSession _session = null!;
    private Task _sessionTask = null!;
    private RecordingGameLoop _loop = null!;
    private WorldScreen _screen = null!;

    [SetUp]
    public void SetUp()
    {
        _connection = new LoopbackConnection();
        _cancellation = new CancellationTokenSource(TimeoutMilliseconds);
        _directory = new TempMessageDirectory();

        _directory.Write("login.sgm", "login.welcome = Welcome, {playerName}.");

        var messages = new MessageService(new VariableService());
        messages.Load(_directory.RootPath, []);

        _loop = new RecordingGameLoop();
        _screen = new WorldScreen(messages);
        _session = new TelnetSession(_connection.Server, Channel.CreateUnbounded<Command>().Writer);
        _sessionTask = _session.StartAsync(_cancellation.Token);
    }

    [TearDown]
    public void TearDown()
    {
        _cancellation.Cancel();
        _session.Dispose();
        _directory.Dispose();
        _connection.Dispose();
        _cancellation.Dispose();
    }

    [Test]
    public async Task OnEnter_WelcomesThePlayerByName()
    {
        // The welcome belongs to arriving in the world rather than to handing over a
        // password, so it still happens if the world is ever reached another way.
        _session.Character = new Character { Name = "Thorin" };

        _screen.OnEnter(Context());

        var received = await ReadTextAsync("Welcome, Thorin.\r\n");

        Assert.That(received, Is.EqualTo("Welcome, Thorin.\r\n"));
    }

    [Test]
    public async Task OnEnter_WithNoCharacter_StillWelcomes()
    {
        // Reaching the world with no character should not throw. It would mean a bug
        // upstream, but a bug upstream must not also take the session down.
        _screen.OnEnter(Context());

        var received = await ReadTextAsync("Welcome, .\r\n");

        Assert.That(received, Is.EqualTo("Welcome, .\r\n"));
    }

    [Test]
    public async Task OnInput_ALine_IsSaidBackTrimmed()
    {
        _screen.OnInput(Context(), "  look  ");

        var received = await ReadTextAsync("echo: look\r\n");

        Assert.That(received, Is.EqualTo("echo: look\r\n"));
    }

    private ScreenContext Context()
    {
        return new ScreenContext(_loop, _session);
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
