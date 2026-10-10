using Hsin.Configuration;
using Hsin.Media;
using Hsin.Media.Control;
using Hsin.Media.Installation;
using Hsin.Media.Process;
using Hsin.Web;
using Microsoft.AspNetCore.Authentication;
using Serilog;
using SerilogLogger = Serilog.ILogger;

namespace Hsin.Application;

internal static class ServiceRegistration
{
    public static void AddHsinServices(
        this IServiceCollection services,
        ResolvedConfiguration   configuration,
        SerilogLogger           logger)
    {
        services.AddSingleton(configuration);
        services.AddSingleton(logger);
        services.AddAuthentication("HsinBearer")
                .AddScheme<HsinBearerOptions, HsinBearerAuthenticationHandler>(
                    "HsinBearer",
                    options => options.Token = configuration.Options.WebUi.Token);
        services.AddAuthorization();
        services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromMinutes(3) });
        services.AddSingleton<MediaMtxReleaseClient>();
        services.AddSingleton(provider => new MediaMtxInstallationService(
                                  configuration.DataDirectory,
                                  provider.GetRequiredService<MediaMtxReleaseClient>(),
                                  logger));
        services.AddSingleton<MediaMtxApiClient>();
        services.AddSingleton<RoomMonitor>();
        if (OperatingSystem.IsWindows())
            services.AddSingleton<IMediaMtxProcess, MediaMtxWindowsProcess>();
        else if (OperatingSystem.IsLinux())
            services.AddSingleton<IMediaMtxProcess, MediaMtxLinuxProcess>();
        else
            throw new PlatformNotSupportedException("Hsin supports Windows and Linux only.");
        services.AddSingleton<MediaMtxProcessService>();
        services.AddSingleton<MediaMtxStartupService>();
    }
}
