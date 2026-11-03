
namespace App.Api.Domain;

public sealed class RentalStore : IRentalStore
{
    private readonly Lock _gate = new();

    public string Kind => "memory";

    public List<InstrumentModel> Models { get; } =
    [
        new() { Id = "mdl-osc", Title = "Осцилограф",           TotalCount = 3, AvailableCount = 3 },
        new() { Id = "mdl-mm",  Title = "Мультиметр",           TotalCount = 5, AvailableCount = 5 },
        new() { Id = "mdl-gen", Title = "Генератор сигналів",   TotalCount = 1, AvailableCount = 1 },
        new() { Id = "mdl-lcr", Title = "Вимірювач RLC",        TotalCount = 2, AvailableCount = 2 }
    ];

    public List<Instrument> Instruments { get; } =
    [
        new() { Id = "ins-1", ModelId = "mdl-osc", SerialNumber = "OSC-0001" },
        new() { Id = "ins-2", ModelId = "mdl-osc", SerialNumber = "OSC-0002" },
        new() { Id = "ins-3", ModelId = "mdl-osc", SerialNumber = "OSC-0003" },
        new() { Id = "ins-4", ModelId = "mdl-mm",  SerialNumber = "MM-0001"  },
        new() { Id = "ins-5", ModelId = "mdl-gen", SerialNumber = "GEN-0001" }
    ];

    public List<Customer> Customers { get; } =
    [
        new() { Id = "cus-1", Name = "Лабораторія №1", Phone = "+380671112233" },
        new() { Id = "cus-2", Name = "Лабораторія №2", Phone = "+380671114455" }
    ];

    public List<Reservation> Reservations { get; } =
    [
        new() { Id = "res-1", CustomerId = "cus-1",
                Items = [new() { Id = 1, ReservationId = "res-1", ModelId = "mdl-osc", Count = 1 }] },
        new() { Id = "res-2", CustomerId = "cus-2",
                Items = [new() { Id = 2, ReservationId = "res-2", ModelId = "mdl-gen", Count = 1 }] }
    ];

    public List<IssueRecord> IssueRecords { get; } = [];

    public Task<IReadOnlyList<InstrumentModel>> GetModelsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<InstrumentModel>>(Models);

    public Task<InstrumentModel?> FindModelAsync(string id, CancellationToken ct = default) =>
        Task.FromResult(Models.FirstOrDefault(m => m.Id == id));

    public Task<InstrumentModel?> RenameModelAsync(string id, string title, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var model = Models.FirstOrDefault(m => m.Id == id);
            if (model is not null) model.Title = title;
            return Task.FromResult(model);
        }
    }

    public Task<IReadOnlyList<Reservation>> GetReservationsAsync(string? ownerId = null, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Reservation>>(
            [.. Reservations.Where(r => ownerId is null || r.CustomerId == ownerId)]);

    public Task<Reservation?> FindReservationAsync(string id, CancellationToken ct = default) =>
        Task.FromResult(Reservations.FirstOrDefault(r => r.Id == id));

    public Task<Customer?> FindCustomerAsync(string id, CancellationToken ct = default) =>
        Task.FromResult(Customers.FirstOrDefault(c => c.Id == id));

    public Task<Reservation> AddReservationAsync(
        string customerId, IEnumerable<ReservationItem> items, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var id = $"res-{Reservations.Count + 1}";
            var reservation = new Reservation
            {
                Id = id,
                CustomerId = customerId,
                Items = [.. items.Select(i => new ReservationItem
                {
                    ReservationId = id, ModelId = i.ModelId, Count = i.Count
                })]
            };
            Reservations.Add(reservation);
            return Task.FromResult(reservation);
        }
    }

    public Task<Reservation?> SetNotesAsync(string id, string? notes, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var reservation = Reservations.FirstOrDefault(r => r.Id == id);
            if (reservation is not null) reservation.Notes = notes;
            return Task.FromResult(reservation);
        }
    }

    public async Task<IssueResult> IssueAsync(
        string reservationId,
        string issuedBy,
        Func<IssueRecord, CancellationToken, Task>? beforeCommit = null,
        CancellationToken ct = default)
    {
        IssueRecord record;
        lock (_gate)
        {
            var reservation = Reservations.FirstOrDefault(r => r.Id == reservationId);
            if (reservation is null)
                return new IssueResult(IssueOutcome.NotFound, null);

            if (reservation.Status != ReservationStatus.Reserved)
                return new IssueResult(IssueOutcome.WrongStatus, null);

            foreach (var item in reservation.Items)
            {
                var model = Models.First(m => m.Id == item.ModelId);
                if (model.AvailableCount < item.Count)
                    return new IssueResult(IssueOutcome.NotEnough, null);
            }

            foreach (var item in reservation.Items)
            {
                var model = Models.First(m => m.Id == item.ModelId);
                model.AvailableCount -= item.Count;
                model.Version++;
            }

            reservation.Status = ReservationStatus.Issued;

            record = new IssueRecord
            {
                Id = IssueRecordIds.Next(IssueRecords.Count),
                ReservationId = reservation.Id,
                IssuedAt = DateTimeOffset.UtcNow,
                IssuedBy = issuedBy
            };
            IssueRecords.Add(record);
        }

        if (beforeCommit is not null)
            await beforeCommit(record, ct);

        return new IssueResult(IssueOutcome.Ok, record.Id);
    }

}
