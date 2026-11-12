using System.Globalization;
using Qf.BuildingBlocks.Domain;
using Qf.ClientRegistry.Application.Commands;
using Qf.ClientRegistry.Domain;

namespace Qf.ClientRegistry.AntiCorruption;

/// <summary>Client as returned by the Credible REST API (illustrative).</summary>
public sealed record CredibleClientDto(
    int ClientId,
    string? FirstName,
    string? LastName,
    string? MiddleName,
    DateTime? Dob,
    string? ChartNumber,
    bool? IsActive);

public static class CredibleTranslator
{
    public static Result<LegacyClientSnapshot> Translate(CredibleClientDto dto, string sourceInstance)
    {
        ArgumentNullException.ThrowIfNull(dto);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceInstance);

        if (dto.Dob is null)
        {
            return Error.Validation("Credible.Dob.Missing", $"Client {dto.ClientId}: date of birth is missing.");
        }

        return new LegacyClientSnapshot(
            LegacySystem.Credible,
            LegacyText.ExternalId(sourceInstance, dto.ClientId.ToString(CultureInfo.InvariantCulture)),
            LegacyText.NullIfBlank(dto.ChartNumber)?.ToUpperInvariant(),
            LegacyText.NormalizeName(dto.FirstName),
            LegacyText.NormalizeName(dto.LastName),
            LegacyText.NullIfBlank(LegacyText.NormalizeName(dto.MiddleName)),
            DateOnly.FromDateTime(dto.Dob.Value),
            dto.IsActive ?? true);
    }
}
