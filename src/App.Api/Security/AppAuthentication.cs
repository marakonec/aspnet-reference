using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using App.Shared;

namespace App.Api.Security;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public string Authority { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;
}

public static class AppAuthentication
{
    public static class Roles
    {
        public const string Operator = "operator";
        public const string Customer = "customer";
    }

    public static class Policies
    {
        public const string OwnReservation = "app.own-reservation";
    }

    public const string RoleClaim = "roles";

    public const string OwnerIdClaim = "owner_id";

    public const string UserNameClaim = "preferred_username";

    public static void AddAppAccess(this WebApplicationBuilder builder)
    {
        var auth = builder.Configuration.GetSection(AuthOptions.SectionName)
            .Get<AuthOptions>() ?? new AuthOptions();
        builder.Services.AddSingleton(auth);

        builder.Services.AddSingleton<AuthFailureCounters>();

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = auth.Authority;
                options.Audience = auth.Audience;

                options.RequireHttpsMetadata = builder.Configuration.GetValue(
                    "Auth:RequireHttpsMetadata", !builder.Environment.IsDevelopment());

                options.MapInboundClaims = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,

                    ValidateIssuer = true,
                    ValidIssuer = auth.Authority,

                    ValidateAudience = true,
                    ValidAudience = auth.Audience,

                    ValidateLifetime = true,

                    ClockSkew = TimeSpan.Zero,

                    NameClaimType = UserNameClaim,
                    RoleClaimType = RoleClaim
                };

                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = OnFailedAsync,
                    OnChallenge = OnChallengeAsync,
                    OnTokenValidated = OnValidatedAsync
                };
            });

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(Policies.OwnReservation, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.Requirements.Add(new OwnReservationRequirement());
            });

        builder.Services.AddSingleton<
            Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
            OwnReservationHandler>();
    }

    public const string LogCategory = "App.Api.Security.AppAuthentication";

    private static ILogger AccessLog(HttpContext http) =>
        http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(LogCategory);

    private static Task OnFailedAsync(AuthenticationFailedContext context)
    {
        var log = AccessLog(context.HttpContext);
        var counters = context.HttpContext.RequestServices.GetRequiredService<AuthFailureCounters>();
        var auth = context.HttpContext.RequestServices.GetRequiredService<AuthOptions>();

        var (reason, text) = Classify(context, auth);

        counters.Count(reason);
        log.LogWarning("{Event} reason={Reason} detail={Detail} path={Path}",
            AppEvents.TokenRejected, reason, text, context.Request.Path);

        return Task.CompletedTask;
    }

    private static (string Reason, string Text) Classify(
        AuthenticationFailedContext context, AuthOptions auth) => context.Exception switch
    {
        SecurityTokenExpiredException =>
            (AuthReasons.Expired, "строк дії токена минув"),

        SecurityTokenInvalidAudienceException =>
            (AuthReasons.WrongAudience, "токен призначений іншому сервісу"),

        SecurityTokenInvalidIssuerException =>
            (AuthReasons.ForeignIssuer, "токен видала інша область"),

        SecurityTokenSignatureKeyNotFoundException or SecurityTokenInvalidSignatureException =>
            LooksForeign(context, auth)
                ? (AuthReasons.ForeignIssuer, "ключ підпису невідомий, у тілі токена інший видавець")
                : (AuthReasons.BadSignature, "підпис не сходиться"),

        _ => (AuthReasons.OtherTokenError,
            context.Exception?.GetType().Name ?? "невідома помилка перевірки")
    };

    private static bool LooksForeign(AuthenticationFailedContext context, AuthOptions auth)
    {
        var raw = TokenPeek.FromAuthorizationHeader(context.Request.Headers.Authorization);
        var issuer = TokenPeek.UnverifiedIssuer(raw);
        return issuer is not null
               && !string.Equals(issuer, auth.Authority, StringComparison.Ordinal);
    }

    private static Task OnChallengeAsync(JwtBearerChallengeContext context)
    {
        if (context.AuthenticateFailure is null)
        {
            var log = AccessLog(context.HttpContext);
            var counters = context.HttpContext.RequestServices
                .GetRequiredService<AuthFailureCounters>();

            counters.Count(AuthReasons.Missing);
            log.LogWarning("{Event} reason={Reason} path={Path}",
                AppEvents.AuthChallenge, AuthReasons.Missing, context.Request.Path);
        }

        context.HandleResponse();
        context.Response.Headers.WWWAuthenticate = context.AuthenticateFailure is null
            ? "Bearer"
            : "Bearer error=\"invalid_token\"";
        return Results.Problem(
                title: "Потрібна автентифікація",
                detail: "Запит не містить дійсного токена доступу",
                statusCode: StatusCodes.Status401Unauthorized)
            .ExecuteAsync(context.HttpContext);
    }

    private static Task OnValidatedAsync(TokenValidatedContext context)
    {
        var log = AccessLog(context.HttpContext);
        var user = context.Principal;

        log.LogInformation("{Event} user={User} roles=[{Roles}] owner={Owner}",
            AppEvents.TokenAccepted,
            user?.Identity?.Name ?? "-",
            string.Join(' ', user?.FindAll(RoleClaim).Select(c => c.Value) ?? []),
            user?.FindFirst(OwnerIdClaim)?.Value ?? "-");

        return Task.CompletedTask;
    }
}
