using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using App.Api.Security;

namespace App.Tests;

public static class TestUsers
{
    public const string Header = "X-Test-User";

    public const string Operator = "operator1|operator|";

    public const string CustomerOfRes2 = "customer1|customer|cus-2";
}

public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers[TestUsers.Header].ToString();
        if (string.IsNullOrEmpty(header))
            return Task.FromResult(AuthenticateResult.NoResult());

        var parts = header.Split('|');
        var claims = new List<Claim>
        {
            new(AppAuthentication.UserNameClaim, parts[0]),
            new(AppAuthentication.RoleClaim, parts[1])
        };
        if (parts.Length > 2 && parts[2].Length > 0)
            claims.Add(new(AppAuthentication.OwnerIdClaim, parts[2]));

        var identity = new ClaimsIdentity(claims, SchemeName,
            AppAuthentication.UserNameClaim, AppAuthentication.RoleClaim);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
