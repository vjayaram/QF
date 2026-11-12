namespace Qf.ModelGen;

/// <summary>
/// Usage:
///   dotnet run --project tools/Qf.ModelGen -- --all            regenerate every model
///   dotnet run --project tools/Qf.ModelGen -- --all --check    CI: fail if generated code drifted
///   dotnet run --project tools/Qf.ModelGen -- --model models/scheduling.model.json
/// </summary>
internal static class Cli
{
    public const int Ok = 0;
    public const int Drift = 1;
    public const int InvalidModel = 2;
    public const int Usage = 64;

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var check = args.Contains("--check");
        var root = Option(args, "--root") ?? FindRepoRoot(Directory.GetCurrentDirectory());
        if (root is null)
        {
            stderr.WriteLine("Could not find the repository root (a folder containing 'models/'). Pass --root.");
            return Usage;
        }

        var models = new List<string>();
        if (args.Contains("--all"))
        {
            models.AddRange(Directory.GetFiles(Path.Combine(root, "models"), "*.model.json").Order(StringComparer.Ordinal));
        }

        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--model")
            {
                models.Add(args[i + 1]);
            }
        }

        if (models.Count == 0)
        {
            stderr.WriteLine("Specify --all or one or more --model <path>.");
            return Usage;
        }

        var drifted = new List<string>();
        foreach (var modelPath in models)
        {
            IReadOnlyList<GeneratedFile> files;
            try
            {
                files = Generator.Generate(ModelLoader.Load(root, modelPath));
            }
            catch (ModelValidationException ex)
            {
                foreach (var e in ex.Errors)
                {
                    stderr.WriteLine($"error: {e}");
                }

                return InvalidModel;
            }
            catch (InvalidDataException ex)
            {
                stderr.WriteLine($"error: {ex.Message}");
                return InvalidModel;
            }

            foreach (var file in files)
            {
                var path = Path.Combine(root, file.RelativePath);
                var existing = File.Exists(path) ? File.ReadAllText(path).ReplaceLineEndings("\n") : null;
                if (existing == file.Content)
                {
                    continue;
                }

                if (check)
                {
                    drifted.Add(file.RelativePath);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, file.Content);
                stdout.WriteLine($"wrote {file.RelativePath}");
            }
        }

        if (drifted.Count > 0)
        {
            foreach (var d in drifted)
            {
                stderr.WriteLine($"drift: {d} does not match its model. Run the generator and commit the result.");
            }

            return Drift;
        }

        stdout.WriteLine(check ? "Generated code is in sync with the models." : "Generation complete.");
        return Ok;
    }

    internal static string? FindRepoRoot(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "models")))
            {
                return dir.FullName;
            }
        }

        return null;
    }

    private static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i < args.Length - 1 ? args[i + 1] : null;
    }
}
