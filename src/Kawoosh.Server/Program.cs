using Serilog;

await ConsoleAppFramework.ConsoleApp.RunAsync(
    args,
    async () =>
    {
        Log.Logger = new LoggerConfiguration().WriteTo.Console().MinimumLevel.Debug().CreateLogger();
    }
);
