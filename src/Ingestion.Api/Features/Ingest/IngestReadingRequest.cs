using System.Globalization;
using System.Text.RegularExpressions;
using Ingestion.Domain;

namespace Ingestion.Api.Features.Ingest;

/// <summary>
/// The request body as sent. Both fields are nullable so a missing field is visible (AC-6), and
/// <see cref="ObservedAt"/> is text so a timestamp without an offset is visible (AC-7): the JSON
/// serializer would otherwise read it in the server's local time zone.
/// </summary>
public sealed partial record IngestReadingRequest(double? Value, string? ObservedAt)
{
    /// <summary>Parses the body into a <see cref="Reading"/>, or names every field that is wrong.</summary>
    public Result<Reading, Dictionary<string, string[]>> Validate()
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (Value is null)
            errors["value"] = ["Required."];

        var observedAt = default(DateTimeOffset);
        if (ObservedAt is null)
            errors["observedAt"] = ["Required."];
        else if (!TryParseInstant(ObservedAt, out observedAt))
            errors["observedAt"] = ["Must be an ISO 8601 timestamp with 'Z' or a UTC offset, for example 2026-09-27T14:00:00Z."];

        return errors.Count == 0 ? new Reading(Value!.Value, observedAt) : errors;
    }

    private static bool TryParseInstant(string text, out DateTimeOffset instant)
    {
        instant = default;
        return ExplicitOffsetTimestamp().IsMatch(text)
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out instant);
    }

    // RFC 3339 date-time: the offset ('Z' or ±hh:mm) is mandatory. [0-9], not \d, which admits any Unicode digit.
    [GeneratedRegex(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]{1,7})?(Z|[+-][0-9]{2}:[0-9]{2})$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ExplicitOffsetTimestamp();
}

public sealed record IngestReadingResponse(Classification Classification);
