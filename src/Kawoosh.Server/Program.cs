using ConsoleAppFramework;
using Serilog;

// await ConsoleApp.RunAsync(
//     args,
//     async (CancellationToken cancellationToken = default) =>
//     {
//
//         await Task.Delay(Timeout.Infinite, cancellationToken);
//     }
// );

await ConsoleApp.RunAsync(
    args,
    async (CancellationToken cancellationToken) =>
    {
        Log.Logger = new LoggerConfiguration().WriteTo.Console().MinimumLevel.Debug().CreateLogger();

        Log.Logger.Information("Starting Kawoosh Server...");
    }
);
