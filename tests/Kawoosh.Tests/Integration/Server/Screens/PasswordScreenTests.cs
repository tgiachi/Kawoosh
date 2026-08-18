using System.Text;
using System.Threading.Channels;
using Kawoosh.Server.Data.Commands;
using Kawoosh.Server.Data.Network;
using Kawoosh.Server.Data.Screens;
using Kawoosh.Server.Networking;
using Kawoosh.Server.Screens;
using Kawoosh.Server.Services;
using Kawoosh.Tests.Support;

namespace Kawoosh.Tests.Integration.Server.Screens;

/// <summary>
/// Where a password is taken. The interesting behaviour is not the password — any is
/// accepted — but the two telnet negotiations that keep it off the player's screen.
/// </summary>
public class PasswordScreenTests
{
    private const int TimeoutMilliseconds = 5000;
    private const string PasswordPrompt = "Password: ";
    private const int NegotiationBytes = 3;

    private LoopbackConnection _connection = null!;
    private CancellationTokenSource _cancellation = null!;
    private TempMessageDirectory _directory = null!;
    private TelnetSession _session = null!;
    private Task _sessionTask = null!;
    private RecordingGameLoop _loop = null!;
    private PasswordScreen _screen = null!;

    [SetUp]
    public void SetUp()
    {
        _connection = new LoopbackConnection();
        _cancellation = new CancellationTokenSource(TimeoutMilliseconds);
        _directory = new TempMessageDirectory();

        _directory.Write("login.sgm", $"login.password-prompt = {PasswordPrompt}");

        var messages = new MessageService(new VariableService());
        messages.Load(_directory.RootPath, []);

        _loop = new RecordingGameLoop();
        _screen = new PasswordScreen(messages);
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
    public async Task OnEnter_StopsTheClientEchoingBeforeThePrompt()
    {
        _screen.OnEnter(Context());

        var negotiation = await ReadBytesAsync(NegotiationBytes);
        var prompt = await ReadTextAsync(PasswordPrompt);

        Assert.Multiple(
            () =>
            {
                // IAC WILL ECHO, and before the prompt: after it, the first characters of the
                // password are already on screen by the time the client stops.
                Assert.That(negotiation, Is.EqualTo(new byte[] { 255, 251, 1 }));
                Assert.That(prompt, Is.EqualTo(PasswordPrompt));
            }
        );
    }

    [Test]
    public async Task OnExit_GivesEchoingBackToTheClient()
    {
        // OnExit's own contract is that anything acquired in one hook must be safe to release
        // in another even when the first never fired: a timeout, a kick, or a forced switch
        // can all leave this screen without ever routing through OnInput. Suppression must
        // not survive onto whatever the session lands on next.
        _screen.OnExit(Context());

        var negotiation = await ReadBytesAsync(NegotiationBytes);

        // IAC WONT ECHO, same restore OnInput sends.
        Assert.That(negotiation, Is.EqualTo(new byte[] { 255, 252, 1 }));
    }

    [Test]
    public void OnInput_AnyPasswordAtAll_MovesToTheWorld()
    {
        // Accepted unconditionally: there is no character store to check against. The step
        // exists so the rest is built against its shape rather than around its absence.
        _screen.OnInput(Context(), "");

        Assert.That(
            _loop.Commands,
            Is.EqualTo(new[] { new SwitchScreenCommand(_session, WorldScreen.ScreenName) })
        );
    }

    private ScreenContext Context()
    {
        return new ScreenContext(_loop, _session);
    }

    private async Task<string> ReadTextAsync(string expected)
    {
        var bytes = await ReadBytesAsync(Encoding.UTF8.GetByteCount(expected));

        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// Reads exactly <paramref name="wanted" /> bytes. One socket read can deliver the next
    /// message too, and an assertion about the next few bytes must not see them.
    /// </summary>
    private async Task<byte[]> ReadBytesAsync(int wanted)
    {
        var stream = _connection.Client.GetStream();
        var received = new byte[wanted];
        var total = 0;

        while (total < wanted)
        {
            // Bounded by what is left to want, so the OS cannot hand back more than was asked
            // for. An oversized buffer would swallow whatever the server wrote next — the two
            // writes around a password prompt arrive as one read often enough — and the call
            // that wanted those bytes would then block until the test's cancellation fired.
            var read = await stream.ReadAsync(received.AsMemory(total), _cancellation.Token);

            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total == wanted ? received : received[..total];
    }
}
