using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.OutputCaching;
using App.Api.Contracts;
using App.Api.Domain;
using App.Api.LoadProtection;
using App.Api.Observability;
using App.Api.Notifications;
using App.Api.Security;
using App.Shared;

namespace App.Api.Endpoints;

public static class RentalEndpoints
{
    public static void MapRental(this WebApplication app)
    {
        app.MapGet("/models", async Task<Ok<IEnumerable<InstrumentModelDto>>> (IRentalStore store) =>
                TypedResults.Ok((await store.GetModelsAsync())
                    .Select(m => new InstrumentModelDto(m.Id, m.Title, m.TotalCount))))
            .WithName("ListModels")
            .WithSummary("Каталог моделей приладів")
            .AllowAnonymous()
            .CacheOutput(LoadProtectionSetup.ModelsPolicy);

        app.MapPut("/models/{id}/title",
                async Task<Results<Ok<InstrumentModelDto>, ProblemHttpResult>> (
                    string id,
                    RenameModelRequest request,
                    ClaimsPrincipal user,
                    IRentalStore store,
                    ReservationAccess access,
                    IOutputCacheStore cache,
                    ILogger<Program> log,
                    CancellationToken ct) =>
            {
                if (!access.CheckOperator(user).Allowed)
                    return Forbidden("Змінювати каталог може лише оператор прокату");

                var model = await store.RenameModelAsync(id, request.Title, ct);
                if (model is null)
                    return NotFound($"Немає моделі з ідентифікатором {id}", "Модель не знайдено");

                log.LogInformation("{Event} model={ModelId}", AppEvents.ModelRenamed, id);

                await cache.EvictByTagAsync(LoadProtectionSetup.ModelsTag, ct);
                log.LogInformation("{Event} tag={Tag}",
                    AppEvents.CacheEvicted, LoadProtectionSetup.ModelsTag);

                return TypedResults.Ok(new InstrumentModelDto(model.Id, model.Title, model.TotalCount));
            })
            .WithName("RenameModel")
            .WithSummary("Змінити назву моделі в каталозі (ідемпотентна операція)")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .RequireAuthorization();

        app.MapGet("/models/{id}/availability",
                async Task<Results<Ok<AvailabilityDto>, ProblemHttpResult>> (
                    string id, IRentalStore store) =>
            {
                var model = await store.FindModelAsync(id);
                return model is null
                    ? NotFound($"Немає моделі з ідентифікатором {id}", "Модель не знайдено")
                    : TypedResults.Ok(new AvailabilityDto(model.Id, model.AvailableCount));
            })
            .WithName("GetAvailability")
            .WithSummary("Кількість вільних екземплярів моделі")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .RequireAuthorization();

        app.MapGet("/reservations", async Task<Ok<IEnumerable<ReservationDto>>> (
                    ClaimsPrincipal user, IRentalStore store, ReservationAccess access) =>
                TypedResults.Ok((await store.GetReservationsAsync(access.VisibleOwner(user))).Select(ToDto)))
            .WithName("ListReservations")
            .WithSummary("Перелік бронювань")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .RequireAuthorization();

        app.MapGet("/reservations/{id}",
                async Task<Results<Ok<ReservationDto>, ProblemHttpResult>> (
                    string id,
                    ClaimsPrincipal user,
                    IRentalStore store,
                    ReservationAccess access) =>
            {
                var reservation = await store.FindReservationAsync(id);
                if (reservation is null)
                    return NotFound($"Немає бронювання {id}", "Бронювання не знайдено");

                var decision = await access.CheckAsync(user, reservation);
                return decision.Allowed
                    ? TypedResults.Ok(ToDto(reservation))
                    : Forbidden(decision.Detail);
            })
            .WithName("GetReservation")
            .WithSummary("Одне бронювання")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .RequireAuthorization();

        app.MapPost("/reservations",
                async Task<Results<Created<ReservationDto>, ValidationProblem, ProblemHttpResult>> (
                    CreateReservationRequest request,
                    ClaimsPrincipal user,
                    IRentalStore store,
                    ReservationAccess access) =>
            {
                var decision = access.CheckCreate(user, request.CustomerId, out var owner);
                if (!decision.Allowed) return Forbidden(decision.Detail);
                if (string.IsNullOrEmpty(owner))
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["customerId"] = ["Потрібен ідентифікатор замовника"]
                    });

                var reservation = await store.AddReservationAsync(
                    owner,
                    request.Items.Select(i => new ReservationItem
                    {
                        ReservationId = string.Empty, ModelId = i.ModelId, Count = i.Count
                    }));
                return TypedResults.Created($"/reservations/{reservation.Id}", ToDto(reservation));
            })
            .WithName("CreateReservation")
            .WithSummary("Створити бронювання. Замовник створює його від власного імені, оператор вказує замовника")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .RequireAuthorization();

        app.MapPut("/reservations/{id}/notes",
                async Task<Results<Ok<ReservationDto>, ProblemHttpResult>> (
                    string id,
                    SetNotesRequest request,
                    ClaimsPrincipal user,
                    IRentalStore store,
                    ReservationAccess access) =>
            {
                var reservation = await store.FindReservationAsync(id);
                if (reservation is null)
                    return NotFound($"Немає бронювання {id}", "Бронювання не знайдено");

                var decision = await access.CheckAsync(user, reservation);
                if (!decision.Allowed) return Forbidden(decision.Detail);

                var updated = await store.SetNotesAsync(id, request.Notes);
                return updated is null
                    ? NotFound($"Немає бронювання {id}", "Бронювання не знайдено")
                    : TypedResults.Ok(ToDto(updated));
            })
            .WithName("SetReservationNotes")
            .WithSummary("Встановити примітку до бронювання (ідемпотентна операція)")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .RequireAuthorization();

        app.MapPost("/reservations/{id}/issue",
                async Task<Results<Ok<IssueResultDto>, ProblemHttpResult>> (
                    string id,
                    ClaimsPrincipal user,
                    IRentalStore store,
                    ReservationAccess access,
                    NotifierClient notifier,
                    AppMetrics metrics,
                    ILogger<Program> log,
                    CancellationToken ct) =>
            {
                var issuedBy = user.Identity?.Name ?? user.FindFirst("sub")?.Value ?? "unknown";

                log.LogInformation("{Event} reservation={ReservationId} by={IssuedBy}",
                    AppEvents.IssueRequested, id, issuedBy);

                var role = access.CheckOperator(user);
                if (!role.Allowed)
                {
                    metrics.Issue("Forbidden");
                    return Forbidden(role.Detail);
                }

                var reservation = await store.FindReservationAsync(id, ct);
                var customer = reservation is null ? null : await store.FindCustomerAsync(reservation.CustomerId, ct);

                // Сповіщення надсилається до фіксації транзакції. Якщо залежність не
                // відповіла, видачу відкочено і клієнт отримує 503.
                Func<IssueRecord, CancellationToken, Task>? beforeCommit = reservation is null
                    ? null
                    : (record, token) => notifier.SendAsync(
                        new Notification(record.Id, id, reservation.CustomerId, customer?.Phone), token);

                IssueResult result;
                try
                {
                    result = await store.IssueAsync(id, issuedBy, beforeCommit, ct);
                }
                catch (NotifierUnavailableException ex)
                {
                    metrics.Issue("DependencyFailed");
                    log.LogWarning("{Event} reservation={ReservationId} reason={Reason}",
                        AppEvents.IssueRolledBackByDependency, id, ex.Message);
                    return TypedResults.Problem(
                        statusCode: StatusCodes.Status503ServiceUnavailable,
                        title: "Сервіс сповіщень недоступний",
                        detail: "Видачу не виконано, бо сповіщення є частиною операції, а залежність не відповіла");
                }

                metrics.Issue(result.Outcome.ToString());
                if (result.Outcome is not IssueOutcome.Ok)
                {
                    log.LogWarning("{Event} reservation={ReservationId} reason={Reason}",
                        AppEvents.IssueRejected, id, result.Outcome);

                    return result.Outcome switch
                    {
                        IssueOutcome.NotFound => NotFound(
                            $"Немає бронювання {id}", "Бронювання не знайдено"),
                        IssueOutcome.WrongStatus => Conflict(
                            "Видати можна лише бронювання у стані Reserved",
                            "Неприпустимий стан бронювання"),
                        IssueOutcome.Conflict => Conflict(
                            "Залишок змінено іншим запитом під час цієї операції. Запит можна повторити",
                            "Конкурентна зміна залишку"),
                        _ => Conflict(
                            "Залишок моделі менший за замовлену кількість",
                            "Недостатньо вільних приладів")
                    };
                }

                log.LogInformation("{Event} reservation={ReservationId} record={RecordId}",
                    AppEvents.IssueCompleted, id, result.RecordId);

                return TypedResults.Ok(new IssueResultDto(id, "Issued", result.RecordId!));
            })
            .WithName("IssueReservation")
            .WithSummary("Видати прилади за бронюванням (ключова операція)")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .RequireAuthorization()
            .RequireRateLimiting(LoadProtectionSetup.IssuePolicy);

    }

    private static ProblemHttpResult NotFound(string detail, string title) =>
        TypedResults.Problem(statusCode: StatusCodes.Status404NotFound,
            title: title, detail: detail);

    private static ProblemHttpResult Conflict(string detail, string title) =>
        TypedResults.Problem(statusCode: StatusCodes.Status409Conflict,
            title: title, detail: detail);

    private static ProblemHttpResult Forbidden(string detail) =>
        TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden,
            title: "Доступ заборонено", detail: detail);

    private static ReservationDto ToDto(Reservation r) => new(
        r.Id, r.CustomerId, r.Status.ToString(), r.Notes,
        [.. r.Items.Select(i => new ReservationItemDto(i.ModelId, i.Count))]);
}
