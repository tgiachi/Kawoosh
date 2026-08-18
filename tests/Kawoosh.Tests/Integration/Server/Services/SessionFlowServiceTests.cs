using System.Text;
using System.Threading.Channels;
using Kawoosh.Server.Data.Network;
using Kawoosh.Server.Networking;
using Kawoosh.Server.Services;
using Kawoosh.Server.Types;
using Kawoosh.Tests.Support;

namespace Kawoosh.Tests.Integration.Server.Services;

/// <summary>
/// Tests for the login conversation. Socket-backed because what the flow does is send text to
/// a session, and a real session is cheaper to reason about than a fake.
/// </summary>
public class SessionFlowServiceTests
{
    private const int TimeoutMilliseconds = 5000;
    private const string NamePrompt = "Name? ";
    private const string PasswordPrompt = "Password: ";

    private LoopbackConnection _connection = null!;
    private CancellationTokenSource _cancellation = null!;
    private TempMessageDirectory _directory = null!;
    private TelnetSession _session = null!;
    private Task _sessionTask = null!;
    private SessionFlowService _flow = null!;

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
            "login.name-invalid = Letters only.",
            $"login.password-prompt = {PasswordPrompt}",
            "login.welcome = Welcome, {playerName}."
        );

        var messages = new MessageService(new VariableService());
        messages.Load(_directory.RootPath, []);

        _flow = new SessionFlowService(messages);
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
    public async Task Begin_ASession_AsksForANameAndLeavesTheCursorOnTheSameLine()
    {
        _flow.Begin(_session);

        var received = await ReadAsync(NamePrompt);

        Assert.Multiple(() =>
        {
            // No trailing CRLF: the player types right after the space.
            Assert.That(received, Is.EqualTo(NamePrompt));
            Assert.That(_session.State, Is.EqualTo(SessionState.AwaitingName));
        });
    }

    [Test]
    public async Task Handle_AUsableName_RemembersItAndAsksForAPassword()
    {
        _flow.Handle(_session, "Thorin");

        var received = await ReadAsync(PasswordPrompt);

        Assert.Multiple(() =>
        {
            Assert.That(received, Is.EqualTo(PasswordPrompt));
            Assert.That(_session.Character!.Name, Is.EqualTo("Thorin"));
            Assert.That(_session.State, Is.EqualTo(SessionState.AwaitingPassword));
        });
    }

    [Test]
    public async Task Handle_ANameWithSurroundingSpaces_IsTrimmed()
    {
        _flow.Handle(_session, "   Thorin   ");

        await ReadAsync(PasswordPrompt);

        Assert.That(_session.Character!.Name, Is.EqualTo("Thorin"));
    }

    [Test]
    public async Task Handle_ANameThatIsTooShort_SaysSoAndAsksAgain()
    {
        _flow.Handle(_session, "ab");

        var received = await ReadAsync($"Too short.\r\n{NamePrompt}");

        Assert.Multiple(() =>
        {
            Assert.That(received, Is.EqualTo($"Too short.\r\n{NamePrompt}"));
            Assert.That(_session.State, Is.EqualTo(SessionState.AwaitingName));
            Assert.That(_session.Character, Is.Null);
        });
    }

    [Test]
    public async Task Handle_ANameThatIsTooLong_SaysSoAndAsksAgain()
    {
        _flow.Handle(_session, new string('a', 17));

        var received = await ReadAsync($"Too long.\r\n{NamePrompt}");

        Assert.Multiple(() =>
        {
            Assert.That(received, Is.EqualTo($"Too long.\r\n{NamePrompt}"));
            Assert.That(_session.State, Is.EqualTo(SessionState.AwaitingName));
        });
    }

    [Test]
    public async Task Handle_ANameThatIsNotAllLetters_SaysSoAndAsksAgain()
    {
        _flow.Handle(_session, "Thor1n");

        var received = await ReadAsync($"Letters only.\r\n{NamePrompt}");

        Assert.Multiple(() =>
        {
            Assert.That(received, Is.EqualTo($"Letters only.\r\n{NamePrompt}"));
            Assert.That(_session.Character, Is.Null);
        });
    }

    [Test]
    public async Task Handle_AnyPassword_LetsThePlayerIn()
    {
        _flow.Handle(_session, "Thorin");
        await ReadAsync(PasswordPrompt);

        // Any password is accepted: there is no store to check one against yet.
        _flow.Handle(_session, "anything at all");
        var received = await ReadAsync("Welcome, Thorin.\r\n");

        Assert.Multiple(() =>
        {
            Assert.That(received, Is.EqualTo("Welcome, Thorin.\r\n"));
            Assert.That(_session.State, Is.EqualTo(SessionState.Playing));
        });
    }

    [Test]
    public async Task Handle_AnEmptyPassword_IsAlsoAccepted()
    {
        _flow.Handle(_session, "Thorin");
        await ReadAsync(PasswordPrompt);

        _flow.Handle(_session, "");
        await ReadAsync("Welcome, Thorin.\r\n");

        Assert.That(_session.State, Is.EqualTo(SessionState.Playing));
    }

    [Test]
    public async Task Handle_ALineWhilePlaying_IsEchoed()
    {
        _session.State = SessionState.Playing;

        _flow.Handle(_session, "look");
        var received = await ReadAsync("echo: look\r\n");

        Assert.That(received, Is.EqualTo("echo: look\r\n"));
    }

    [Test]
    public async Task Handle_ANameAtThePasswordPrompt_IsNotTakenAsANewName()
    {
        _flow.Handle(_session, "Thorin");
        await ReadAsync(PasswordPrompt);

        // "Bilbo" here is a password, not a second name: the state decides what a line means.
        _flow.Handle(_session, "Bilbo");
        await ReadAsync("Welcome, Thorin.\r\n");

        Assert.That(_session.Character!.Name, Is.EqualTo("Thorin"));
    }

    private async Task<string> ReadAsync(string expected)
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
