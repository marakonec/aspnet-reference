using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using App.Api;
using App.Api.Domain;
using App.Api.Endpoints;
using App.Api.LoadProtection;
using App.Api.Notifications;
using App.Api.Observability;
using App.Api.Persistence;
using App.Api.Security;
using App.Shared;

var builder = WebApplication.CreateBuilder(args);

var switches = builder.Configuration.GetSection(FeatureSwitches.SectionName)
    .Get<FeatureSwitches>() ?? new FeatureSwitches();
builder.Services.AddSingleton(switches);

builder.AddAppTelemetry();
builder.Services.AddSingleton<AppMetrics>();

// Строк перевірки менший за строк очікування драйвера, щоб зупинена база давала
// відповідь «не готовий» за дві секунди.
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>(DatabaseHealthCheck.Name,
        tags: [DatabaseHealthCheck.ReadyTag], timeout: TimeSpan.FromSeconds(2));

builder.Services.AddOpenApi(OpenApiSecurity.Configure);
builder.Services.AddValidation();
builder.Services.AddProblemDetails(ProblemDetailsSetup.Configure);
builder.Services.AddExceptionHandler<BadRequestBodyHandler>();

// Без цього рядка зіпсоване тіло запиту доходить до обробника винятків лише в середовищі
// розробки, а в інших середовищах дає відповідь 400 без опису проблеми.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

builder.AddAppLoadProtection();

if (switches.UsesDatabase)
{
    // Рядок підключення читається під час створення контексту, бо тестовий хост
    // підміняє його після складання служб.
    builder.Services.AddDbContext<AppDbContext>((sp, options) =>
        options.UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("Default")));
    builder.Services.AddScoped<IRentalStore, DatabaseRentalStore>();
}
else
{
    builder.Services.AddSingleton<RentalStore>();
    builder.Services.AddSingleton<IRentalStore>(sp => sp.GetRequiredService<RentalStore>());
}

builder.AddAppAccess();
builder.Services.AddScoped<ReservationAccess>();

builder.AddAppNotifications();

var app = builder.Build();

if (switches.UsesDatabase)
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    app.Logger.LogInformation("{Event} provider={Provider}",
        AppEvents.MigrationsApplied, db.Database.ProviderName);

    await SeedData.EnsureSeededAsync(db, app.Logger);
}

app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var feature = context.Features.Get<IExceptionHandlerFeature>();
    var log = context.RequestServices.GetRequiredService<ILogger<Program>>();
    log.LogError(feature?.Error, "{Event} path={Path}",
        AppEvents.UnhandledException, context.Request.Path);

    await Results.Problem(
            title: "Необроблена помилка",
            detail: "Подробиці не розкриваються",
            statusCode: StatusCodes.Status500InternalServerError)
        .ExecuteAsync(context);
}));

app.Use(async (context, next) =>
{
    var log = context.RequestServices.GetRequiredService<ILogger<Program>>();
    log.LogInformation("{Event} method={Method} path={Path}",
        AppEvents.PipelineEnter, context.Request.Method, context.Request.Path);

    await next(context);

    log.LogInformation("{Event} status={Status} path={Path}",
        AppEvents.PipelineExit, context.Response.StatusCode, context.Request.Path);
});

// Обробники автентифікації й авторизації додано явно після рядка логу конвеєра.
// Без цих рядків платформа поставила б їх раніше, і відхилений запит не лишав би
// в лозі рядків входу і виходу.
app.UseAuthentication();
app.UseAuthorization();

app.UseRateLimiter();

// Вихідний кеш стоїть після авторизації, щоб відповідь для одного користувача
// не потрапила до іншого.
app.UseOutputCache();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapGet("/health/live", () => Results.Ok(new { status = "live" }))
    .ExcludeFromDescription();

app.MapHealthChecks("/health/ready", DatabaseHealthCheck.ReadyOptions())
    .AllowAnonymous();

app.MapRental();

app.Logger.LogInformation("{Event} environment={Environment} persistence={Persistence}",
    AppEvents.Startup, app.Environment.EnvironmentName, switches.Persistence);

app.Run();

public partial class Program;
