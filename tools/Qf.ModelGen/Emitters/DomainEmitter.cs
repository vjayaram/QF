namespace Qf.ModelGen.Emitters;

/// <summary>Emits framework-free domain structure: enums, state machines, value objects, aggregate state, events.</summary>
internal static class DomainEmitter
{
    public static IEnumerable<GeneratedFile> Emit(DomainModel model, ModelIndex index)
    {
        var dir = model.Outputs.Domain;
        if (model.Enums.Count > 0)
        {
            yield return new(Combine(dir, "Enums.g.cs"), EmitEnums(model));
        }

        if (model.ValueObjects.Count > 0)
        {
            yield return new(Combine(dir, "ValueObjects.g.cs"), EmitValueObjects(model, index));
        }

        if (model.Aggregates.Count > 0)
        {
            yield return new(Combine(dir, "Aggregates.g.cs"), EmitAggregates(model, index));
        }

        if (model.Events.Count > 0)
        {
            yield return new(Combine(dir, "Events.g.cs"), EmitEvents(model, index));
        }
    }

    internal static string Combine(string dir, string file) => $"{dir.TrimEnd('/')}/{file}";

    internal static string CSharpType(FieldModel f, ModelIndex index)
    {
        var t = index.Resolve(f.Type);
        return f.Required ? t.Name : t.Name + "?";
    }

    private static string EmitEnums(DomainModel model)
    {
        var w = Header.Start(model, model.Namespace);
        foreach (var e in model.Enums)
        {
            w.Line();
            w.Summary(e.Description);
            using (w.Block($"public enum {e.Name}"))
            {
                // Start at 1 so default(T) is never a valid state; strings are stored, not ordinals.
                for (var i = 0; i < e.Values.Count; i++)
                {
                    w.Line($"{e.Values[i]} = {i + 1},");
                }
            }

            if (e.Transitions.Count == 0)
            {
                continue;
            }

            w.Line();
            w.Line($"/// <summary>Allowed lifecycle transitions for <see cref=\"{e.Name}\"/>, declared in the model.</summary>");
            using (w.Block($"public static class {e.Name}Transitions"))
            {
                w.Line($"private static readonly ({e.Name} From, {e.Name} To)[] Allowed =");
                w.Line("[");
                foreach (var t in e.Transitions)
                {
                    w.Line($"    ({e.Name}.{t.From}, {e.Name}.{t.To}),");
                }

                w.Line("];");
                w.Line();
                w.Line($"public static bool CanTransition({e.Name} from, {e.Name} to) => Array.IndexOf(Allowed, (from, to)) >= 0;");
                w.Line();
                w.Line($"public static IReadOnlyList<{e.Name}> AllowedFrom({e.Name} from) =>");
                w.Line("    [.. Allowed.Where(t => t.From == from).Select(t => t.To)];");
            }
        }

        return w.ToString();
    }

    private static string EmitValueObjects(DomainModel model, ModelIndex index)
    {
        var w = Header.Start(
            model,
            model.Namespace,
            "System.ComponentModel",
            "System.Text",
            "System.Text.RegularExpressions",
            "Qf.BuildingBlocks.Domain");

        foreach (var vo in model.ValueObjects)
        {
            var parameters = string.Join(", ", vo.Fields.Select(f => $"{CSharpType(f, index)} {Naming.Camel(f.Name)}"));
            var arguments = string.Join(", ", vo.Fields.Select(f => Naming.Camel(f.Name)));

            w.Line();
            w.Summary(vo.Description ?? $"Immutable value object. Construct with {vo.Name}.Create(...).");
            using (w.Block($"public sealed partial record {vo.Name}"))
            {
                foreach (var f in vo.Fields.Where(f => f.Pattern is not null))
                {
                    w.Line($"private static readonly Regex {f.Name}Pattern =");
                    w.Line($"    new({Naming.Verbatim(f.Pattern!)}, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));");
                    w.Line();
                }

                using (w.Block($"private {vo.Name}({parameters})"))
                {
                    foreach (var f in vo.Fields)
                    {
                        w.Line($"{f.Name} = {Naming.Camel(f.Name)};");
                    }
                }

                foreach (var f in vo.Fields)
                {
                    w.Line();
                    w.Summary(f.Description);
                    if (f.Phi)
                    {
                        w.Line("[Phi]");
                    }

                    w.Line($"public {CSharpType(f, index)} {f.Name} {{ get; private init; }}");
                }

                w.Line();
                using (w.Block($"public static Result<{vo.Name}> Create({parameters})"))
                {
                    w.Line("var errors = new List<Error>();");
                    foreach (var f in vo.Fields)
                    {
                        EmitFieldValidation(w, vo.Name, f, index);
                    }

                    w.Line();
                    using (w.Block("if (errors.Count == 0)"))
                    {
                        w.Line($"ValidateInvariants({arguments}, errors);");
                    }

                    w.Line();
                    w.Line("return errors.Count == 0");
                    w.Line($"    ? Result<{vo.Name}>.Success(new {vo.Name}({arguments}))");
                    w.Line($"    : Result<{vo.Name}>.Failure(errors);");
                }

                w.Line();
                w.Line("/// <summary>Hook for cross-field rules; implement in a hand-written partial.</summary>");
                w.Line($"static partial void ValidateInvariants({parameters}, List<Error> errors);");

                if (vo.Fields.Count == 1)
                {
                    var f = vo.Fields[0];
                    w.Line();
                    w.Line("/// <summary>Persistence-only factory: values loaded from the database were validated on write.</summary>");
                    w.Line("[EditorBrowsable(EditorBrowsableState.Never)]");
                    w.Line($"public static {vo.Name} Rehydrate({CSharpType(f, index)} {Naming.Camel(f.Name)}) => new({Naming.Camel(f.Name)});");
                }

                if (vo.Fields.Any(f => f.Phi))
                {
                    w.Line();
                    w.Line("/// <summary>Redacts PHI so value objects are safe to log or include in exception messages.</summary>");
                    using (w.Block("private bool PrintMembers(StringBuilder builder)"))
                    {
                        for (var i = 0; i < vo.Fields.Count; i++)
                        {
                            var f = vo.Fields[i];
                            var sep = i == 0 ? string.Empty : ", ";
                            w.Line(f.Phi
                                ? $"builder.Append(\"{sep}{f.Name} = ***\");"
                                : $"builder.Append(\"{sep}{f.Name} = \").Append({f.Name});");
                        }

                        w.Line("return true;");
                    }
                }
            }
        }

        return w.ToString();
    }

    private static void EmitFieldValidation(CodeWriter w, string owner, FieldModel f, ModelIndex index)
    {
        var p = Naming.Camel(f.Name);
        var code = $"{owner}.{f.Name}";
        var t = index.Resolve(f.Type);
        string Add(string suffix, string message) =>
            $"errors.Add(Error.Validation({Naming.Literal(code + "." + suffix)}, {Naming.Literal(message)}));";

        w.Line();
        if (t.IsString)
        {
            var checks = new List<string>();
            if (f.MinLength is { } min)
            {
                checks.Add($"if ({p}.Length < {min})");
                checks.Add("{");
                checks.Add("    " + Add("TooShort", $"{f.Name} must be at least {min} characters."));
                checks.Add("}");
            }

            if (f.MaxLength is { } max)
            {
                checks.Add($"if ({p}.Length > {max})");
                checks.Add("{");
                checks.Add("    " + Add("TooLong", $"{f.Name} must be at most {max} characters."));
                checks.Add("}");
            }

            if (f.Pattern is not null)
            {
                checks.Add($"if (!{f.Name}Pattern.IsMatch({p}))");
                checks.Add("{");
                checks.Add("    " + Add("InvalidFormat", $"{f.Name} has an invalid format."));
                checks.Add("}");
            }

            using (w.Block($"if (string.IsNullOrWhiteSpace({p}))"))
            {
                w.Line(f.Required ? Add("Required", $"{f.Name} is required.") : $"{p} = null;");
            }

            using (w.Block("else"))
            {
                w.Line($"{p} = {p}.Trim();");
                w.Lines(checks);
            }

            return;
        }

        if (!f.Required)
        {
            if (t.Kind == TypeKind.Enum)
            {
                using (w.Block($"if ({p} is {{ }} {p}Value && !Enum.IsDefined({p}Value))"))
                {
                    w.Line(Add("Invalid", $"{f.Name} is not a valid value."));
                }
            }

            return;
        }

        switch (t.Kind)
        {
            case TypeKind.Enum:
                using (w.Block($"if (!Enum.IsDefined({p}))"))
                {
                    w.Line(Add("Invalid", $"{f.Name} is not a valid value."));
                }

                break;
            case TypeKind.ValueObject:
                using (w.Block($"if ({p} is null)"))
                {
                    w.Line(Add("Required", $"{f.Name} is required."));
                }

                break;
            case TypeKind.Primitive when f.Type is "Guid" or "DateOnly" or "DateTimeOffset" or "TimeOnly":
                using (w.Block($"if ({p} == default)"))
                {
                    w.Line(Add("Required", $"{f.Name} is required."));
                }

                break;
            default:
                w.Line($"// {f.Name}: no generated constraints.");
                break;
        }
    }

    private static string EmitAggregates(DomainModel model, ModelIndex index)
    {
        var w = Header.Start(model, model.Namespace, "Qf.BuildingBlocks.Domain");
        foreach (var agg in model.Aggregates)
        {
            w.Line();
            w.Summary(agg.Description ?? $"{agg.Name} aggregate root. Behaviour lives in {agg.Name}.cs.");
            using (w.Block($"public sealed partial class {agg.Name} : AggregateRoot<Guid>"))
            {
                foreach (var c in agg.Collections)
                {
                    w.Line($"private readonly List<{c.Type}> {Naming.BackingField(c.Name)} = [];");
                    w.Line();
                }

                w.Line("/// <summary>Used by EF Core when materialising; behaviour code uses factory methods.</summary>");
                w.Line($"private {agg.Name}()");
                w.Line("{");
                w.Line("}");

                foreach (var f in agg.Fields)
                {
                    var type = CSharpType(f, index);
                    var t = index.Resolve(f.Type);
                    var init = f.Required && !t.IsValueType ? " = null!;" : string.Empty;
                    w.Line();
                    w.Summary(f.Description);
                    if (index.IsPhi(f))
                    {
                        w.Line("[Phi]");
                    }

                    w.Line($"public {type} {f.Name} {{ get; private set; }}{init}");
                }

                foreach (var c in agg.Collections)
                {
                    w.Line();
                    if (index.ValueObject(c.Type).Fields.Any(f => f.Phi))
                    {
                        w.Line("[Phi]");
                    }

                    w.Line($"public IReadOnlyCollection<{c.Type}> {c.Name} => {Naming.BackingField(c.Name)}.AsReadOnly();");
                }

                foreach (var f in agg.Fields.Where(f => f.Required && index.HasTransitions(f.Type)))
                {
                    w.Line();
                    w.Line($"/// <summary>Moves {f.Name} along the state machine declared for {f.Type} in the model.</summary>");
                    using (w.Block($"private Result Transition{f.Name}To({f.Type} target)"))
                    {
                        using (w.Block($"if (!{f.Type}Transitions.CanTransition({f.Name}, target))"))
                        {
                            w.Line("return Result.Failure(Error.Rule(");
                            w.Line($"    \"{agg.Name}.{f.Name}.InvalidTransition\",");
                            w.Line($"    $\"{agg.Name} cannot move from {{{f.Name}}} to {{target}}.\"));");
                        }

                        w.Line();
                        w.Line($"{f.Name} = target;");
                        w.Line("return Result.Success();");
                    }
                }
            }
        }

        return w.ToString();
    }

    private static string EmitEvents(DomainModel model, ModelIndex index)
    {
        var w = Header.Start(model, model.Namespace, "Qf.BuildingBlocks.Domain");
        foreach (var ev in model.Events)
        {
            var parameters = string.Join(", ", ev.Fields.Select(f => $"{CSharpType(f, index)} {f.Name}"));
            w.Line();
            w.Summary(ev.Description);
            w.Line($"public sealed record {ev.Name}({parameters}) : DomainEvent;");
        }

        w.Line();
        w.Line("/// <summary>Event-type registry used by the outbox to serialise and route events.</summary>");
        using (w.Block($"public static class {model.BoundedContext}DomainEvents"))
        {
            w.Line("public static IReadOnlyDictionary<string, Type> ByName { get; } = new Dictionary<string, Type>(StringComparer.Ordinal)");
            w.Line("{");
            foreach (var ev in model.Events)
            {
                w.Line($"    [\"{ev.Name}\"] = typeof({ev.Name}),");
            }

            w.Line("};");
        }

        return w.ToString();
    }
}
