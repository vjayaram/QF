namespace Qf.ClientRegistry.Domain;

public enum MatchConfidence
{
    None = 0,
    Probable = 1,
    Certain = 2,
}

/// <summary>
/// Deterministic matching used when consolidating clients from acquired EHRs.
/// Deliberately conservative: only "Certain" matches are linked automatically; "Probable"
/// matches go to a human review queue, because a wrong merge in a clinical record is a patient-safety event.
/// </summary>
public static class ClientMatchingPolicy
{
    public static MatchConfidence Evaluate(
        Client candidate,
        string? mrn,
        string given,
        string family,
        DateOnly dateOfBirth)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (candidate.Status == ClientStatus.Merged || candidate.DateOfBirth != dateOfBirth)
        {
            return MatchConfidence.None;
        }

        var sameFamily = Normalize(candidate.Name.Family) == Normalize(family);
        var sameGiven = Normalize(candidate.Name.Given) == Normalize(given);
        var sameMrn = !string.IsNullOrWhiteSpace(mrn)
            && string.Equals(candidate.Mrn.Value, mrn.Trim(), StringComparison.OrdinalIgnoreCase);

        if (sameMrn && sameFamily && sameGiven)
        {
            return MatchConfidence.Certain;
        }

        var sameGivenInitial = Normalize(candidate.Name.Given).FirstOrDefault() == Normalize(given).FirstOrDefault();
        return sameFamily && (sameGiven || sameGivenInitial || sameMrn)
            ? MatchConfidence.Probable
            : MatchConfidence.None;
    }

    /// <summary>Upper-case letters only, so "O'Brien", "OBRIEN" and "o brien" compare equal.</summary>
    internal static string Normalize(string value) =>
        new([.. (value ?? string.Empty).Where(char.IsLetter).Select(char.ToUpperInvariant)]);
}
