using System.Threading.Channels;
using Kawoosh.Server.Data.Commands;
using Kawoosh.Server.Data.Network;
using Kawoosh.Server.Data.Screens;
using Kawoosh.Server.Networking;
using Kawoosh.Server.Screens;
using Kawoosh.Tests.Support;

namespace Kawoosh.Tests.Integration.Server.Screens;

/// <summary>
/// The banner screen. Its art is played by the loop before OnEnter runs, so all it has to do
/// is move on.
/// </summary>
public class GreetingScreenTests
{
    private const int TimeoutMilliseconds = 5000;

    private LoopbackConnection _connection = null!;
    private CancellationTokenSource _cancellation = null!;
    private TelnetSession _session = null!;
    private Task _sessionTask = null!;
    private RecordingGameLoop _loop = null!;

    [SetUp]
    public void SetUp()
    {
        _connection = new LoopbackConnection();
        _cancellation = new CancellationTokenSource(TimeoutMilliseconds);
        _loop = new RecordingGameLoop();
        _session = new TelnetSession(_connection.Server, Channel.CreateUnbounded<Command>().Writer);
        _sessionTask = _session.StartAsync(_cancellation.Token);
    }

    [TearDown]
    public void TearDown()
    {
        _cancellation.Cancel();
        _session.Dispose();
        _connection.Dispose();
        _cancellation.Dispose();
    }

    [Test]
    public void OnEnter_MovesStraightToTheNameScreen()
    {
        // OnEnter runs once the art has finished, so moving on here does not cut it short.
        var screen = new GreetingScreen();

        screen.OnEnter(new ScreenContext(_loop, _session));

        Assert.That(
            _loop.Commands,
            Is.EqualTo(new[] { new SwitchScreenCommand(_session, NameScreen.ScreenName) })
        );
    }

    [Test]
    public void Name_IsTheScreenItsArtIsNamedAfter()
    {
        Assert.That(new GreetingScreen().Name, Is.EqualTo("greeting"));
    }
}
