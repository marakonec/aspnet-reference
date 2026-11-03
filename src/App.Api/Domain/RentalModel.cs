namespace App.Api.Domain;

public enum ReservationStatus
{
    Draft,
    Reserved,
    Issued,
    Returned,
    Cancelled
}

public sealed class InstrumentModel
{
    public required string Id { get; init; }
    public required string Title { get; set; }

    public required int TotalCount { get; init; }

    public int AvailableCount { get; set; }

    public int Version { get; set; }
}

public sealed class Instrument
{
    public required string Id { get; init; }
    public required string ModelId { get; init; }
    public required string SerialNumber { get; init; }
}

public sealed class Customer
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    public required string Phone { get; init; }
}

public sealed class ReservationItem
{
    public int Id { get; set; }

    public required string ReservationId { get; set; }
    public required string ModelId { get; init; }
    public required int Count { get; init; }
}

public sealed class Reservation
{
    public required string Id { get; init; }
    public required string CustomerId { get; init; }
    public ReservationStatus Status { get; set; } = ReservationStatus.Reserved;
    public string? Notes { get; set; }
    public List<ReservationItem> Items { get; init; } = [];
}

public sealed class IssueRecord
{
    public required string Id { get; init; }
    public required string ReservationId { get; init; }
    public required DateTimeOffset IssuedAt { get; init; }
    public required string IssuedBy { get; init; }
}
