// Тип InstrumentModelDto після несумісної зміни: обов'язкове поле відповіді
// TotalCount перейменовано на Total, поле Manufacturer із сумісної зміни лишилося.

/// <summary>Модель приладу в каталозі.</summary>
/// <param name="Id">Стабільний ідентифікатор моделі.</param>
/// <param name="Title">Назва моделі для людини.</param>
/// <param name="Total">Скільки екземплярів є всього.</param>
/// <param name="Manufacturer">Виробник, необов'язкове поле.</param>
public sealed record InstrumentModelDto(
    string Id, string Title, int Total, string? Manufacturer = null);
