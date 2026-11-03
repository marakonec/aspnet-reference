using Microsoft.EntityFrameworkCore;
using App.Api.Domain;
using App.Shared;

namespace App.Api.Persistence;

public static class SeedData
{
    private const int SeedReservations = 25;

    public static readonly IReadOnlyDictionary<string, string> ModelTitles =
        new Dictionary<string, string>
        {
            ["mdl-osc"] = "Осцилограф",
            ["mdl-mm"] = "Мультиметр",
            ["mdl-gen"] = "Генератор сигналів",
            ["mdl-lcr"] = "Вимірювач RLC"
        };

    public static async Task EnsureSeededAsync(
        AppDbContext db, ILogger logger, CancellationToken ct = default)
    {
        if (await db.Models.AnyAsync(ct))
        {
            logger.LogInformation("{Event} seeded=skipped", AppEvents.DatabaseSeeded);
            return;
        }

        db.Models.AddRange(
            new InstrumentModel { Id = "mdl-osc", Title = ModelTitles["mdl-osc"], TotalCount = 3, AvailableCount = 3 },
            new InstrumentModel { Id = "mdl-mm",  Title = ModelTitles["mdl-mm"], TotalCount = 5, AvailableCount = 5 },
            new InstrumentModel { Id = "mdl-gen", Title = ModelTitles["mdl-gen"], TotalCount = 1, AvailableCount = 1 },
            new InstrumentModel { Id = "mdl-lcr", Title = ModelTitles["mdl-lcr"], TotalCount = 2, AvailableCount = 2 });

        db.Instruments.AddRange(
            new Instrument { Id = "ins-1", ModelId = "mdl-osc", SerialNumber = "OSC-0001" },
            new Instrument { Id = "ins-2", ModelId = "mdl-osc", SerialNumber = "OSC-0002" },
            new Instrument { Id = "ins-3", ModelId = "mdl-osc", SerialNumber = "OSC-0003" },
            new Instrument { Id = "ins-4", ModelId = "mdl-mm",  SerialNumber = "MM-0001"  },
            new Instrument { Id = "ins-5", ModelId = "mdl-gen", SerialNumber = "GEN-0001" });

        db.Customers.AddRange(
            new Customer { Id = "cus-1", Name = "Лабораторія №1", Phone = "+380671112233" },
            new Customer { Id = "cus-2", Name = "Лабораторія №2", Phone = "+380671114455" });

        db.Reservations.Add(new Reservation
        {
            Id = "res-1", CustomerId = "cus-1",
            Items = [new ReservationItem { ReservationId = "res-1", ModelId = "mdl-osc", Count = 1 }]
        });
        db.Reservations.Add(new Reservation
        {
            Id = "res-2", CustomerId = "cus-2",
            Items = [new ReservationItem { ReservationId = "res-2", ModelId = "mdl-gen", Count = 1 }]
        });

        var models = new[] { "mdl-osc", "mdl-mm", "mdl-gen", "mdl-lcr" };
        for (var n = 3; n <= SeedReservations; n++)
        {
            var id = $"res-{n}";
            db.Reservations.Add(new Reservation
            {
                Id = id,
                CustomerId = n % 2 == 0 ? "cus-2" : "cus-1",
                Items = [new ReservationItem
                {
                    ReservationId = id, ModelId = models[n % models.Length], Count = 1
                }]
            });
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation("{Event} reservations={Count}",
            AppEvents.DatabaseSeeded, SeedReservations);
    }
}
