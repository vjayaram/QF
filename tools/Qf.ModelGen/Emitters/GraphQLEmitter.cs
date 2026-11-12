namespace Qf.ModelGen.Emitters;

/// <summary>
/// Emits Hot Chocolate object types. Fields are bound explicitly (nothing leaks by accident),
/// and every PHI field is guarded by the phi.read policy plus an access-audit middleware.
/// </summary>
internal static class GraphQLEmitter
{
    public static IEnumerable<GeneratedFile> Emit(DomainModel model, ModelIndex index)
    {
        var exposed = model.Aggregates.Where(a => a.Graphql.Expose).ToList();
        if (exposed.Count == 0)
        {
            yield break;
        }

        var w = Header.Start(
            model,
            model.Outputs.ApiNamespace,
            "HotChocolate.Authorization",
            "HotChocolate.Execution.Configuration",
            "HotChocolate.Types",
            "Microsoft.Extensions.DependencyInjection",
            "Qf.BuildingBlocks.Hosting.GraphQL",
            model.Namespace);

        var objectVos = new List<ValueObjectModel>();
        foreach (var agg in exposed)
        {
            w.Line();
            using (w.Block($"public sealed class {agg.Name}Type : ObjectType<{agg.Name}>"))
            using (w.Block($"protected override void Configure(IObjectTypeDescriptor<{agg.Name}> descriptor)"))
            {
                w.Line("ArgumentNullException.ThrowIfNull(descriptor);");
                w.Line($"descriptor.Name(\"{agg.Name}\");");
                w.Line("descriptor.BindFieldsExplicitly();");
                w.Line("descriptor.Field(x => x.Id);");

                foreach (var f in agg.Fields)
                {
                    var t = index.Resolve(f.Type);
                    var guard = index.IsPhi(f) ? ".Authorize(GraphQLPolicies.PhiRead).Use<PhiAccessAuditMiddleware>()" : string.Empty;
                    if (t.Kind == TypeKind.ValueObject && index.IsSingleFieldValueObject(f.Type))
                    {
                        var sub = index.ValueObject(f.Type).Fields[0];
                        var gqlType = f.Required ? "NonNullType<StringType>" : "StringType";
                        var access = f.Required ? $".{f.Name}.{sub.Name}" : $".{f.Name}?.{sub.Name}";
                        w.Line($"descriptor.Field(\"{Naming.GraphQLField(f.Name)}\")");
                        w.Line($"    .Type<{gqlType}>()");
                        w.Line($"    .Resolve(ctx => ctx.Parent<{agg.Name}>(){access}){guard};");
                        continue;
                    }

                    if (t.Kind == TypeKind.ValueObject && !objectVos.Contains(index.ValueObject(f.Type)))
                    {
                        objectVos.Add(index.ValueObject(f.Type));
                    }

                    w.Line($"descriptor.Field(x => x.{f.Name}){guard};");
                }

                foreach (var c in agg.Collections)
                {
                    var vo = index.ValueObject(c.Type);
                    if (!objectVos.Contains(vo))
                    {
                        objectVos.Add(vo);
                    }

                    var guard = vo.Fields.Any(f => f.Phi) ? ".Authorize(GraphQLPolicies.PhiRead).Use<PhiAccessAuditMiddleware>()" : string.Empty;
                    w.Line($"descriptor.Field(x => x.{c.Name}){guard};");
                }
            }
        }

        foreach (var vo in objectVos)
        {
            w.Line();
            using (w.Block($"public sealed class {vo.Name}Type : ObjectType<{vo.Name}>"))
            using (w.Block($"protected override void Configure(IObjectTypeDescriptor<{vo.Name}> descriptor)"))
            {
                w.Line("ArgumentNullException.ThrowIfNull(descriptor);");
                w.Line($"descriptor.Name(\"{vo.Name}\");");
                w.Line("descriptor.BindFieldsExplicitly();");
                foreach (var f in vo.Fields)
                {
                    w.Line($"descriptor.Field(x => x.{f.Name});");
                }
            }
        }

        w.Line();
        using (w.Block($"public static class {model.BoundedContext}GeneratedGraphQLTypes"))
        using (w.Block("public static IRequestExecutorBuilder AddGeneratedTypes(this IRequestExecutorBuilder builder)"))
        {
            w.Line("ArgumentNullException.ThrowIfNull(builder);");
            w.Line("return builder");
            foreach (var agg in exposed)
            {
                w.Line($"    .AddType<{agg.Name}Type>()");
            }

            foreach (var vo in objectVos)
            {
                w.Line($"    .AddType<{vo.Name}Type>()");
            }

            w.Line("    ;");
        }

        yield return new(DomainEmitter.Combine(model.Outputs.Api, "GraphQLTypes.g.cs"), w.ToString());
    }
}
