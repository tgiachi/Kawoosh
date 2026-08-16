using System.Threading.Channels;
using ConsoleAppFramework;
using Kawoosh.Server.Data.Network;
using Kawoosh.Server.Networking;
using Kawoosh.Server.Services;
using Serilog;

await ConsoleApp.RunAsync(
    args,
    async (CancellationToken cancellationToken, int port = TelnetListener.DefaultPort) =>
    {
        Log.Logger = new LoggerConfiguration().WriteTo.Console().MinimumLevel.Debug().CreateLogger();
        Log.Information("Starting Kawoosh Server...");

        var commands = Channel.CreateUnbounded<Command>();

        using var listener = new TelnetListener(port);
        var sink = new CommandLogService();

        // Either half ending means the server no longer serves anyone: an accept loop that
        // stopped on its own would otherwise leave the process up and silently dead.
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var accepting = listener.StartAsync(commands.Writer, shutdown.Token);
        var pumping = sink.PumpAsync(commands.Reader, shutdown.Token);

        await Task.WhenAny(accepting, pumping);
        await shutdown.CancelAsync();

        await Task.WhenAll(accepting, pumping);

        Log.Information("Kawoosh Server stopped");
        await Log.CloseAndFlushAsync();
    }
);
