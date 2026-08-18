using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using Kawoosh.Server.Data.Network;
using Kawoosh.Server.Interfaces;
using Kawoosh.Server.Networking;

namespace Kawoosh.Tests.Integration.Server.Networking;

/// <summary>
/// Socket-backed tests for the accept loop, bound to an ephemeral port.
/// </summary>
public class TelnetListenerTests
{
    private const int EphemeralPort = 0;
    private const int TimeoutMilliseconds = 5000;

    private Channel<Command> _commands = null!;
    private CancellationTokenSource _cancellation = null!;

    [Test]
    public void DefaultPort_IsFourThousand()
        => Assert.That(ITelnetListener.DefaultPort, Is.EqualTo(4000));

    [Test]
    public void Port_WhenZeroWasRequested_IsTheEphemeralPortBoundByStart()
    {
        using var listener = CreateListener();

        Assert.That(listener.Port, Is.GreaterThan(0));
    }

    [Test]
    public void Port_BeforeStart_IsZero()
    {
        using var listener = new TelnetListener();

        Assert.That(listener.Port, Is.Zero);
    }

    [Test]
    public void Dispose_OnAListenerThatWasNeverStarted_DoesNotThrow()
    {
        var listener = new TelnetListener();

        Assert.That(listener.Dispose, Throws.Nothing);
    }

    [Test]
    public void StartAsync_BeforeStart_ThrowsAnExplicitError()
    {
        using var listener = new TelnetListener();

        Assert.That(
            async () => await listener.StartAsync(_commands.Writer, _cancellation.Token),
            Throws.InstanceOf<InvalidOperationException>()
                  .With.Message.Contains("Start")
        );
    }

    [Test]
    public void Start_CalledTwice_ThrowsInsteadOfLeakingTheFirstSocket()
    {
        using var listener = CreateListener();

        Assert.That(() => listener.Start(EphemeralPort), Throws.InstanceOf<InvalidOperationException>());
    }

    [SetUp]
    public void SetUp()
    {
        _commands = Channel.CreateUnbounded<Command>();
        _cancellation = new(TimeoutMilliseconds);
    }

    [Test]
    public async Task StartAsync_Cancelled_StopsWithoutThrowing()
    {
        using var listener = CreateListener();
        var accept = listener.StartAsync(_commands.Writer, _cancellation.Token);

        using var client = await ConnectAsync(listener);
        await _cancellation.CancelAsync();

        Assert.That(async () => await accept, Throws.Nothing);
    }

    [Test]
    public async Task StartAsync_ClientConnectsAndSendsALine_PublishesTheCommand()
    {
        using var listener = CreateListener();
        var accept = listener.StartAsync(_commands.Writer, _cancellation.Token);

        using var client = await ConnectAsync(listener);
        await SendLineAsync(client, "look");

        var command = await _commands.Reader.ReadAsync(_cancellation.Token);

        Assert.That(command.Text, Is.EqualTo("look"));

        await _cancellation.CancelAsync();
        await accept;
    }

    [Test]
    public async Task StartAsync_OneClientKeepsTalking_DoesNotBlockTheAcceptLoop()
    {
        using var listener = CreateListener();
        var accept = listener.StartAsync(_commands.Writer, _cancellation.Token);

        using var talkative = await ConnectAsync(listener);
        await SendLineAsync(talkative, "first");
        await _commands.Reader.ReadAsync(_cancellation.Token);

        using var latecomer = await ConnectAsync(listener);
        await SendLineAsync(latecomer, "second");

        var command = await _commands.Reader.ReadAsync(_cancellation.Token);

        Assert.That(command.Text, Is.EqualTo("second"));

        await _cancellation.CancelAsync();
        await accept;
    }

    [Test]
    public async Task StartAsync_TwoClients_BothAreServed()
    {
        using var listener = CreateListener();
        var accept = listener.StartAsync(_commands.Writer, _cancellation.Token);

        using var first = await ConnectAsync(listener);
        using var second = await ConnectAsync(listener);

        await SendLineAsync(first, "north");
        await SendLineAsync(second, "south");

        var one = await _commands.Reader.ReadAsync(_cancellation.Token);
        var two = await _commands.Reader.ReadAsync(_cancellation.Token);

        Assert.Multiple(
            () =>
            {
                Assert.That(new[] { one.Text, two.Text }, Is.EquivalentTo(new[] { "north", "south" }));
                Assert.That(one.Session.Id, Is.Not.EqualTo(two.Session.Id));
            }
        );

        await _cancellation.CancelAsync();
        await accept;
    }

    [Test]
    public async Task StartAsync_AClientConnects_AnnouncesTheSessionBeforeItCanSendAnything()
    {
        using var listener = CreateListener();
        var announced = new TaskCompletionSource<TelnetSession>();
        listener.SessionAccepted += session => announced.TrySetResult(session);

        var accept = listener.StartAsync(_commands.Writer, _cancellation.Token);

        using var client = await ConnectAsync(listener);
        var session = await announced.Task.WaitAsync(_cancellation.Token);

        Assert.That(session.Id, Is.Not.EqualTo(Guid.Empty));

        await _cancellation.CancelAsync();
        await accept;
    }

    [Test]
    public async Task StartAsync_ASubscriberThatThrows_DoesNotKillTheAcceptLoop()
    {
        using var listener = CreateListener();
        listener.SessionAccepted += _ => throw new InvalidOperationException("no");

        var accept = listener.StartAsync(_commands.Writer, _cancellation.Token);

        using var client = await ConnectAsync(listener);
        await SendLineAsync(client, "look");

        // One bad subscriber must not stop everyone else from connecting.
        var command = await _commands.Reader.ReadAsync(_cancellation.Token);

        Assert.That(command.Text, Is.EqualTo("look"));

        await _cancellation.CancelAsync();
        await accept;
    }

    [TearDown]
    public void TearDown()
        => _cancellation.Dispose();

    /// <summary>
    /// Builds a listener bound to an ephemeral port. The constructor no longer binds, so
    /// <see cref="TelnetListener.Start" /> has to run before <see cref="TelnetListener.Port" />
    /// is meaningful.
    /// </summary>
    private static TelnetListener CreateListener()
    {
        var listener = new TelnetListener();
        listener.Start(EphemeralPort);

        return listener;
    }

    private async Task<TcpClient> ConnectAsync(TelnetListener listener)
    {
        var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", listener.Port, _cancellation.Token);

        return client;
    }

    private static async Task SendLineAsync(TcpClient client, string text)
    {
        var payload = Encoding.UTF8.GetBytes(text + "\r\n");

        await client.GetStream().WriteAsync(payload);
        await client.GetStream().FlushAsync();
    }
}
