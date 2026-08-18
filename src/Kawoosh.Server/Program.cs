using System.Threading.Channels;
using ConsoleAppFramework;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using Kawoosh.Server.Data.Network;
using Kawoosh.Server.Interfaces;
using Kawoosh.Server.Provider;

await ConsoleApp.RunAsync(
    args,
    async (CancellationToken cancellationToken, int port = ITelnetListener.DefaultPort) =>
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

        var commands = Channel.CreateUnbounded<Command>();

        // Any of the three ending means the server no longer serves anyone: a half that
        // stopped on its own would otherwise leave the process up and silently dead.
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

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
