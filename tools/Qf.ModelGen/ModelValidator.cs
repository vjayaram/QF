using System.Text.RegularExpressions;

namespace Qf.ModelGen;

/// <summary>
/// Enforces modelling rules before any code is generated. These rules encode architecture
/// and compliance decisions so they cannot be bypassed by hand-editing generated code:
///   - domain events never carry PHI (they leave the bounded context on the bus);
///   - every string column has a max length (no nvarchar(max) on hot tables);
///   - state transitions reference real enum values.
/// </summary>
internal static class ModelValidator
{
    private static readonly HashSet<string> ReservedAggregateMembers = new(StringComparer.Ordinal)
    {
        "Id", "TenantId", "DomainEvents",
    };

    public static IReadOnlyList<string> Validate(DomainModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var errors = new List<string>();
        var index = new ModelIndex(model);
        void Fail(string path, string message) => errors.Add($"{model.SourcePath}: {path}: {message}");

        if (!Naming.IsPascalIdentifier(model.BoundedContext))
        {
            Fail("boundedContext", "must be a PascalCase identifier.");
        }

        if (string.IsNullOrWhiteSpace(model.Namespace))
        {
            Fail("namespace", "is required.");
        }

        foreach (var (name, value) in new[]
                 {
                     ("outputs.domain", model.Outputs.Domain),
                     ("outputs.infrastructure", model.Outputs.Infrastructure),
                     ("outputs.infrastructureNamespace", model.Outputs.InfrastructureNamespace),
                     ("outputs.api", model.Outputs.Api),
                     ("outputs.apiNamespace", model.Outputs.ApiNamespace),
                     ("outputs.database", model.Outputs.Database),
                 })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                Fail(name, "is required.");
            }
        }

        var typeNames = model.Enums.Select(e => e.Name)
            .Concat(model.ValueObjects.Select(v => v.Name))
            .Concat(model.Aggregates.Select(a => a.Name))
            .Concat(model.Events.Select(e => e.Name))
            .ToList();
        foreach (var dup in typeNames.GroupBy(n => n, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            Fail(dup.Key, "type name is declared more than once.");
        }

        foreach (var name in typeNames.Where(n => !Naming.IsPascalIdentifier(n)))
        {
            Fail(name, "type names must be PascalCase identifiers.");
        }

        foreach (var e in model.Enums)
        {
            ValidateEnum(e, Fail);
        }

        foreach (var vo in model.ValueObjects)
        {
            var path = $"valueObjects.{vo.Name}";
            if (vo.Fields.Count == 0)
            {
                Fail(path, "must declare at least one field.");
            }

            ValidateFields(path, vo.Fields, index, Fail);
            foreach (var f in vo.Fields)
            {
                var t = index.Resolve(f.Type);
                if (t.Kind == TypeKind.ValueObject)
                {
                    Fail($"{path}.{f.Name}", "nested value objects are not supported; flatten the field or use an enum.");
                }

                if (f.Unique)
                {
                    Fail($"{path}.{f.Name}", "'unique' belongs on the aggregate field that uses this value object.");
                }
            }
        }

        foreach (var agg in model.Aggregates)
        {
            ValidateAggregate(agg, index, Fail);
        }

        foreach (var ev in model.Events)
        {
            var path = $"events.{ev.Name}";
            ValidateFields(path, ev.Fields, index, Fail);
            foreach (var f in ev.Fields)
            {
                var t = index.Resolve(f.Type);
                if (t.Kind == TypeKind.ValueObject)
                {
                    Fail($"{path}.{f.Name}", "events carry primitives and enums only (published language), not value objects.");
                }

                if (f.Phi)
                {
                    Fail($"{path}.{f.Name}", "events must not carry PHI; publish the identifier and let consumers query with authorization.");
                }

                if (!f.Required)
                {
                    Fail($"{path}.{f.Name}", "event fields are always required; model optional facts as a separate event.");
                }
            }
        }

        return errors;
    }

    private static void ValidateEnum(EnumModel e, Action<string, string> fail)
    {
        var path = $"enums.{e.Name}";
        if (e.Values.Count == 0)
        {
            fail(path, "must declare at least one value.");
        }

        foreach (var v in e.Values.Where(v => !Naming.IsPascalIdentifier(v)))
        {
            fail($"{path}.{v}", "enum values must be PascalCase identifiers.");
        }

        foreach (var dup in e.Values.GroupBy(v => v, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            fail($"{path}.{dup.Key}", "value is declared more than once.");
        }

        var values = new HashSet<string>(e.Values, StringComparer.Ordinal);
        var seen = new HashSet<(string, string)>();
        foreach (var t in e.Transitions)
        {
            var tPath = $"{path}.transitions[{t.From}->{t.To}]";
            if (!values.Contains(t.From) || !values.Contains(t.To))
            {
                fail(tPath, "references a value that is not declared.");
            }

            if (string.Equals(t.From, t.To, StringComparison.Ordinal))
            {
                fail(tPath, "self-transitions are not allowed.");
            }

            if (!seen.Add((t.From, t.To)))
            {
                fail(tPath, "transition is declared more than once.");
            }
        }
    }

    private static void ValidateAggregate(AggregateModel agg, ModelIndex index, Action<string, string> fail)
    {
        var path = $"aggregates.{agg.Name}";
        if (string.IsNullOrWhiteSpace(agg.Table))
        {
            fail(path, "table is required.");
        }

        if (agg.Fields.Count == 0)
        {
            fail(path, "must declare at least one field.");
        }

        ValidateFields(path, agg.Fields, index, fail);

        foreach (var f in agg.Fields)
        {
            var fPath = $"{path}.{f.Name}";
            if (ReservedAggregateMembers.Contains(f.Name))
            {
                fail(fPath, "is provided by AggregateRoot and cannot be redeclared.");
            }

            var t = index.Resolve(f.Type);
            if (f.Unique && !(t.Kind is TypeKind.Primitive or TypeKind.Enum || index.IsSingleFieldValueObject(f.Type)))
            {
                fail(fPath, "'unique' is only supported on scalar columns (primitive, enum or single-field value object).");
            }

            if (agg.Graphql.Expose && index.IsSingleFieldValueObject(f.Type)
                && !index.Resolve(index.ValueObject(f.Type).Fields[0].Type).IsString)
            {
                fail(fPath, "single-field value objects exposed over GraphQL must wrap a string.");
            }
        }

        var names = new HashSet<string>(agg.Fields.Select(f => f.Name), StringComparer.Ordinal);
        foreach (var c in agg.Collections)
        {
            var cPath = $"{path}.collections.{c.Name}";
            if (!Naming.IsPascalIdentifier(c.Name))
            {
                fail(cPath, "collection names must be PascalCase identifiers.");
            }

            if (!names.Add(c.Name))
            {
                fail(cPath, "name collides with another member.");
            }

            if (string.IsNullOrWhiteSpace(c.Table))
            {
                fail(cPath, "table is required.");
            }

            if (!index.TryGetValueObject(c.Type, out var vo) || vo is null)
            {
                fail(cPath, $"type '{c.Type}' must be a value object.");
                continue;
            }

            foreach (var u in c.UniqueAcross.Where(u => vo.Fields.All(f => f.Name != u)))
            {
                fail(cPath, $"uniqueAcross references unknown field '{u}'.");
            }
        }

        foreach (var ix in agg.Indexes)
        {
            if (ix.Count == 0)
            {
                fail($"{path}.indexes", "an index needs at least one field.");
            }

            foreach (var col in ix)
            {
                var field = agg.Fields.FirstOrDefault(f => f.Name == col);
                if (field is null)
                {
                    fail($"{path}.indexes", $"references unknown field '{col}'.");
                }
                else if (index.Resolve(field.Type).Kind == TypeKind.ValueObject && !index.IsSingleFieldValueObject(field.Type))
                {
                    fail($"{path}.indexes", $"'{col}' is a multi-column value object and cannot be indexed directly.");
                }
            }
        }
    }

    private static void ValidateFields(string owner, List<FieldModel> fields, ModelIndex index, Action<string, string> fail)
    {
        foreach (var dup in fields.GroupBy(f => f.Name, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            fail($"{owner}.{dup.Key}", "field is declared more than once.");
        }

        foreach (var f in fields)
        {
            var path = $"{owner}.{f.Name}";
            if (!Naming.IsPascalIdentifier(f.Name))
            {
                fail(path, "field names must be PascalCase identifiers.");
            }

            if (f.Type.EndsWith('?'))
            {
                fail(path, "do not use '?' in types; set \"required\": false instead.");
                continue;
            }

            var t = index.Resolve(f.Type);
            if (t.Kind == TypeKind.Unknown)
            {
                fail(path, $"unknown type '{f.Type}'.");
                continue;
            }

            if (!t.IsString && (f.MaxLength is not null || f.MinLength is not null || f.Pattern is not null))
            {
                fail(path, "maxLength, minLength and pattern apply to strings only.");
            }

            if (t.IsString && !owner.StartsWith("events.", StringComparison.Ordinal) && f.MaxLength is null)
            {
                fail(path, "string fields must declare maxLength (no unbounded columns).");
            }

            if (f.MinLength is { } min && f.MaxLength is { } max && min > max)
            {
                fail(path, "minLength cannot exceed maxLength.");
            }

            if ((f.Precision is not null || f.Scale is not null) && f.Type != "decimal")
            {
                fail(path, "precision and scale apply to decimals only.");
            }

            if (f.Pattern is not null)
            {
                try
                {
                    _ = new Regex(f.Pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
                }
                catch (ArgumentException ex)
                {
                    fail(path, $"pattern is not a valid regular expression: {ex.Message}");
                }
            }
        }
    }
}
