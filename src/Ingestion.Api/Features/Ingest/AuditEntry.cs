using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Ingestion.Api.Features.Ingest;

/// <summary>
/// Who changed what, when, under which trace (ADR 0008, AC-13). IDs only: no names, no reading
/// values. The store writes it in the same transaction as the reading it describes.
/// </summary>
public sealed record AuditEntry(
    string Action,
    string SubjectId,
    string? ClientAppId,
    string? TenantId,
    string SensorId,
    DateTimeOffset ObservedAt,
    DateTimeOffset RecordedAt,
    string CorrelationId)
{
    public const string ReadingAccepted = "reading.accepted";

    /// <summary>
    /// Derived from the action and the reading's key, which is accepted at most once, so every
    /// redelivered copy of this record carries the same ID and readers drop duplicates by it.
    /// </summary>
    public Guid Id
    {
        get
        {
            var key = string.Create(CultureInfo.InvariantCulture, $"{Action}\n{SensorId}\n{ObservedAt.UtcTicks}");
            return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(key)).AsSpan(0, 16));
        }
    }

    /// <summary>
    /// The record for accepting a reading. The caller is named by its token's IDs: <c>oid</c> (else
    /// <c>sub</c>) for the user or service principal, <c>azp</c> (else <c>appid</c>) for the client
    /// app, and <c>tid</c> for the tenant.
    /// </summary>
    public static AuditEntry ForAcceptedReading(
        ClaimsPrincipal caller, string sensorId, DateTimeOffset observedAt, DateTimeOffset recordedAt, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(caller);

        // The write policy requires sub, so an authorized caller always has a subject.
        var subject = caller.FindFirst("oid")?.Value ?? caller.FindFirst("sub")?.Value
            ?? throw new InvalidOperationException("An authorized caller has no subject; the write policy requires 'sub'.");

        return new AuditEntry(
            ReadingAccepted,
            subject,
            caller.FindFirst("azp")?.Value ?? caller.FindFirst("appid")?.Value,
            caller.FindFirst("tid")?.Value,
            sensorId,
            observedAt,
            recordedAt,
            correlationId);
    }
}
