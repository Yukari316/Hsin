using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Hsin.Configuration;
using Hsin.Media.Control;
using Hsin.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Core;

namespace Hsin.Tests;

public sealed class HsinBearerAuthenticationTests
{
    [Fact]
    public async Task ProtectedRoomUsesBearerAndPublicBootstrapStaysAnonymous()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "hsin-api-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string configPath = Path.Combine(directory, "hsin.json");
            await File.WriteAllBytesAsync(Path.Combine(directory, "mobile.png"), [7, 8, 9]);
            await File.WriteAllTextAsync(
                configPath,
                """
                {"WebUi":{"Token":"测试口令"},"Appearance":{"MobileBackgroundFile":"mobile.png"},"Logging":{"FileEnabled":false}}
                """);
            using ResolvedConfiguration configuration = await ConfigurationLoader.LoadAsync(configPath);
            using MediaMtxApiClient     api           = new(configuration);
            using Logger                logger        = new LoggerConfiguration().CreateLogger();
            await using RoomMonitor     monitor       = new(api, logger);
            WebApplicationBuilder       builder       = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(configuration);
            builder.Services.AddSingleton(monitor);
            builder.Services
                   .AddAuthentication("HsinBearer")
                   .AddScheme<HsinBearerOptions, HsinBearerAuthenticationHandler>(
                       "HsinBearer",
                       options => options.Token = configuration.Options.WebUi.Token);
            builder.Services.AddAuthorization();
            builder.Services
                   .AddControllers()
                   .AddApplicationPart(typeof(Program).Assembly);
            await using WebApplication app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapControllers();
            await app.StartAsync();
            using HttpClient client = app.GetTestClient();

            string bootstrap = await client.GetStringAsync("/api/bootstrap");
            Assert.DoesNotContain("测试口令", bootstrap);
            Assert.DoesNotContain("publisher:", bootstrap);
            using (JsonDocument document = JsonDocument.Parse(bootstrap))
            {
                Assert.Equal(
                    "/assets/appearance/background-mobile",
                    document.RootElement.GetProperty("mobileBackgroundUrl").GetString());
                Assert.False(document.RootElement.TryGetProperty("backgroundLeftWidth", out _));
            }

            using HttpResponseMessage mobileBackground = await client.GetAsync("/assets/appearance/background-mobile");
            Assert.Equal(HttpStatusCode.OK, mobileBackground.StatusCode);
            Assert.Equal([7, 8, 9], await mobileBackground.Content.ReadAsByteArrayAsync());

            using HttpResponseMessage anonymous = await client.GetAsync("/api/room");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            Assert.Equal("Bearer", anonymous.Headers.WwwAuthenticate.Single().Scheme);

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes("viewer:测试口令")));
            using HttpResponseMessage basic = await client.PostAsync("/api/access", null);
            Assert.Equal(HttpStatusCode.Unauthorized, basic.StatusCode);

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                "错误口令");
            using HttpResponseMessage invalid = await client.PostAsync("/api/access", null);
            Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                "测试口令");
            using HttpResponseMessage access = await client.PostAsync("/api/access", null);
            Assert.Equal(HttpStatusCode.NoContent, access.StatusCode);
            string room = await client.GetStringAsync("/api/room");
            Assert.Contains("initializing", room);
            string announcement = await client.GetStringAsync("/api/announcement");
            Assert.Empty(announcement);

            using HttpResponseMessage missing = await client.GetAsync("/api/not-a-route");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task OpenRoomDoesNotRequireBearerToken()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "hsin-open-api-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string configPath = Path.Combine(directory, "hsin.json");
            await File.WriteAllTextAsync(
                configPath,
                """
                {"Logging":{"FileEnabled":false}}
                """);
            using ResolvedConfiguration configuration = await ConfigurationLoader.LoadAsync(configPath);
            using MediaMtxApiClient     api           = new(configuration);
            using Logger                logger        = new LoggerConfiguration().CreateLogger();
            await using RoomMonitor     monitor       = new(api, logger);
            WebApplicationBuilder       builder       = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(configuration);
            builder.Services.AddSingleton(monitor);
            builder.Services
                   .AddAuthentication("HsinBearer")
                   .AddScheme<HsinBearerOptions, HsinBearerAuthenticationHandler>(
                       "HsinBearer",
                       options => options.Token = string.Empty);
            builder.Services.AddAuthorization();
            builder.Services
                   .AddControllers()
                   .AddApplicationPart(typeof(Program).Assembly);
            await using WebApplication app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapControllers();
            await app.StartAsync();
            using HttpClient client = app.GetTestClient();

            using HttpResponseMessage access = await client.PostAsync("/api/access", null);
            Assert.Equal(HttpStatusCode.NoContent, access.StatusCode);
            using HttpResponseMessage room = await client.GetAsync("/api/room");
            Assert.Equal(HttpStatusCode.OK, room.StatusCode);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
