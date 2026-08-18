using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using Kawoosh.Server.Data.Network;
using Kawoosh.Server.Interfaces;
using Kawoosh.Server.Networking;
using Kawoosh.Server.Provider;
using Kawoosh.SGW.Interfaces;
using Kawoosh.Tests.Support;

namespace Kawoosh.Tests.Integration.Server.Provider;

/// <summary>
/// Tests for the service graph itself. Lifetimes here are not a style choice: a second
/// listener binds a second socket and a second game loop is a second world.
/// </summary>
public class KawooshServiceProviderTests
{
    private const int EphemeralPort = 0;
    private const int TimeoutMilliseconds = 5000;

    [Test]
    public void GetService_TheListener_IsTheSameInstanceEveryTime()
    {
        using var services = new KawooshServiceProvider();

        var first = services.GetService<ITelnetListener>();
        var second = services.GetService<ITelnetListener>();

        Assert.That(first, Is.SameAs(second));
    }

    [Test]
    public void GetService_TheGameLoop_IsTheSameInstanceEveryTime()
    {
        using var services = new KawooshServiceProvider();

        var first = services.GetService<IGameLoopService>();
        var second = services.GetService<IGameLoopService>();

        Assert.That(first, Is.SameAs(second));
    }

    [Test]
    public void GetService_TheVariableStore_IsTheSameInstanceEveryTime()
    {
        using var services = new KawooshServiceProvider();
        services.GetService<IVariableService>().AddVariable("serverName", "Kawoosh");

        Assert.That(services.GetService<IVariableService>().TranslateText("{serverName}"), Is.EqualTo("Kawoosh"));
    }

    [Test]
    public void GetService_TheScreenStore_IsTheSameInstanceEveryTime()
    {
        using var services = new KawooshServiceProvider();

        var first = services.GetService<IScreenService>();
        var second = services.GetService<IScreenService>();

        // A second store would load the directory twice and serve two versions of the truth.
        Assert.That(first, Is.SameAs(second));
    }

    [Test]
    public void GetService_TheMessageStore_IsTheSameInstanceEveryTime()
    {
        using var services = new KawooshServiceProvider();

        var first = services.GetService<IMessageService>();
        var second = services.GetService<IMessageService>();

        // A second store would load the directory twice and serve two versions of the truth.
        Assert.That(first, Is.SameAs(second));
    }

    [Test]
    public void GetService_TheWorldFileParser_IsANewInstanceEachTime()
    {
        using var services = new KawooshServiceProvider();

        var first = services.GetService<ISGWFileParser>();
        var second = services.GetService<ISGWFileParser>();

        // Stateless, so sharing it buys nothing and keeping it around costs a root.
        Assert.That(first, Is.Not.SameAs(second));
    }

    [Test]
    public async Task GetService_TheRouter_FeedsTheSameGameLoopEveryoneElseResolves()
    {
        using var services = new KawooshServiceProvider();
        using var connection = new LoopbackConnection();
        using var cancellation = new CancellationTokenSource(TimeoutMilliseconds);

        var gameLoop = services.GetService<IGameLoopService>();
        var router = services.GetService<ISessionInputRouter>();

        var commands = Channel.CreateUnbounded<Command>();
        using var session = new TelnetSession(connection.Server, commands.Writer);

        var running = session.StartAsync(cancellation.Token);
        var routing = router.PumpAsync(commands.Reader, cancellation.Token);
        var ticking = gameLoop.ProcessAsync(cancellation.Token);

        // The line only comes back if the router put it on the very loop we resolved.
        connection.SendRaw("look\r\n"u8.ToArray());
        var echoed = await ReadEchoAsync(connection, cancellation.Token);

        Assert.That(echoed, Is.EqualTo("echo: look\r\n"));

        await cancellation.CancelAsync();
        await Task.WhenAll(running, routing, ticking);
    }

    [Test]
    public void Dispose_TheProvider_ClosesTheListenerSocketItBuilt()
    {
        int port;

        using (var services = new KawooshServiceProvider())
        {
            var listener = services.GetService<ITelnetListener>();
            listener.Start(EphemeralPort);
            port = listener.Port;

            using var reachable = new TcpClient();
            reachable.Connect("127.0.0.1", port);
        }

        // Program.cs relies on this: it never disposes the listener itself.
        using var afterDispose = new TcpClient();

        Assert.That(() => afterDispose.Connect("127.0.0.1", port), Throws.InstanceOf<SocketException>());
    }

    private static async Task<string> ReadEchoAsync(LoopbackConnection connection, CancellationToken cancellationToken)
    {
        const string expected = "echo: look\r\n";
        var stream = connection.Client.GetStream();
        var buffer = new byte[128];
        var total = 0;

        while (total < expected.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken);

            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return Encoding.UTF8.GetString(buffer, 0, total);
    }
}
