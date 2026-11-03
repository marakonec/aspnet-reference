namespace App.Api.Domain;

public interface IRentalStore
{
    Task<IReadOnlyList<InstrumentModel>> GetModelsAsync(CancellationToken ct = default);

    Task<InstrumentModel?> FindModelAsync(string id, CancellationToken ct = default);

    Task<InstrumentModel?> RenameModelAsync(string id, string title, CancellationToken ct = default);

    Task<IReadOnlyList<Reservation>> GetReservationsAsync(string? ownerId = null, CancellationToken ct = default);

    Task<Reservation?> FindReservationAsync(string id, CancellationToken ct = default);

    Task<Customer?> FindCustomerAsync(string id, CancellationToken ct = default);

    Task<Reservation> AddReservationAsync(
        string customerId, IEnumerable<ReservationItem> items, CancellationToken ct = default);

    Task<Reservation?> SetNotesAsync(string id, string? notes, CancellationToken ct = default);

    Task<IssueResult> IssueAsync(
        string reservationId,
        string issuedBy,
        Func<IssueRecord, CancellationToken, Task>? beforeCommit = null,
        CancellationToken ct = default);

    string Kind { get; }
}

public readonly record struct IssueResult(IssueOutcome Outcome, string? RecordId);

public enum IssueOutcome { Ok, NotFound, WrongStatus, NotEnough, Conflict }
