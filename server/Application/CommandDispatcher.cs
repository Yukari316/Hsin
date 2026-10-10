using System.Net;
using Hsin.Configuration;
using Hsin.Logging;
using Hsin.Media.Control;
using Hsin.Media.Installation;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;

namespace Hsin.Application;

internal sealed class CommandDispatcher
{
    private const int ACCESS_PERMIT_LIMIT   = 10;
    private const int ACCESS_WINDOW_MINUTES = 1;

    public async Task<int> ExecuteAsync(CommandOptions command)
    {
        string configPath = command.ConfigPath
                            ?? throw new InvalidOperationException("The configuration path is absent.");
        string dataDirectory = await ConfigurationLoader.ReadDataDirectoryAsync(configPath);
        Directory.CreateDirectory(dataDirectory);
        string lockPath = Path.Combine(dataDirectory, "hsin.lock");
        await using FileStream instanceLock = new(
            lockPath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);

        using ResolvedConfiguration configuration = await ConfigurationLoader.LoadAsync(configPath);
        LogSession                  logSession    = LogSetup.Create(configuration, DateTimeOffset.Now);
        Log.Logger = logSession.Logger;
        using LogRetentionService? logRetention =
            logSession.FilePath is { } activeLogFile && configuration.LogDirectory is { } logDirectory
                ? new LogRetentionService(
                    logDirectory,
                    activeLogFile,
                    configuration.Options.Logging.RetentionDays,
                    Log.Logger)
                : null;
        logRetention?.Cleanup(DateTimeOffset.UtcNow);
        logRetention?.Start();

        return command.Kind switch
               {
                   CommandKind.Update or CommandKind.Rollback => await RunMaintenanceAsync(command, configuration),
                   CommandKind.Server => await RunServerAsync(command, configuration),
                   _ => throw new ArgumentException("The guardian command must run before building a host.")
               };
    }

    private static async Task<int> RunMaintenanceAsync(
        CommandOptions        command,
        ResolvedConfiguration configuration)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Services.AddHsinServices(configuration, Log.Logger);
        using IHost                 host         = builder.Build();
        MediaMtxInstallationService installation = host.Services.GetRequiredService<MediaMtxInstallationService>();
        if (command.Kind == CommandKind.Update)
        {
            InstallationState prepared = await installation.PrepareAsync(command.Version, CancellationToken.None);
            Log.Information(
                "Prepared MediaMTX {Version} for the next Hsin start",
                prepared.Pending?.ReportedVersion ?? prepared.Active?.ReportedVersion);
        }
        else
        {
            await installation.PrepareRollbackAsync(CancellationToken.None);
            Log.Information("Prepared the previous MediaMTX version for the next Hsin start");
        }

        return 0;
    }

    private static async Task<int> RunServerAsync(CommandOptions command, ResolvedConfiguration configuration)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                Args            = [],
                ContentRootPath = AppContext.BaseDirectory,
                WebRootPath     = "wwwroot"
            });
        builder.Host.UseSerilog(Log.Logger, false);
        builder.Services.AddHsinServices(configuration, Log.Logger);
        builder.Services.AddControllers();
        builder.WebHost.ConfigureKestrel(server =>
        {
            WebUiOptions web     = configuration.Options.WebUi;
            IPAddress    address = ParseListenAddress(web.ListenAddress);
            server.Listen(
                address,
                web.Port,
                listen =>
                {
                    if (configuration.Certificate is not null)
                        listen.UseHttps(https =>
                        {
                            https.ServerCertificate      = configuration.Certificate;
                            https.ServerCertificateChain = configuration.CertificateChain;
                        });
                });
        });
        builder.Services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddFixedWindowLimiter(
                "access",
                settings =>
                {
                    settings.PermitLimit = ACCESS_PERMIT_LIMIT;
                    settings.Window      = TimeSpan.FromMinutes(ACCESS_WINDOW_MINUTES);
                    settings.QueueLimit  = 0;
                });
        });

        await using WebApplication app = builder.Build();
        app.UseRateLimiter();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.UseAuthentication();
        app.UseAuthorization();

        RoomMonitor monitor = app.Services.GetRequiredService<RoomMonitor>();

        app.MapControllers();
        app.MapFallbackToFile("index.html");

        MediaMtxStartupService startup  = app.Services.GetRequiredService<MediaMtxStartupService>();
        MediaMtxProcessService process  = app.Services.GetRequiredService<MediaMtxProcessService>();
        InstalledMediaVersion  selected = await startup.StartAsync(command.MediaPackage, CancellationToken.None);
        if (process.HasExited)
            throw new IOException($"MediaMTX exited after startup with code {process.ExitCode}.");

        monitor.Start();
        Log.Information(
            "Hsin listening on {Address}:{Port} with MediaMTX {Version}",
            configuration.Options.WebUi.ListenAddress,
            configuration.Options.WebUi.Port,
            selected.ReportedVersion);
        try
        {
            Task webRun    = app.RunAsync();
            Task mediaExit = process.WaitForExitAsync(CancellationToken.None);
            Task first     = await Task.WhenAny(webRun, mediaExit);
            if (first == mediaExit)
            {
                await mediaExit;
                Log.Error("MediaMTX exited unexpectedly with code {ExitCode}; stopping Hsin", process.ExitCode);
                await app.StopAsync();
                await webRun;
                return 1;
            }

            await webRun;
            return 0;
        }
        finally
        {
            await monitor.DisposeAsync();
            await process.StopAsync(CancellationToken.None);
        }
    }

    private static IPAddress ParseListenAddress(string configured)
    {
        if (configured == "*")
            return IPAddress.Any;
        return configured.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            ? IPAddress.Loopback
            : IPAddress.Parse(configured.Trim('[', ']'));
    }
}
