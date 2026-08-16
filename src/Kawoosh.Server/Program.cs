using System.Threading.Channels;
using ConsoleAppFramework;
using Serilog;
using Kawoosh.Server.Data.Network;
using Kawoosh.Server.Networking;
using Kawoosh.Server.Services;

await ConsoleApp.RunAsync(
    args,
    async (CancellationToken cancellationToken, int port = TelnetListener.DefaultPort) =>
    {
        Log.Logger = new LoggerConfiguration().WriteTo.Console().MinimumLevel.Debug().CreateLogger();
        Log.Information("Starting Kawoosh Server...");

        var commands = Channel.CreateUnbounded<Command>();

        using var listener = new TelnetListener(port);
        var sink = new CommandLogService();

        var accepting = listener.StartAsync(commands.Writer, cancellationToken);
        var pumping = sink.PumpAsync(commands.Reader, cancellationToken);

        await Task.WhenAll(accepting, pumping);

        Log.Information("Kawoosh Server stopped");
        await Log.CloseAndFlushAsync();
    }
);
