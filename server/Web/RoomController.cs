using Hsin.Configuration;
using Hsin.Media.Control;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Hsin.Web;

[ApiController]
[Route("api")]
public sealed class RoomController : ControllerBase
{
    private readonly ResolvedConfiguration configuration;
    private readonly RoomMonitor           monitor;

    public RoomController(ResolvedConfiguration configuration, RoomMonitor monitor)
    {
        this.configuration = configuration;
        this.monitor       = monitor;
    }

    [AllowAnonymous]
    [HttpGet("bootstrap")]
    public IActionResult Bootstrap()
    {
        HsinOptions options = configuration.Options;
        return new JsonResult(
            new
            {
                requiresToken    = !string.IsNullOrEmpty(options.WebUi.Token),
                title            = options.Appearance.Title,
                logoUrl          = ResourceUrl("logo"),
                showcaseImageUrl = ResourceUrl("showcase"),
                passwordPrompt   = options.Appearance.PasswordPrompt
            });
    }

    [Authorize]
    [EnableRateLimiting("access")]
    [HttpPost("access")]
    public IActionResult Access()
    {
        Response.Headers.CacheControl = "no-store";
        return NoContent();
    }

    [Authorize]
    [HttpGet("room")]
    public IActionResult Room()
    {
        Response.Headers.CacheControl = "no-store";
        HsinOptions  options  = configuration.Options;
        RoomSnapshot snapshot = monitor.Snapshot;
        return new JsonResult(
            new
            {
                status      = snapshot.Status,
                generation  = snapshot.Generation,
                whepUrl     = options.MediaMtx.PublicOrigin.TrimEnd('/') + "/live/whep",
                idleMessage = options.Appearance.IdleMessage,
                idleIconUrl = ResourceUrl("idle-icon")
            });
    }

    [Authorize]
    [HttpGet("announcement")]
    public IActionResult Announcement()
    {
        Response.Headers.CacheControl = "no-store";
        return Content(configuration.Announcement, "text/markdown; charset=utf-8");
    }

    private string? ResourceUrl(string key)
        => configuration.Resources.ContainsKey(key)
            ? "/assets/appearance/" + key
            : null;
}
