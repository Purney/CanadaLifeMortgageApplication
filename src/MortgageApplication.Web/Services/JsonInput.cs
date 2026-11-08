using System.Text.Json;

namespace MortgageApplication.Web.Services;

public static class JsonInput
{
    public static JsonElement FromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return JsonSerializer.SerializeToElement<string?>(null);
        }

        try
        {
            using var document = JsonDocument.Parse(text);

            // Cloning retains the original number token, including precision and exponent.
            if (document.RootElement.ValueKind == JsonValueKind.Number)
            {
                return document.RootElement.Clone();
            }
        }
        catch (JsonException)
        {
            // Invalid numeric text is deliberately sent to the API as a string.
        }

        // Preserve malformed numeric text as a string so the API can reject its type.
        return JsonSerializer.SerializeToElement(text);
    }
}
