using System.Text.Json.Serialization;

namespace Qf.ModelGen;

internal sealed class DomainModel
{
    [JsonPropertyName("$comment")]
    public string? Comment { get; init; }

    public string BoundedContext { get; init; } = string.Empty;

    public string Namespace { get; init; } = string.Empty;

    public ModelOutputs Outputs { get; init; } = new();

    public List<EnumModel> Enums { get; init; } = [];

    public List<ValueObjectModel> ValueObjects { get; init; } = [];

    public List<AggregateModel> Aggregates { get; init; } = [];

    public List<EventModel> Events { get; init; } = [];

    /// <summary>Repo-relative path of the model file, used in generated headers.</summary>
    [JsonIgnore]
    public string SourcePath { get; set; } = string.Empty;
}

internal sealed class ModelOutputs
{
    public string Domain { get; init; } = string.Empty;

    public string Infrastructure { get; init; } = string.Empty;

    public string InfrastructureNamespace { get; init; } = string.Empty;

    public string Api { get; init; } = string.Empty;

    public string ApiNamespace { get; init; } = string.Empty;

    /// <summary>SQL Database Project folder for generated DDL (deployed as a DACPAC).</summary>
    public string Database { get; init; } = string.Empty;
}

internal sealed class FieldModel
{
    public string Name { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;

    public bool Required { get; init; } = true;

    public int? MaxLength { get; init; }

    public int? MinLength { get; init; }

    public string? Pattern { get; init; }

    public int? Precision { get; init; }

    public int? Scale { get; init; }

    public bool Phi { get; init; }

    public bool Unique { get; init; }

    public string? Description { get; init; }
}

internal sealed class EnumModel
{
    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public List<string> Values { get; init; } = [];

    public List<TransitionModel> Transitions { get; init; } = [];
}

internal sealed class TransitionModel
{
    public string From { get; init; } = string.Empty;

    public string To { get; init; } = string.Empty;
}

internal sealed class ValueObjectModel
{
    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public List<FieldModel> Fields { get; init; } = [];
}

internal sealed class CollectionModel
{
    public string Name { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;

    public string Table { get; init; } = string.Empty;

    public List<string> UniqueAcross { get; init; } = [];
}

internal sealed class GraphQLOptions
{
    public bool Expose { get; init; }
}

internal sealed class AggregateModel
{
    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public string Table { get; init; } = string.Empty;

    public List<FieldModel> Fields { get; init; } = [];

    public List<CollectionModel> Collections { get; init; } = [];

    public List<List<string>> Indexes { get; init; } = [];

    public GraphQLOptions Graphql { get; init; } = new();
}

internal sealed class EventModel
{
    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public List<FieldModel> Fields { get; init; } = [];
}

internal sealed record GeneratedFile(string RelativePath, string Content);
