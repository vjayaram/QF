using System.Text.RegularExpressions;

namespace Qf.ModelGen;

internal static partial class Naming
{
    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
        "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern",
        "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface",
        "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override",
        "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof",
        "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
    };

    [GeneratedRegex("^[A-Z][A-Za-z0-9]*$", RegexOptions.CultureInvariant)]
    private static partial Regex PascalIdentifier();

    public static bool IsPascalIdentifier(string value) => !string.IsNullOrEmpty(value) && PascalIdentifier().IsMatch(value);

    public static string Camel(string pascal)
    {
        ArgumentException.ThrowIfNullOrEmpty(pascal);
        var camel = char.ToLowerInvariant(pascal[0]) + pascal[1..];
        return CSharpKeywords.Contains(camel) ? "@" + camel : camel;
    }

    /// <summary>GraphQL field names: camelCase without the C# keyword escape.</summary>
    public static string GraphQLField(string pascal) => Camel(pascal).TrimStart('@');

    public static string BackingField(string pascal) => "_" + Camel(pascal).TrimStart('@');

    public static string Verbatim(string value) => "@\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    public static string Literal(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
