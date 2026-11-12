using System.Text.Json;
using System.Text.Json.Serialization;

namespace Qf.ModelGen;

internal static class ModelLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // A typo such as "maxLenght" must fail loudly, not silently drop a constraint.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static DomainModel Parse(string json, string sourcePath)
    {
        var model = JsonSerializer.Deserialize<DomainModel>(json, Options)
            ?? throw new InvalidDataException($"{sourcePath}: model is empty.");
        model.SourcePath = sourcePath.Replace('\\', '/');
        return model;
    }

    public static DomainModel Load(string repoRoot, string modelPath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(repoRoot, modelPath));
        var relative = Path.GetRelativePath(repoRoot, fullPath);
        try
        {
            return Parse(File.ReadAllText(fullPath), relative);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{relative}: {ex.Message}", ex);
        }
    }
}
