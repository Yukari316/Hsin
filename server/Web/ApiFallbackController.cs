using Microsoft.AspNetCore.Mvc;

namespace Hsin.Web;

[ApiController]
[Route("api/{**path}", Order = 1000)]
public sealed class ApiFallbackController : ControllerBase
{
    [AcceptVerbs(
        "GET",
        "POST",
        "PUT",
        "PATCH",
        "DELETE",
        "OPTIONS",
        "HEAD")]
    public IActionResult NotFoundApi() => NotFound();
}
