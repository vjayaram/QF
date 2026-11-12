using System.Globalization;
using Qf.BuildingBlocks.Domain;
using Qf.ClientRegistry.Application.Commands;
using Qf.ClientRegistry.Domain;

namespace Qf.ClientRegistry.AntiCorruption;

/// <summary>
/// Translates the PID segment of an HL7 v2 ADT message from InSync (illustrative feed).
///   PID-3  patient identifier list   id^^^authority^type~...   (type MR = MRN, PI = internal id)
///   PID-5  patient name              family^given^middle
///   PID-7  date of birth             yyyyMMdd
/// ADT^A03 (discharge) maps to an inactive client.
/// </summary>
public static class InSyncHl7Translator
{
    public static Result<LegacyClientSnapshot> Translate(string pidSegment, string messageType, string sourceInstance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceInstance);
        if (string.IsNullOrWhiteSpace(pidSegment) || !pidSegment.StartsWith("PID|", StringComparison.Ordinal))
        {
            return Error.Validation("InSync.Pid.Missing", "Message has no PID segment.");
        }

        var fields = pidSegment.Split('|');
        string Field(int n) => n < fields.Length ? fields[n] : string.Empty;

        string? internalId = null;
        string? mrn = null;
        foreach (var repetition in Field(3).Split('~', StringSplitOptions.RemoveEmptyEntries))
        {
            var components = repetition.Split('^');
            var id = components.ElementAtOrDefault(0);
            var type = components.ElementAtOrDefault(4);
            if (type == "PI")
            {
                internalId = id;
            }
            else if (type == "MR")
            {
                mrn = id;
            }
        }

        if (string.IsNullOrWhiteSpace(internalId))
        {
            return Error.Validation("InSync.Pid3.NoInternalId", "PID-3 has no PI identifier.");
        }

        var name = Field(5).Split('^');
        if (!DateOnly.TryParseExact(Field(7), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dob))
        {
            return Error.Validation("InSync.Pid7.Unparseable", "PID-7 is not yyyyMMdd.");
        }

        return new LegacyClientSnapshot(
            LegacySystem.InSync,
            LegacyText.ExternalId(sourceInstance, internalId),
            LegacyText.NullIfBlank(mrn)?.ToUpperInvariant(),
            LegacyText.NormalizeName(name.ElementAtOrDefault(1)),
            LegacyText.NormalizeName(name.ElementAtOrDefault(0)),
            LegacyText.NullIfBlank(LegacyText.NormalizeName(name.ElementAtOrDefault(2))),
            dob,
            IsActive: !string.Equals(messageType, "ADT^A03", StringComparison.Ordinal));
    }
}
