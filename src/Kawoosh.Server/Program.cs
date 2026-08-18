using System.Threading.Channels;
using ConsoleAppFramework;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using Kawoosh.Server.Data.Network;
using Kawoosh.Server.Interfaces;
using Kawoosh.Server.Internal;
using Kawoosh.Server.Provider;

await ConsoleApp.RunAsync(
    args,
    async (
        CancellationToken cancellationToken,
        int port = ITelnetListener.DefaultPort,
        string screenPath = "screens",
        string messagePath = "messages"
    ) =>
    {
        Log.Logger = new LoggerConfiguration().WriteTo
                                              .Console(
                                                  theme: AnsiConsoleTheme.Literate,
                                                  applyThemeToRedirectedOutput: true
                                              )
                                              .MinimumLevel
                                              .Debug()
                                              .CreateLogger();
        Log.Information("Starting Kawoosh Server...");

        // The provider owns what it builds: disposing it closes the listener's socket and the
        // game loop's timer, so neither needs a using of its own.
        await using var services = new KawooshServiceProvider();

        var listener = services.GetService<ITelnetListener>();
        var gameLoop = services.GetService<IGameLoopService>();
        var router = services.GetService<ISessionInputRouter>();

        listener.Start(port);

        var variables = services.GetService<IVariableService>();
        var screens = services.GetService<IScreenService>();

        // Returns 0 until there is a session registry to count: a builder that is wrong is
        // still the seam the registry plugs into, where a missing token would be a gap.
        variables.AddVariableBuilder("onlineCount", () => 0);

        // Before the listener accepts anyone: greeting players with a blank screen is worse
        // than not starting at all.
        screens.Load(ContentPath.Resolve(screenPath), ["greeting"]);

        var messages = services.GetService<IMessageService>();

        messages.Load(ContentPath.Resolve(messagePath), ["login.name-prompt"]);

        var commands = Channel.CreateUnbounded<Command>();

        // Any of the three ending means the server no longer serves anyone: a half that
        // stopped on its own would otherwise leave the process up and silently dead.
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // The game layer learns about connections here; the listener stays ignorant of what
        // happens to the sessions it hands over.
        listener.SessionAccepted += router.OnSessionAccepted;

        var accepting = listener.StartAsync(commands.Writer, shutdown.Token);
        var routing = router.PumpAsync(commands.Reader, shutdown.Token);
        var ticking = gameLoop.ProcessAsync(shutdown.Token);

        await Task.WhenAny(accepting, routing, ticking);
        await shutdown.CancelAsync();

        await Task.WhenAll(accepting, routing, ticking);

        Log.Information("Kawoosh Server stopped");
        await Log.CloseAndFlushAsync();
    }
);
