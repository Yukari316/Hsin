using Hsin.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hsin.Web;

[ApiController]
[Route("assets/appearance")]
public sealed class AppearanceController : ControllerBase
{
    private readonly ResolvedConfiguration configuration;

    public AppearanceController(ResolvedConfiguration configuration)
    {
        this.configuration = configuration;
    }

    [AllowAnonymous]
    [HttpGet("logo")]
    public IActionResult Logo() => Resource("logo");

    [Authorize]
    [HttpGet("showcase")]
    public IActionResult Showcase() => Resource("showcase");

    [Authorize]
    [HttpGet("idle-icon")]
    public IActionResult IdleIcon() => Resource("idle-icon");

    private IActionResult Resource(string key)
    {
        if (!configuration.Resources.TryGetValue(key, out AppearanceResource? resource))
            return NotFound();

        Response.Headers.ETag         = "\"" + resource.ETag + "\"";
        Response.Headers.CacheControl = "no-store";
        return File(resource.Content.ToArray(), resource.ContentType);
    }
}
