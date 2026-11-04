// Тип InstrumentModelDto з src/App.Api/Contracts/Dtos.cs після сумісної зміни:
// нове необов'язкове поле відповіді Manufacturer.

/// <summary>Модель приладу в каталозі.</summary>
/// <param name="Id">Стабільний ідентифікатор моделі.</param>
/// <param name="Title">Назва моделі для людини.</param>
/// <param name="TotalCount">Скільки екземплярів є всього.</param>
/// <param name="Manufacturer">Виробник, необов'язкове поле.</param>
public sealed record InstrumentModelDto(
    string Id, string Title, int TotalCount, string? Manufacturer = null);
