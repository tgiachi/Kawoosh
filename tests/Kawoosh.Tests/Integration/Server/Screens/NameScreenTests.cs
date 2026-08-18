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
/// Where a player says who they are. The validation moved here verbatim from the flow it
/// replaces, so these tests are the same rules under a new owner.
/// </summary>
public class NameScreenTests
{
    private const int TimeoutMilliseconds = 5000;
    private const string NamePrompt = "Name? ";

    private LoopbackConnection _connection = null!;
    private CancellationTokenSource _cancellation = null!;
    private TempMessageDirectory _directory = null!;
    private TelnetSession _session = null!;
    private Task _sessionTask = null!;
    private RecordingGameLoop _loop = null!;
    private NameScreen _screen = null!;

    [SetUp]
    public void SetUp()
    {
        _connection = new LoopbackConnection();
        _cancellation = new CancellationTokenSource(TimeoutMilliseconds);
        _directory = new TempMessageDirectory();

        _directory.Write(
            "login.sgm",
            $"login.name-prompt = {NamePrompt}",
            "login.name-too-short = Too short.",
            "login.name-too-long = Too long.",
            "login.name-invalid = Letters only."
        );

        var messages = new MessageService(new VariableService());
        messages.Load(_directory.RootPath, []);

        _loop = new RecordingGameLoop();
        _screen = new NameScreen(messages);
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
    public async Task OnEnter_AsksForANameAndLeavesTheCursorOnTheSameLine()
    {
        _screen.OnEnter(Context());

        var received = await ReadTextAsync(NamePrompt);

        // No trailing CRLF: the player types right after the space.
        Assert.That(received, Is.EqualTo(NamePrompt));
    }

    [Test]
    public void OnInput_AUsableName_RemembersItAndMovesToThePassword()
    {
        _screen.OnInput(Context(), "Thorin");

        Assert.Multiple(
            () =>
            {
                Assert.That(_session.Character?.Name, Is.EqualTo("Thorin"));
                Assert.That(
                    _loop.Commands,
                    Is.EqualTo(new[] { new SwitchScreenCommand(_session, PasswordScreen.ScreenName) })
                );
            }
        );
    }

    [Test]
    public void OnInput_ANameWithSurroundingSpaces_IsTrimmed()
    {
        _screen.OnInput(Context(), "  Thorin  ");

        Assert.That(_session.Character?.Name, Is.EqualTo("Thorin"));
    }

    [Test]
    public async Task OnInput_ANameThatIsTooShort_SaysSoAndAsksAgain()
    {
        _screen.OnInput(Context(), "ab");

        var expected = $"Too short.\r\n{NamePrompt}";
        var received = await ReadTextAsync(expected);

        Assert.Multiple(
            () =>
            {
                Assert.That(received, Is.EqualTo(expected));
                Assert.That(_loop.Commands, Is.Empty);
            }
        );
    }

    [Test]
    public async Task OnInput_ANameThatIsTooLong_SaysSoAndAsksAgain()
    {
        _screen.OnInput(Context(), "Thorinsonofthrainsonofthror");

        var expected = $"Too long.\r\n{NamePrompt}";
        var received = await ReadTextAsync(expected);

        Assert.That(received, Is.EqualTo(expected));
    }

    [Test]
    public async Task OnInput_ANameThatIsNotAllLetters_SaysSoAndAsksAgain()
    {
        _screen.OnInput(Context(), "Th0rin");

        var expected = $"Letters only.\r\n{NamePrompt}";
        var received = await ReadTextAsync(expected);

        Assert.That(received, Is.EqualTo(expected));
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
