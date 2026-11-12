namespace Qf.ModelGen;

internal enum TypeKind
{
    Primitive,
    Enum,
    ValueObject,
    Unknown,
}

internal sealed record ResolvedType(string Name, TypeKind Kind)
{
    public bool IsString => Kind == TypeKind.Primitive && Name == "string";

    public bool IsValueType => Kind == TypeKind.Enum || (Kind == TypeKind.Primitive && Name != "string");
}

/// <summary>Lookup of every type a model can reference.</summary>
internal sealed class ModelIndex
{
    public static readonly IReadOnlySet<string> Primitives = new HashSet<string>(StringComparer.Ordinal)
    {
        "string", "int", "long", "decimal", "bool", "Guid", "DateOnly", "TimeOnly", "DateTimeOffset",
    };

    private readonly Dictionary<string, EnumModel> _enums;
    private readonly Dictionary<string, ValueObjectModel> _valueObjects;

    public ModelIndex(DomainModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _enums = model.Enums.GroupBy(e => e.Name).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        _valueObjects = model.ValueObjects.GroupBy(v => v.Name).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
    }

    public ResolvedType Resolve(string typeName)
    {
        if (Primitives.Contains(typeName))
        {
            return new(typeName, TypeKind.Primitive);
        }

        if (_enums.ContainsKey(typeName))
        {
            return new(typeName, TypeKind.Enum);
        }

        return _valueObjects.ContainsKey(typeName)
            ? new(typeName, TypeKind.ValueObject)
            : new(typeName, TypeKind.Unknown);
    }

    public EnumModel Enum(string name) => _enums[name];

    public ValueObjectModel ValueObject(string name) => _valueObjects[name];

    public bool TryGetValueObject(string name, out ValueObjectModel? vo) => _valueObjects.TryGetValue(name, out vo);

    public bool HasTransitions(string enumName) => _enums.TryGetValue(enumName, out var e) && e.Transitions.Count > 0;

    /// <summary>A field is PHI if flagged, or if its value object carries any PHI.</summary>
    public bool IsPhi(FieldModel field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return field.Phi || (_valueObjects.TryGetValue(field.Type, out var vo) && vo.Fields.Any(f => f.Phi));
    }

    public bool IsSingleFieldValueObject(string typeName) =>
        _valueObjects.TryGetValue(typeName, out var vo) && vo.Fields.Count == 1;
}
