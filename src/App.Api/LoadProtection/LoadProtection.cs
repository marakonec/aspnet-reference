using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using App.Shared;

namespace App.Api.LoadProtection;

public static class LoadProtectionSetup
{
    public const string ModelsPolicy = "models";

    public const string ModelsTag = "models";

    public const string IssuePolicy = "issue";

    public static readonly TimeSpan ModelsExpiration = TimeSpan.FromSeconds(60);

    public const int IssuePermitsPerSecond = 5;

    public static void AddAppLoadProtection(this WebApplicationBuilder builder)
    {
        builder.Services.AddOutputCache(options =>
        {
            options.AddPolicy(ModelsPolicy, policy => policy
                .Expire(ModelsExpiration)
                .Tag(ModelsTag));
        });

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddFixedWindowLimiter(IssuePolicy, limiter =>
            {
                limiter.PermitLimit = IssuePermitsPerSecond;
                limiter.Window = TimeSpan.FromSeconds(1);
                limiter.QueueLimit = 0;
            });

            options.OnRejected = async (context, cancellationToken) =>
            {
                var seconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                    ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
                    : 1;
                var http = context.HttpContext;
                http.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);

                var log = http.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("App.Api.LoadProtection");
                log.LogWarning("{Event} path={Path} retryAfterSeconds={RetryAfter}",
                    AppEvents.RateLimitRejected, http.Request.Path, seconds);

                await Results.Problem(
                        statusCode: StatusCodes.Status429TooManyRequests,
                        title: "Забагато запитів",
                        detail: $"Ліміт ключової операції вичерпано. Повторити можна через {seconds} с")
                    .ExecuteAsync(http);
            };
        });
    }
}
