namespace App.Api.Observability;

public static class SensitiveData
{
    // У журнал потрапляють лише перші чотири і дві останні цифри телефону.
    public static string Phone(string? phone)
    {
        if (string.IsNullOrEmpty(phone)) return "-";
        return phone.Length <= 6
            ? new string('*', phone.Length)
            : string.Concat(phone.AsSpan(0, 4), new string('*', phone.Length - 6), phone.AsSpan(phone.Length - 2));
    }
}
