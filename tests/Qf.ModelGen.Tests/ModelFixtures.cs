namespace Qf.ModelGen.Tests;

internal static class ModelFixtures
{
    /// <summary>Smallest valid model; tests mutate the JSON to break one rule at a time.</summary>
    public const string Minimal = """
    {
      "boundedContext": "Sample",
      "namespace": "Sample.Domain",
      "outputs": {
        "domain": "out/Domain", "infrastructure": "out/Infra", "infrastructureNamespace": "Sample.Infra",
        "api": "out/Api", "apiNamespace": "Sample.Api", "database": "out/Db"
      },
      "enums": [
        { "name": "Color", "values": [ "Red", "Green" ], "transitions": [ { "from": "Red", "to": "Green" } ] }
      ],
      "valueObjects": [
        { "name": "Code", "fields": [ { "name": "Value", "type": "string", "maxLength": 10, "pattern": "^[A-Z]+$" } ] },
        { "name": "Secret", "fields": [ { "name": "Text", "type": "string", "maxLength": 20, "phi": true }, { "name": "Hint", "type": "string", "maxLength": 20, "required": false } ] }
      ],
      "aggregates": [
        {
          "name": "Widget", "table": "Widgets",
          "fields": [
            { "name": "Code", "type": "Code", "unique": true },
            { "name": "Secret", "type": "Secret" },
            { "name": "Color", "type": "Color" },
            { "name": "Price", "type": "decimal", "precision": 10, "scale": 2 }
          ],
          "graphql": { "expose": true }
        }
      ],
      "events": [ { "name": "WidgetMade", "fields": [ { "name": "WidgetId", "type": "Guid" }, { "name": "Color", "type": "Color" } ] } ]
    }
    """;

    public static DomainModel Load(string json = Minimal) => ModelLoader.Parse(json, "models/sample.model.json");

    public static IReadOnlyList<string> Errors(string json) => ModelValidator.Validate(Load(json));

    public static string File(IReadOnlyList<GeneratedFile> files, string suffix) =>
        files.Single(f => f.RelativePath.EndsWith(suffix, StringComparison.Ordinal)).Content;
}
