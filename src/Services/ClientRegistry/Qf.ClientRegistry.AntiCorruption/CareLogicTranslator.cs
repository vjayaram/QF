using System.Globalization;
using Qf.BuildingBlocks.Domain;
using Qf.ClientRegistry.Application.Commands;
using Qf.ClientRegistry.Domain;

namespace Qf.ClientRegistry.AntiCorruption;

/// <summary>Row captured by SQL Server change data capture from the CareLogic patient table (illustrative).</summary>
public sealed record CareLogicPatientRow(
    long PatientId,
    string? LastName,
    string? FirstName,
    string? MiddleInitial,
    string? BirthDate,
    string? MedRecNo,
    string? StatusCode);

public static class CareLogicTranslator
{
    public static Result<LegacyClientSnapshot> Translate(CareLogicPatientRow row, string sourceInstance)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceInstance);

        if (!DateOnly.TryParseExact(row.BirthDate?.Trim(), "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dob))
        {
            return Error.Validation("CareLogic.BirthDate.Unparseable", $"Patient {row.PatientId}: birth date is not MM/dd/yyyy.");
        }

        bool active;
        switch (row.StatusCode?.Trim().ToUpperInvariant())
        {
            case "A":
                active = true;
                break;
            case "I" or "D": // D = discharged; still a valid client record, just inactive
                active = false;
                break;
            default:
                return Error.Validation("CareLogic.Status.Unknown", $"Patient {row.PatientId}: unknown status code.");
        }

        return new LegacyClientSnapshot(
            LegacySystem.CareLogic,
            LegacyText.ExternalId(sourceInstance, row.PatientId.ToString(CultureInfo.InvariantCulture)),
            LegacyText.NullIfBlank(row.MedRecNo)?.ToUpperInvariant(),
            LegacyText.NormalizeName(row.FirstName),
            LegacyText.NormalizeName(row.LastName),
            LegacyText.NullIfBlank(LegacyText.NormalizeName(row.MiddleInitial)),
            dob,
            active);
    }
}
