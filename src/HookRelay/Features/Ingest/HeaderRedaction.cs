using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace HookRelay.Features.Ingest;

public static class HeaderRedaction
{
    private const string Redacted = "[redacted]";

    private static readonly HashSet<string> RedactedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Cookie",
    };

    public static string ToJson(IHeaderDictionary headers)
    {
        var result = new Dictionary<string, string>();
        foreach (var header in headers)
        {
            var isSensitive = RedactedNames.Contains(header.Key);
            var value = isSensitive ? Redacted : string.Join(", ", header.Value.ToArray().Where(v => v is not null));
            result[header.Key] = value;
        }

        return JsonSerializer.Serialize(result);
    }
}
