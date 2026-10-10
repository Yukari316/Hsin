using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Hsin.Web;

internal sealed class HsinBearerOptions : AuthenticationSchemeOptions
{
    public string Token { get; set; } = string.Empty;
}

internal sealed class HsinBearerAuthenticationHandler : AuthenticationHandler<HsinBearerOptions>
{
    public HsinBearerAuthenticationHandler(
        IOptionsMonitor<HsinBearerOptions> options,
        ILoggerFactory                     logger,
        UrlEncoder                         encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (string.IsNullOrEmpty(Options.Token))
            return Task.FromResult(Success());

        string authorization = Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.NoResult());

        string receivedToken = authorization["Bearer ".Length..].Trim();
        byte[] received      = Encoding.UTF8.GetBytes(receivedToken);
        byte[] expected      = Encoding.UTF8.GetBytes(Options.Token);
        if (received.Length != expected.Length
            || !CryptographicOperations.FixedTimeEquals(received, expected))
            return Task.FromResult(AuthenticateResult.Fail("Invalid bearer token."));

        return Task.FromResult(Success());
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode              = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer";
        return Task.CompletedTask;
    }

    private AuthenticateResult Success()
    {
        ClaimsIdentity identity = new([new Claim(ClaimTypes.Name, "hsin-viewer")], Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}
