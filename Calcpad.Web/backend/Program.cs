using Calcpad.Server;
using Calcpad.Server.Services;
using System.Net;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

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

// ProcessExit fires on Environment.Exit and clean Main return, but NOT on
// FailFast or StackOverflow. Useful for catching the "graceful but unexpected"
// exit path — and for flushing the log on normal shutdown.
AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
{
    FileLogger.LogInfo("ProcessExit fired", $"ExitCode={Environment.ExitCode}");
    FileLogger.Flush();
};

try
{
    FileLogger.LogInfo("Starting Calcpad Server");

    // Set default environment variables if not set
    Environment.SetEnvironmentVariable("CALCPAD_HOST", Environment.GetEnvironmentVariable("CALCPAD_HOST") ?? "127.0.0.1");

    // Pull our custom CLI flags out of args before handing them to ASP.NET.
    // --port-file <path>          Write the bound base URL to this file once
    //                             Kestrel is listening. Used by the Neutralino
    //                             desktop to discover the random-port server
    //                             without hard-coding 9420.
    // --exit-on-stdin-close       When stdin reaches EOF (parent process died
    //                             and dropped our stdin pipe), exit. Default
    //                             for Neutralino-spawned launches; opt out
    //                             via env CALCPAD_DETACHED=1 (VS Code does
    //                             this so the server outlives the spawning
    //                             window and is shared across instances).
    // --no-exit-on-stdin-close    Force-disable the watchdog (overrides the
    //                             default-on logic for stdin-piped launches).
    string? portFile = null;
    bool? exitOnStdinCloseExplicit = null;
    var passthroughArgs = new List<string>(args.Length);
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--port-file" && i + 1 < args.Length)
        {
            portFile = args[++i];
        }
        else if (args[i] == "--exit-on-stdin-close")
        {
            exitOnStdinCloseExplicit = true;
        }
        else if (args[i] == "--no-exit-on-stdin-close")
        {
            exitOnStdinCloseExplicit = false;
        }
        else
        {
            passthroughArgs.Add(args[i]);
        }
    }
    var forwardedArgs = passthroughArgs.ToArray();

    // Decide whether the EOF watchdog runs. Default-on when stdin is piped
    // (we're a child of *something* that may die and orphan us), opt-out
    // via CALCPAD_DETACHED=1 (the VS Code extension's server-manager.ts
    // sets this because it shares one server across windows).
    bool detached = Environment.GetEnvironmentVariable("CALCPAD_DETACHED") == "1";
    bool exitOnStdinClose = exitOnStdinCloseExplicit ?? (Console.IsInputRedirected && !detached);

    // Default the port file to a fixed location next to the binary when no
    // path was given explicitly. Always wipe any stale file at startup so
    // the frontend never reads a URL pointing at a previous (dead) server.
    if (portFile == null && !detached)
    {
        portFile = Path.Combine(AppContext.BaseDirectory, ".calcpad-server.port");
    }
    if (!string.IsNullOrEmpty(portFile))
    {
        try { if (File.Exists(portFile)) File.Delete(portFile); } catch { /* best-effort */ }
    }

    if (exitOnStdinClose && Console.IsInputRedirected)
    {
        // Death-detection background task. Two modes, picked by sniffing the
        // first stdin line:
        //
        //   1. Neutralino extension protocol — Neutralino writes a single
        //      JSON line ({nlPort, nlToken, nlConnectToken, nlExtensionId})
        //      then *closes its end of stdin*. Stdin EOFs immediately, so
        //      EOF is a useless death signal here. Instead the extension
        //      must open ws://localhost:{nlPort}?extensionId=...&connectToken=...
        //      and exit when that socket closes (per
        //      https://neutralino.js.org/docs/how-to/extensions-overview).
        //
        //   2. Anything else (raw piped-stdin parent like a build script):
        //      stdin stays open until the parent dies, so reading until EOF
        //      is the correct death signal.
        _ = Task.Run(async () =>
        {
            try
            {
                var firstLine = await Console.In.ReadLineAsync().ConfigureAwait(false);
                NeutralinoExtensionInfo? neuInfo = TryParseNeutralinoInfo(firstLine);

                if (neuInfo is not null)
                {
                    FileLogger.LogInfo("Neutralino extension protocol detected",
                        $"nlPort={neuInfo.NlPort} extensionId={neuInfo.NlExtensionId}");
                    await WatchNeutralinoSocketAsync(neuInfo).ConfigureAwait(false);
                    FileLogger.LogInfo("Neutralino WebSocket closed — parent exited; shutting down");
                }
                else
                {
                    while (await Console.In.ReadLineAsync().ConfigureAwait(false) != null) { /* drain */ }
                    FileLogger.LogInfo("stdin EOF; parent likely exited — shutting down");
                }
            }
            catch (Exception ex)
            {
                FileLogger.LogInfo("watchdog error", ex.Message);
            }

            // Environment.Exit bypasses ApplicationStopping callbacks, so
            // clean up the port file ourselves before going down — otherwise
            // the next launch sees a stale URL pointing at a dead server.
            if (!string.IsNullOrEmpty(portFile))
            {
                try { if (File.Exists(portFile)) File.Delete(portFile); } catch { /* best-effort */ }
            }
            Environment.Exit(0);
        });
    }

    // When nothing was set explicitly (no --urls, no CALCPAD_PORT env), prefer
    // an OS-assigned random port over the legacy hard-coded 9420 — this is
    // what Neutralino-extension launches want, and it eliminates the
    // "address already in use" failure mode for orphan processes. Callers
    // that need a fixed port (`dotnet run` for dev, the VS Code extension
    // explicitly passing --urls) keep their existing behavior.
    bool hasExplicitUrls = forwardedArgs.Any(a => a == "--urls");
    bool hasExplicitPort = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CALCPAD_PORT"));
    if (!hasExplicitUrls && !hasExplicitPort)
    {
        // Kestrel rejects "localhost:0" with "Dynamic port binding is not
        // supported when binding to localhost" — must be the loopback IP.
        forwardedArgs = forwardedArgs.Concat(new[] { "--urls", "http://127.0.0.1:0" }).ToArray();
        FileLogger.LogInfo("No explicit URL or port set", "defaulting to http://127.0.0.1:0 (random free port)");
    }
    else if (!hasExplicitUrls)
    {
        // CALCPAD_PORT was set explicitly — preserve the legacy 9420 default
        // for that path through GetServerUrl.
        Environment.SetEnvironmentVariable("CALCPAD_PORT", Environment.GetEnvironmentVariable("CALCPAD_PORT") ?? "9420");
    }

    // Create and configure web application using shared service
    var (app, serverUrl) = CalcpadApiService.CreateConfiguredApp(forwardedArgs);

    // Server mode (non-localhost binding) is in development and not yet supported:
    // the include-resolution path that ships uploaded files from a remote browser
    // is being reworked. Crash early if anything resolves to a non-loopback URL.
    foreach (var u in serverUrl.Split(';', StringSplitOptions.RemoveEmptyEntries))
    {
        if (!Program.IsLoopbackUrl(u))
            throw new InvalidOperationException(
                $"Calcpad server is bound to '{u}' which is not localhost. " +
                "Server mode is in development and not yet supported. " +
                "Set CALCPAD_HOST=127.0.0.1 or pass --urls http://127.0.0.1:<port>.");
    }

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

    // When --port-file was given, write the bound URL once Kestrel is
    // listening. The frontend (Neutralino desktop) polls this file to
    // discover the random-port server without hard-coding 9420.
    if (!string.IsNullOrEmpty(portFile))
    {
        lifetime.ApplicationStarted.Register(() =>
        {
            try
            {
                // Use the first listening URL — Kestrel resolves any
                // wildcard/random binding to a concrete one by this point.
                var addresses = app.Urls.ToList();
                var bound = addresses.FirstOrDefault() ?? serverUrl;
                File.WriteAllText(portFile, bound);
                FileLogger.LogInfo("Wrote port file", $"{portFile} -> {bound}");
            }
            catch (Exception ex)
            {
                FileLogger.LogError($"Failed to write port file {portFile}", ex);
            }
        });
        lifetime.ApplicationStopping.Register(() =>
        {
            try { if (File.Exists(portFile)) File.Delete(portFile); } catch { /* best-effort */ }
        });
    }

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

// Note: createdump (DOTNET_DbgEnableMiniDump and friends) must be set in the
// child process's environment BEFORE the runtime starts up — setting them from
// inside Main is too late. The VS Code extension's spawn-time env in
// calcpad-frontend/server-manager.ts owns this configuration.

// ─── Neutralino extension protocol helpers ────────────────────────────────────

/// <summary>
/// First stdin line written by Neutralino to a freshly-spawned extension. See
/// https://neutralino.js.org/docs/how-to/extensions-overview — the extension
/// must use these to open a WebSocket back to Neutralino, then exit when the
/// socket closes (Neutralino does not SIGTERM extension processes on exit).
/// </summary>
internal sealed class NeutralinoExtensionInfo
{
    [JsonPropertyName("nlPort")]          public string NlPort { get; set; } = "";
    [JsonPropertyName("nlToken")]         public string NlToken { get; set; } = "";
    [JsonPropertyName("nlConnectToken")]  public string NlConnectToken { get; set; } = "";
    [JsonPropertyName("nlExtensionId")]   public string NlExtensionId { get; set; } = "";
}

internal static partial class Program
{
    /// <summary>
    /// True if the URL's host resolves to a loopback address (localhost, 127.0.0.0/8, ::1).
    /// Used to gate server-mode bindings until the remote-include path is finished.
    /// </summary>
    internal static bool IsLoopbackUrl(string urlString)
    {
        if (!Uri.TryCreate(urlString, UriKind.Absolute, out var uri)) return false;
        var host = uri.Host;
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
        return IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip);
    }

    /// <summary>
    /// Try to parse a stdin line as Neutralino's extension-handshake JSON.
    /// Returns null on anything that doesn't structurally match.
    /// </summary>
    internal static NeutralinoExtensionInfo? TryParseNeutralinoInfo(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        var trimmed = line.TrimStart();
        if (!trimmed.StartsWith('{')) return null;
        try
        {
            var info = JsonSerializer.Deserialize<NeutralinoExtensionInfo>(line);
            if (info is null) return null;
            // All four fields must be populated for the handshake to be valid.
            if (string.IsNullOrEmpty(info.NlPort)
                || string.IsNullOrEmpty(info.NlConnectToken)
                || string.IsNullOrEmpty(info.NlExtensionId))
            {
                return null;
            }
            return info;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Connect to the Neutralino extension WebSocket and block until it closes.
    /// Closure means Neutralino exited; the caller should then exit too.
    /// </summary>
    internal static async Task WatchNeutralinoSocketAsync(NeutralinoExtensionInfo info)
    {
        using var ws = new ClientWebSocket();
        var uri = new Uri(
            $"ws://localhost:{info.NlPort}?extensionId={Uri.EscapeDataString(info.NlExtensionId)}"
            + $"&connectToken={Uri.EscapeDataString(info.NlConnectToken)}");
        try
        {
            await ws.ConnectAsync(uri, CancellationToken.None).ConfigureAwait(false);
            FileLogger.LogInfo("Connected to Neutralino extension WebSocket", uri.ToString());

            var buffer = new byte[4096];
            while (ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None)
                                     .ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close) break;
                // Discard any inbound messages — Neutralino uses this channel
                // for extension RPC, which we don't participate in. Just
                // staying connected is enough to detect the close.
            }
        }
        catch (Exception ex)
        {
            // Connection failure or read error means we can't detect parent
            // death this way. Treat that as "parent gone" and let the
            // caller exit, rather than hanging forever.
            FileLogger.LogInfo("Neutralino WebSocket error (treating as disconnect)", ex.Message);
        }
    }
}
