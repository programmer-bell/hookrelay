using HookRelay.Domain;

namespace HookRelay.Features.Inspector;

public static class StatusFilter
{
    public const string All = "all";

    private static readonly HashSet<string> Valid = new(StringComparer.Ordinal)
    {
        All,
        DeliveryStatus.Pending,
        DeliveryStatus.Succeeded,
        DeliveryStatus.Dead,
    };

    public static string Normalize(string? value) =>
        value is not null && Valid.Contains(value) ? value : All;
}
