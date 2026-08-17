using System.Threading.Channels;
using ConsoleAppFramework;
using Kawoosh.Server.Data.Network;
using Kawoosh.Server.Networking;
using Kawoosh.Server.Provider;
using Kawoosh.Server.Services;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;

await ConsoleApp.RunAsync(
    args,
    async (CancellationToken cancellationToken, int port = TelnetListener.DefaultPort) =>
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

        var serviceProvider = new KawooshServiceProvider();

        var commands = Channel.CreateUnbounded<Command>();

        using var listener = serviceProvider.GetService<TelnetListener>();
        listener.Start(port);

        using var gameLoop = new GameLoopService();
        var router = new SessionInputRouter(gameLoop);

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
