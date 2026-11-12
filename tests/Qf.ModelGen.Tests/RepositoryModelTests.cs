namespace Qf.ModelGen.Tests;

/// <summary>
/// The real models in /models must be valid and the committed generated code must match them.
/// This is the same check CI runs with `--check`, so a hand edit to a .g.cs file fails the build.
/// </summary>
public class RepositoryModelTests
{
    private static string RepoRoot =>
        Cli.FindRepoRoot(AppContext.BaseDirectory)
        ?? Environment.GetEnvironmentVariable("QF_REPO_ROOT")
        ?? throw new InvalidOperationException("Repository root not found.");

    [Fact]
    public void RepositoryModels_AreValid_AndGeneratedCodeIsInSync()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exit = Cli.Run(["--all", "--check", "--root", RepoRoot], stdout, stderr);

        Assert.True(exit == Cli.Ok, stderr.ToString());
    }

    [Fact]
    public void Check_ReportsDrift_ForAHandEditedFile()
    {
        var root = Directory.CreateTempSubdirectory("qf-modelgen-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "models"));
            File.WriteAllText(Path.Combine(root, "models", "sample.model.json"), ModelFixtures.Minimal);
            using var quiet = new StringWriter();
            Assert.Equal(Cli.Ok, Cli.Run(["--all", "--root", root], quiet, quiet));

            File.AppendAllText(Path.Combine(root, "out", "Domain", "Enums.g.cs"), "// hand edit\n");
            using var stderr = new StringWriter();
            var exit = Cli.Run(["--all", "--check", "--root", root], quiet, stderr);

            Assert.Equal(Cli.Drift, exit);
            Assert.Contains("out/Domain/Enums.g.cs", stderr.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void InvalidModel_ExitsWithValidationCode()
    {
        var root = Directory.CreateTempSubdirectory("qf-modelgen-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "models"));
            File.WriteAllText(Path.Combine(root, "models", "bad.model.json"), ModelFixtures.Minimal.Replace("\"to\": \"Green\"", "\"to\": \"Red\"", StringComparison.Ordinal));
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            Assert.Equal(Cli.InvalidModel, Cli.Run(["--all", "--root", root], stdout, stderr));
            Assert.Contains("self-transitions", stderr.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MissingArguments_IsUsageError()
    {
        using var w = new StringWriter();

        Assert.Equal(Cli.Usage, Cli.Run(["--root", RepoRoot], w, w));
    }
}
