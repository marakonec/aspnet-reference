using Microsoft.AspNetCore.Http.HttpResults;
using App.Api.Contracts;
using App.Api.Domain;
using App.Shared;

namespace App.Api.Endpoints;

public static class RentalEndpoints
{
    public static void MapRental(this WebApplication app)
    {
        app.MapGet("/models", Ok<IEnumerable<InstrumentModelDto>> (RentalStore store) =>
                TypedResults.Ok(store.Models
                    .Select(m => new InstrumentModelDto(m.Id, m.Title, m.TotalCount))))
            .WithName("ListModels")
            .WithSummary("Каталог моделей приладів");

        app.MapGet("/models/{id}/availability",
                Results<Ok<AvailabilityDto>, ProblemHttpResult> (string id, RentalStore store) =>
            {
                var model = store.Models.FirstOrDefault(m => m.Id == id);
                return model is null
                    ? NotFound($"Немає моделі з ідентифікатором {id}", "Модель не знайдено")
                    : TypedResults.Ok(new AvailabilityDto(model.Id, model.AvailableCount));
            })
            .WithName("GetAvailability")
            .WithSummary("Кількість вільних екземплярів моделі")
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/reservations", Ok<IEnumerable<ReservationDto>> (RentalStore store) =>
                TypedResults.Ok(store.Reservations.Select(ToDto)))
            .WithName("ListReservations")
            .WithSummary("Перелік бронювань");

        app.MapGet("/reservations/{id}",
                Results<Ok<ReservationDto>, ProblemHttpResult> (string id, RentalStore store) =>
            {
                var reservation = store.Reservations.FirstOrDefault(r => r.Id == id);
                return reservation is null
                    ? NotFound($"Немає бронювання {id}", "Бронювання не знайдено")
                    : TypedResults.Ok(ToDto(reservation));
            })
            .WithName("GetReservation")
            .WithSummary("Одне бронювання")
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapPost("/reservations",
                Created<ReservationDto> (CreateReservationRequest request, RentalStore store) =>
            {
                var reservation = new Reservation
                {
                    Id = $"res-{store.Reservations.Count + 1}",
                    CustomerId = request.CustomerId,
                    Items = [.. request.Items.Select(i =>
                        new ReservationItem { ModelId = i.ModelId, Count = i.Count })]
                };
                store.Reservations.Add(reservation);
                return TypedResults.Created($"/reservations/{reservation.Id}", ToDto(reservation));
            })
            .WithName("CreateReservation")
            .WithSummary("Створити бронювання")
            .ProducesValidationProblem();

        app.MapPut("/reservations/{id}/notes",
                Results<Ok<ReservationDto>, ProblemHttpResult> (
                    string id, SetNotesRequest request, RentalStore store) =>
            {
                var reservation = store.Reservations.FirstOrDefault(r => r.Id == id);
                if (reservation is null)
                    return NotFound($"Немає бронювання {id}", "Бронювання не знайдено");
                reservation.Notes = request.Notes;
                return TypedResults.Ok(ToDto(reservation));
            })
            .WithName("SetReservationNotes")
            .WithSummary("Встановити примітку до бронювання (ідемпотентна операція)")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapPost("/reservations/{id}/issue",
                Results<Ok<IssueResultDto>, ProblemHttpResult> (
                    string id,
                    IssueRequest request,
                    RentalStore store,
                    ILogger<Program> log) =>
            {
                log.LogInformation("{Event} reservation={ReservationId} by={IssuedBy}",
                    AppEvents.IssueRequested, id, request.IssuedBy);

                var outcome = store.Issue(id, request.IssuedBy);
                if (outcome is not IssueOutcome.Ok)
                {
                    log.LogWarning("{Event} reservation={ReservationId} reason={Reason}",
                        AppEvents.IssueRejected, id, outcome);

                    return outcome switch
                    {
                        IssueOutcome.NotFound => NotFound(
                            $"Немає бронювання {id}", "Бронювання не знайдено"),
                        IssueOutcome.WrongStatus => Conflict(
                            "Видати можна лише бронювання у стані Reserved",
                            "Неприпустимий стан бронювання"),
                        _ => Conflict(
                            "Залишок моделі менший за замовлену кількість",
                            "Недостатньо вільних приладів")
                    };
                }

                var record = store.IssueRecords[^1];
                log.LogInformation("{Event} reservation={ReservationId} record={RecordId}",
                    AppEvents.IssueCompleted, id, record.Id);
                return TypedResults.Ok(new IssueResultDto(id, "Issued", record.Id));
            })
            .WithName("IssueReservation")
            .WithSummary("Видати прилади за бронюванням (ключова операція)")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    }

    private static ProblemHttpResult NotFound(string detail, string title) =>
        TypedResults.Problem(statusCode: StatusCodes.Status404NotFound,
            title: title, detail: detail);

    private static ProblemHttpResult Conflict(string detail, string title) =>
        TypedResults.Problem(statusCode: StatusCodes.Status409Conflict,
            title: title, detail: detail);

    private static ReservationDto ToDto(Reservation r) => new(
        r.Id, r.CustomerId, r.Status.ToString(), r.Notes,
        [.. r.Items.Select(i => new ReservationItemDto(i.ModelId, i.Count))]);
}
