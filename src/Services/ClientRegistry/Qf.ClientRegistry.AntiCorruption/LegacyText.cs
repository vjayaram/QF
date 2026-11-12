using System.Text;

namespace Qf.ClientRegistry.AntiCorruption;

internal static class LegacyText
{
    /// <summary>
    /// Legacy systems store names as "SMITH-JONES" or "o'brien". Title-cases each segment
    /// split by space, hyphen or apostrophe, culture-invariantly.
    /// </summary>
    public static string NormalizeName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var lower = raw.Trim().ToLowerInvariant();
        var sb = new StringBuilder(lower.Length);
        var capitalizeNext = true;
        foreach (var c in lower)
        {
            if (c is ' ' or '-' or '\'')
            {
                // Collapse runs of spaces left by fixed-width legacy columns.
                if (!(c == ' ' && sb.Length > 0 && sb[^1] == ' '))
                {
                    sb.Append(c);
                }

                capitalizeNext = true;
                continue;
            }

            sb.Append(capitalizeNext ? char.ToUpperInvariant(c) : c);
            capitalizeNext = false;
        }

        return sb.ToString();
    }

    public static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Legacy ids are only unique per source database, so they are namespaced by instance.</summary>
    public static string ExternalId(string sourceInstance, string legacyId) =>
        $"{sourceInstance.Trim().ToUpperInvariant()}:{legacyId.Trim()}";
}
