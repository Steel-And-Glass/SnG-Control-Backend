using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SnG.Control.Tests.TestDoubles;

// This scheme exists only in Tests and never validates or issues Google tokens.
public sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, IOptionsMonitor<JwtBearerOptions> googleOptions) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "TestIdentity";
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (header.Length == 0) return AuthenticateResult.NoResult();
        if (!header.StartsWith(SchemeName + " ", StringComparison.Ordinal)) return AuthenticateResult.Fail("Invalid test authentication.");
        var mode = header[(SchemeName.Length + 1)..];
        if (mode is not ("valid" or "missing-sub" or "empty-sub" or "subject-only" or "unverified" or "other" or "head-other-email" or "outsider" or "creator-other-email" or "budgets" or "accounting" or "management" or "management-other-email")) return AuthenticateResult.Fail("Invalid test identity.");
        var claims = new List<Claim>();
        if (mode != "missing-sub") claims.Add(new Claim("sub", mode == "empty-sub" ? " " : mode is "management" or "management-other-email" ? "management-test-subject" : mode == "accounting" ? "accounting-test-subject" : mode == "budgets" ? "budgets-test-subject" : mode is "other" or "creator-other-email" ? "other-google-subject" : mode == "outsider" ? "third-google-subject" : "google-test-subject"));
        if (mode != "subject-only")
        {
            claims.Add(new Claim("email", mode is "head-other-email" or "creator-other-email" or "management-other-email" ? "changed@example.com" : "test@example.com")); claims.Add(new Claim("name", "Test User"));
            claims.Add(new Claim("email_verified", mode == "unverified" ? "false" : "true", ClaimValueTypes.Boolean));
        }
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        // Apply the production post-validation subject rule to this synthetic identity.
        var google = googleOptions.Get(JwtBearerDefaults.AuthenticationScheme);
        var validated = new TokenValidatedContext(Context, new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme,
            null, typeof(JwtBearerHandler)), google)
        { Principal = principal };
        await google.Events.TokenValidated(validated);
        if (validated.Result?.Failure is not null) return AuthenticateResult.Fail(validated.Result.Failure);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }
}
