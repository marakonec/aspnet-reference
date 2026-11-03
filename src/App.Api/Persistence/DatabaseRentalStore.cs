using Microsoft.EntityFrameworkCore;
using Npgsql;
using App.Api.Domain;
using App.Shared;

namespace App.Api.Persistence;

public sealed class DatabaseRentalStore(
    AppDbContext db,
    ILogger<DatabaseRentalStore> log) : IRentalStore
{
    public string Kind => "postgres";

    public async Task<IReadOnlyList<InstrumentModel>> GetModelsAsync(CancellationToken ct = default) =>
        await db.Models.AsNoTracking().OrderBy(m => m.Id).ToListAsync(ct);

    public async Task<InstrumentModel?> FindModelAsync(string id, CancellationToken ct = default) =>
        await db.Models.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);

    public async Task<InstrumentModel?> RenameModelAsync(
        string id, string title, CancellationToken ct = default)
    {
        var model = await db.Models.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (model is null) return null;

        model.Title = title;
        await db.SaveChangesAsync(ct);
        return model;
    }

    public async Task<IReadOnlyList<Reservation>> GetReservationsAsync(string? ownerId = null, CancellationToken ct = default)
    {
        var reservations = db.Reservations.AsNoTracking()
            .Where(r => ownerId == null || r.CustomerId == ownerId);

        return await reservations
            .Include(r => r.Items)
            .OrderBy(r => r.Id)
            .ToListAsync(ct);
    }

    public async Task<Reservation?> FindReservationAsync(string id, CancellationToken ct = default) =>
        await db.Reservations.AsNoTracking()
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<Customer?> FindCustomerAsync(string id, CancellationToken ct = default) =>
        await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<Reservation> AddReservationAsync(
        string customerId, IEnumerable<ReservationItem> items, CancellationToken ct = default)
    {
        var count = await db.Reservations.CountAsync(ct);
        var id = $"res-{count + 1}";

        var reservation = new Reservation
        {
            Id = id,
            CustomerId = customerId,
            Items = [.. items.Select(i => new ReservationItem
            {
                ReservationId = id, ModelId = i.ModelId, Count = i.Count
            })]
        };

        db.Reservations.Add(reservation);
        await db.SaveChangesAsync(ct);
        return reservation;
    }

    public async Task<Reservation?> SetNotesAsync(
        string id, string? notes, CancellationToken ct = default)
    {
        var reservation = await db.Reservations.Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
        if (reservation is null) return null;

        reservation.Notes = notes;
        await db.SaveChangesAsync(ct);
        return reservation;
    }

    public async Task<IssueResult> IssueAsync(
        string reservationId,
        string issuedBy,
        Func<IssueRecord, CancellationToken, Task>? beforeCommit = null,
        CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var reservation = await db.Reservations
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == reservationId, ct);

        if (reservation is null) return new IssueResult(IssueOutcome.NotFound, null);
        if (reservation.Status != ReservationStatus.Reserved)
            return new IssueResult(IssueOutcome.WrongStatus, null);

        var modelIds = reservation.Items.Select(i => i.ModelId).ToList();
        var models = await db.Models.Where(m => modelIds.Contains(m.Id)).ToListAsync(ct);

        foreach (var item in reservation.Items)
        {
            var model = models.First(m => m.Id == item.ModelId);
            if (model.AvailableCount < item.Count)
                return new IssueResult(IssueOutcome.NotEnough, null);
        }

        foreach (var item in reservation.Items)
        {
            var model = models.First(m => m.Id == item.ModelId);
            model.AvailableCount -= item.Count;
            model.Version++;
        }

        reservation.Status = ReservationStatus.Issued;

        try
        {
            await db.SaveChangesAsync(ct);

            var record = await AddIssueRecordAsync(reservation.Id, issuedBy, ct);

            if (beforeCommit is not null)
                await beforeCommit(record, ct);

            await tx.CommitAsync(ct);
            return new IssueResult(IssueOutcome.Ok, record.Id);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            log.LogWarning("{Event} reservation={ReservationId} entries={Entries}",
                AppEvents.ConcurrencyConflict, reservationId, ex.Entries.Count);
            await tx.RollbackAsync(ct);
            return new IssueResult(IssueOutcome.Conflict, null);
        }
    }

    private async Task<IssueRecord> AddIssueRecordAsync(
        string reservationId, string issuedBy, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var issued = await db.IssueRecords.CountAsync(ct);
            var record = new IssueRecord
            {
                Id = IssueRecordIds.Next(issued),
                ReservationId = reservationId,
                IssuedAt = DateTimeOffset.UtcNow,
                IssuedBy = issuedBy
            };
            db.IssueRecords.Add(record);

            try
            {
                await db.SaveChangesAsync(ct);
                return record;
            }
            catch (DbUpdateException ex) when (attempt < MaxRecordNumberAttempts && IsRecordNumberTaken(ex))
            {
                db.Entry(record).State = EntityState.Detached;
                log.LogInformation("{Event} reservation={ReservationId} record={RecordId} attempt={Attempt}",
                    AppEvents.IssueRecordNumberTaken, reservationId, record.Id, attempt);
            }
        }
    }

    private const int MaxRecordNumberAttempts = 5;

    private static bool IsRecordNumberTaken(DbUpdateException ex) =>
        ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "PK_issue_records"
        };
}
