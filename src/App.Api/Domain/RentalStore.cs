namespace App.Api.Domain;

public sealed class RentalStore
{
    private readonly Lock _gate = new();

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
                Items = [new() { ModelId = "mdl-osc", Count = 1 }] },
        new() { Id = "res-2", CustomerId = "cus-2",
                Items = [new() { ModelId = "mdl-gen", Count = 1 }] }
    ];

    public List<IssueRecord> IssueRecords { get; } = [];

    public IssueOutcome Issue(string reservationId, string issuedBy)
    {
        lock (_gate)
        {
            var reservation = Reservations.FirstOrDefault(r => r.Id == reservationId);
            if (reservation is null)
                return IssueOutcome.NotFound;

            if (reservation.Status != ReservationStatus.Reserved)
                return IssueOutcome.WrongStatus;

            foreach (var item in reservation.Items)
            {
                var model = Models.First(m => m.Id == item.ModelId);
                if (model.AvailableCount < item.Count)
                    return IssueOutcome.NotEnough;
            }

            foreach (var item in reservation.Items)
                Models.First(m => m.Id == item.ModelId).AvailableCount -= item.Count;

            reservation.Status = ReservationStatus.Issued;

            IssueRecords.Add(new IssueRecord
            {
                Id = $"iss-{IssueRecords.Count + 1}",
                ReservationId = reservation.Id,
                IssuedAt = DateTimeOffset.UtcNow,
                IssuedBy = issuedBy
            });

            return IssueOutcome.Ok;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            foreach (var m in Models) m.AvailableCount = m.TotalCount;
            foreach (var r in Reservations) { r.Status = ReservationStatus.Reserved; r.Notes = null; }
            IssueRecords.Clear();
        }
    }
}

public enum IssueOutcome { Ok, NotFound, WrongStatus, NotEnough }
