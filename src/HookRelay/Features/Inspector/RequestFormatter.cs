using System.Text.Json;

namespace HookRelay.Features.Inspector;

public readonly record struct HeaderEntry(string Name, string Value);

public static class RequestFormatter
{
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    public static string PrettyPrintBody(string body)
    {
        if (body.Length == 0)
        {
            return body;
        }

        var first = body[0];
        if (first is not ('{' or '['))
        {
            return body;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return JsonSerializer.Serialize(document.RootElement, IndentedJson);
        }
        catch (JsonException)
        {
            return body;
        }
    }

    public static IReadOnlyList<HeaderEntry> ParseHeaders(string headersJson)
    {
        if (string.IsNullOrWhiteSpace(headersJson))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(headersJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            var entries = new List<HeaderEntry>(document.RootElement.EnumerateObject().Count());
            foreach (var property in document.RootElement.EnumerateObject())
            {
                entries.Add(new HeaderEntry(property.Name, property.Value.GetString() ?? string.Empty));
            }

            return entries;
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
