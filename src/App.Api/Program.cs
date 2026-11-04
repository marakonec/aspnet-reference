using Microsoft.AspNetCore.Diagnostics;
using Scalar.AspNetCore;
using App.Api;
using App.Api.Domain;
using App.Api.Endpoints;
using App.Api.Observability;
using App.Shared;

var builder = WebApplication.CreateBuilder(args);

builder.AddAppTelemetry();

builder.Services.AddOpenApi();
builder.Services.AddValidation();
builder.Services.AddProblemDetails(ProblemDetailsSetup.Configure);

builder.Services.AddSingleton<RentalStore>();

var app = builder.Build();

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

// Записи журналу на вході і виході пишуться з тим самим ідентифікатором трасування,
// тому за ним у журналі видно весь шлях запиту крізь конвеєр.
app.Use(async (context, next) =>
{
    var log = context.RequestServices.GetRequiredService<ILogger<Program>>();
    log.LogInformation("{Event} method={Method} path={Path}",
        AppEvents.PipelineEnter, context.Request.Method, context.Request.Path);

    await next(context);

    log.LogInformation("{Event} status={Status} path={Path}",
        AppEvents.PipelineExit, context.Response.StatusCode, context.Request.Path);
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapGet("/health/live", () => Results.Ok(new { status = "live" }))
    .ExcludeFromDescription();

app.MapRental();

app.Logger.LogInformation("{Event} environment={Environment}",
    AppEvents.Startup, app.Environment.EnvironmentName);

app.Run();

public partial class Program;
