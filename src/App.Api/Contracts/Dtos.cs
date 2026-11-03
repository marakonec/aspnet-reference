using System.ComponentModel.DataAnnotations;

namespace App.Api.Contracts;

/// <summary>Модель приладу в каталозі.</summary>
/// <param name="Id">Стабільний ідентифікатор моделі.</param>
/// <param name="Title">Назва моделі для людини.</param>
/// <param name="TotalCount">Скільки екземплярів є всього.</param>
public sealed record InstrumentModelDto(string Id, string Title, int TotalCount);

/// <summary>Скільки екземплярів моделі вільні зараз.</summary>
/// <param name="ModelId">Ідентифікатор моделі.</param>
/// <param name="AvailableCount">Кількість вільних екземплярів.</param>
public sealed record AvailabilityDto(string ModelId, int AvailableCount);

/// <summary>Позиція бронювання.</summary>
/// <param name="ModelId">Ідентифікатор моделі приладу.</param>
/// <param name="Count">Кількість екземплярів, додатне число.</param>
public sealed record ReservationItemDto(
    [property: Required(ErrorMessage = "Потрібен ідентифікатор моделі")]
    string ModelId,
    [property: Range(1, 10, ErrorMessage = "Кількість від 1 до 10")]
    int Count);

/// <summary>Бронювання.</summary>
/// <param name="Id">Ідентифікатор бронювання.</param>
/// <param name="CustomerId">Ідентифікатор замовника.</param>
/// <param name="Status">Стан бронювання.</param>
/// <param name="Notes">Довільна примітка.</param>
/// <param name="Items">Позиції бронювання.</param>
public sealed record ReservationDto(
    string Id,
    string CustomerId,
    string Status,
    string? Notes,
    IReadOnlyList<ReservationItemDto> Items);

/// <summary>Запит на створення бронювання.</summary>
/// <param name="CustomerId">Ідентифікатор замовника. Обов'язковий для оператора, замовник може його не вказувати.</param>
/// <param name="Items">Щонайменше одна позиція.</param>
public sealed record CreateReservationRequest(
    [property: MinLength(1, ErrorMessage = "Ідентифікатор замовника не може бути порожнім")]
    string? CustomerId,
    [property: Required]
    [property: MinLength(1, ErrorMessage = "Потрібна щонайменше одна позиція")]
    List<ReservationItemDto> Items);

/// <summary>Результат видачі.</summary>
/// <param name="ReservationId">Бронювання, за яким видано.</param>
/// <param name="Status">Новий стан бронювання.</param>
/// <param name="IssueRecordId">Створений запис видачі.</param>
public sealed record IssueResultDto(string ReservationId, string Status, string IssueRecordId);

/// <summary>Нова назва моделі в каталозі.</summary>
public sealed record RenameModelRequest(
    [property: Required(ErrorMessage = "Потрібна назва моделі")]
    [property: StringLength(80, MinimumLength = 2, ErrorMessage = "Назва від 2 до 80 символів")]
    string Title);

/// <summary>Запит на встановлення примітки. Ідемпотентна операція.</summary>
/// <param name="Notes">Текст примітки. Порожній рядок очищає її.</param>
public sealed record SetNotesRequest(
    [property: MaxLength(200, ErrorMessage = "Не більше 200 символів")]
    string Notes);
