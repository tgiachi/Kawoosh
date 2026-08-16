using System.Net.Sockets;
using System.Threading.Channels;
using Kawoosh.Server.Data.Network;
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

    [SetUp]
    public void SetUp()
    {
        _commands = Channel.CreateUnbounded<Command>();
        _cancellation = new CancellationTokenSource(TimeoutMilliseconds);
    }

    [TearDown]
    public void TearDown()
    {
        _cancellation.Dispose();
    }

    private async Task<TcpClient> ConnectAsync(TelnetListener listener)
    {
        var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", listener.Port, _cancellation.Token);

        return client;
    }

    private static async Task SendLineAsync(TcpClient client, string text)
    {
        var payload = System.Text.Encoding.UTF8.GetBytes(text + "\r\n");

        await client.GetStream().WriteAsync(payload);
        await client.GetStream().FlushAsync();
    }

    [Test]
    public void DefaultPort_IsFourThousand()
    {
        Assert.That(TelnetListener.DefaultPort, Is.EqualTo(4000));
    }

    [Test]
    public async Task StartAsync_ClientConnectsAndSendsALine_PublishesTheCommand()
    {
        using var listener = new TelnetListener(EphemeralPort);
        var accept = listener.StartAsync(_commands.Writer, _cancellation.Token);

        using var client = await ConnectAsync(listener);
        await SendLineAsync(client, "look");

        var command = await _commands.Reader.ReadAsync(_cancellation.Token);

        Assert.That(command.Text, Is.EqualTo("look"));

        await _cancellation.CancelAsync();
        await accept;
    }

    [Test]
    public async Task StartAsync_TwoClients_BothAreServed()
    {
        using var listener = new TelnetListener(EphemeralPort);
        var accept = listener.StartAsync(_commands.Writer, _cancellation.Token);

        using var first = await ConnectAsync(listener);
        using var second = await ConnectAsync(listener);

        await SendLineAsync(first, "north");
        await SendLineAsync(second, "south");

        var one = await _commands.Reader.ReadAsync(_cancellation.Token);
        var two = await _commands.Reader.ReadAsync(_cancellation.Token);

        Assert.Multiple(() =>
        {
            Assert.That(new[] { one.Text, two.Text }, Is.EquivalentTo(new[] { "north", "south" }));
            Assert.That(one.Session.Id, Is.Not.EqualTo(two.Session.Id));
        });

        await _cancellation.CancelAsync();
        await accept;
    }

    [Test]
    public async Task StartAsync_OneClientKeepsTalking_DoesNotBlockTheAcceptLoop()
    {
        using var listener = new TelnetListener(EphemeralPort);
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
    public async Task StartAsync_Cancelled_StopsWithoutThrowing()
    {
        using var listener = new TelnetListener(EphemeralPort);
        var accept = listener.StartAsync(_commands.Writer, _cancellation.Token);

        using var client = await ConnectAsync(listener);
        await _cancellation.CancelAsync();

        Assert.That(async () => await accept, Throws.Nothing);
    }

    [Test]
    public void Port_WhenZeroWasRequested_IsTheEphemeralPortBoundByTheConstructor()
    {
        using var listener = new TelnetListener(EphemeralPort);

        Assert.That(listener.Port, Is.GreaterThan(0));
    }
}
