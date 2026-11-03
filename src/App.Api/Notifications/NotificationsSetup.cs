using Microsoft.Extensions.Http.Resilience;
using Polly;
using App.Shared;

namespace App.Api.Notifications;

public static class NotificationsSetup
{
    public static void AddAppNotifications(this WebApplicationBuilder builder)
    {
        var baseUrl = builder.Configuration["Notifier:BaseUrl"] ?? "http://localhost:15110";

        var client = builder.Services.AddHttpClient<NotifierClient>(http =>
        {
            http.BaseAddress = new Uri(baseUrl);
        });

        client.AddResilienceHandler("notifier", (pipeline, context) =>
            {
                var log = context.ServiceProvider.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("App.Api.Resilience");

                pipeline.AddTimeout(TimeSpan.FromSeconds(3));

                var retry = new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 2,
                    BackoffType = DelayBackoffType.Constant,
                    Delay = TimeSpan.FromMilliseconds(200)
                };
#pragma warning disable EXTEXP0001
                retry.DisableFor(HttpMethod.Post);
#pragma warning restore EXTEXP0001
                pipeline.AddRetry(retry);

                pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    FailureRatio = 0.5,
                    MinimumThroughput = 4,
                    SamplingDuration = TimeSpan.FromSeconds(30),
                    BreakDuration = TimeSpan.FromSeconds(15),
                    OnOpened = args =>
                    {
                        log.LogWarning("{Event} breakSeconds={BreakSeconds}",
                            AppEvents.CircuitOpened, (int)args.BreakDuration.TotalSeconds);
                        return ValueTask.CompletedTask;
                    },
                    OnHalfOpened = _ =>
                    {
                        log.LogInformation("{Event}", AppEvents.CircuitHalfOpened);
                        return ValueTask.CompletedTask;
                    },
                    OnClosed = _ =>
                    {
                        log.LogInformation("{Event}", AppEvents.CircuitClosed);
                        return ValueTask.CompletedTask;
                    }
                });

                pipeline.AddTimeout(TimeSpan.FromSeconds(1));
            });
    }
}
