namespace App.Shared;

public sealed class FeatureSwitches
{
    public const string SectionName = "Features";

    // memory або postgres. Набір тестів у пам'яті і крок конвеєра, що будує документ
    // контракту, працюють без бази.
    public string Persistence { get; set; } = "postgres";

    public bool UsesDatabase =>
        string.Equals(Persistence, "postgres", StringComparison.OrdinalIgnoreCase);
}
