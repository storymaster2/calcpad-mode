using Calcpad.Server;
using Calcpad.Server.Services;
using System.Runtime.InteropServices;

// Auto-flush stdout so the parent process (VS Code extension) sees logs in real time
// when stdio is piped (non-TTY), instead of waiting for buffer flush on exit.
Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });

// Set up global exception handling
AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
{
    FileLogger.LogCrash((Exception)e.ExceptionObject, "AppDomain.UnhandledException");
};

TaskScheduler.UnobservedTaskException += (sender, e) =>
{
    FileLogger.LogCrash(e.Exception, "TaskScheduler.UnobservedTaskException");
    e.SetObserved();
};

try
{
    FileLogger.LogInfo("Starting Calcpad Server");

    // Set default environment variables if not set
    Environment.SetEnvironmentVariable("CALCPAD_PORT", Environment.GetEnvironmentVariable("CALCPAD_PORT") ?? "9420");
    Environment.SetEnvironmentVariable("CALCPAD_HOST", Environment.GetEnvironmentVariable("CALCPAD_HOST") ?? "0.0.0.0");

    // Create and configure web application using shared service
    var (app, serverUrl) = CalcpadApiService.CreateConfiguredApp(args);

    FileLogger.LogInfo("Starting console application", serverUrl);
    Console.WriteLine($"Calcpad Server starting at {serverUrl}");
    Console.WriteLine("Press Ctrl+C to stop the server.");
    Console.WriteLine($"API Documentation: {serverUrl}/swagger");
    Console.WriteLine($"Sample Client: Open sample-client.html in a browser");

    var cts = new CancellationTokenSource();

    // Handle SIGINT (Ctrl+C) and SIGTERM (graceful kill from parent) on all platforms.
    // Replaces Console.CancelKeyPress, which only covers SIGINT.
    using var sigIntReg = PosixSignalRegistration.Create(PosixSignal.SIGINT, ctx =>
    {
        FileLogger.LogInfo("Received SIGINT, shutting down");
        ctx.Cancel = true;
        cts.Cancel();
    });
    using var sigTermReg = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx =>
    {
        FileLogger.LogInfo("Received SIGTERM, shutting down");
        ctx.Cancel = true;
        cts.Cancel();
    });

    // Log ASP.NET Core lifetime transitions so graceful-shutdown progress is visible.
    var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
    lifetime.ApplicationStopping.Register(() => FileLogger.LogInfo("ApplicationStopping"));
    lifetime.ApplicationStopped.Register(() => FileLogger.LogInfo("ApplicationStopped"));

    // Server is designed to be shared across multiple VS Code instances, so it
    // intentionally outlives the spawning process. It only exits on explicit
    // SIGINT/SIGTERM (via the `calcpad.stopServer` command) or OS shutdown.

    var runTask = Task.Run(async () =>
    {
        try
        {
            await app.RunAsync();
        }
        catch (Exception ex)
        {
            FileLogger.LogCrash(ex, "Web application");
        }
    });

    try
    {
        await Task.Delay(-1, cts.Token);
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("Shutting down...");
        await app.StopAsync();
    }

    FileLogger.LogInfo("Application shutdown complete");
}
catch (Exception ex)
{
    FileLogger.LogCrash(ex, "Console application");
    Console.WriteLine($"ERROR: {ex.Message}");
    Console.WriteLine($"Log file: {FileLogger.GetLogFilePath()}");
    throw;
}
