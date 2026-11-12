namespace Qf.ModelGen.Emitters;

/// <summary>
/// Emits EF Core mappings (Azure SQL). Conventions encoded here, once, for every aggregate:
/// enums stored as strings, single-field value objects as converted scalar columns,
/// multi-field value objects as owned columns, rowversion concurrency, tenant index.
/// </summary>
internal static class EfCoreEmitter
{
    public static IEnumerable<GeneratedFile> Emit(DomainModel model, ModelIndex index)
    {
        if (model.Aggregates.Count == 0)
        {
            yield break;
        }

        var w = Header.Start(
            model,
            model.Outputs.InfrastructureNamespace,
            "Microsoft.EntityFrameworkCore",
            "Microsoft.EntityFrameworkCore.Metadata.Builders",
            model.Namespace);

        foreach (var agg in model.Aggregates)
        {
            w.Line();
            using (w.Block($"public sealed class {agg.Name}EntityConfiguration : IEntityTypeConfiguration<{agg.Name}>"))
            using (w.Block($"public void Configure(EntityTypeBuilder<{agg.Name}> builder)"))
            {
                w.Line("ArgumentNullException.ThrowIfNull(builder);");
                w.Line($"builder.ToTable(\"{agg.Table}\");");
                w.Line("builder.HasKey(x => x.Id);");
                w.Line("builder.Property(x => x.Id).ValueGeneratedNever();");
                w.Line("builder.Property(x => x.TenantId).IsRequired();");
                w.Line("builder.HasIndex(x => x.TenantId);");
                w.Line("builder.Property<byte[]>(\"RowVersion\").IsRowVersion();");
                w.Line("builder.Ignore(x => x.DomainEvents);");

                foreach (var f in agg.Fields)
                {
                    EmitProperty(w, f, index);
                }

                foreach (var f in agg.Fields.Where(f => f.Unique))
                {
                    w.Line($"builder.HasIndex(x => new {{ x.TenantId, x.{f.Name} }}).IsUnique();");
                }

                foreach (var ix in agg.Indexes)
                {
                    var cols = string.Join(", ", ix.Select(c => $"x.{c}"));
                    w.Line($"builder.HasIndex(x => new {{ x.TenantId, {cols} }});");
                }

                foreach (var c in agg.Collections)
                {
                    var vo = index.ValueObject(c.Type);
                    using (w.Block($"builder.OwnsMany(x => x.{c.Name}, owned =>", "});"))
                    {
                        w.Line($"owned.ToTable(\"{c.Table}\");");
                        w.Line($"owned.WithOwner().HasForeignKey(\"{agg.Name}Id\");");
                        w.Line("owned.Property<int>(\"Id\");");
                        w.Line("owned.HasKey(\"Id\");");
                        foreach (var sub in vo.Fields)
                        {
                            w.Line($"owned.Property(p => p.{sub.Name}){ColumnSuffix(sub, index)};");
                        }

                        if (c.UniqueAcross.Count > 0)
                        {
                            var cols = string.Join(", ", c.UniqueAcross.Select(u => $"p.{u}"));
                            w.Line($"owned.HasIndex(p => new {{ {cols} }}).IsUnique();");
                        }
                    }

                    w.Line($"builder.Navigation(x => x.{c.Name}).UsePropertyAccessMode(PropertyAccessMode.Field);");
                }
            }
        }

        w.Line();
        w.Line($"/// <summary>Applies every generated mapping for the {model.BoundedContext} context.</summary>");
        using (w.Block($"public static class {model.BoundedContext}ModelConfiguration"))
        using (w.Block("public static void ApplyGeneratedConfigurations(ModelBuilder modelBuilder)"))
        {
            w.Line("ArgumentNullException.ThrowIfNull(modelBuilder);");
            foreach (var agg in model.Aggregates)
            {
                w.Line($"modelBuilder.ApplyConfiguration(new {agg.Name}EntityConfiguration());");
            }
        }

        yield return new(DomainEmitter.Combine(model.Outputs.Infrastructure, "EntityConfigurations.g.cs"), w.ToString());
    }

    private static void EmitProperty(CodeWriter w, FieldModel f, ModelIndex index)
    {
        var t = index.Resolve(f.Type);
        if (t.Kind != TypeKind.ValueObject)
        {
            w.Line($"builder.Property(x => x.{f.Name}){ColumnSuffix(f, index)};");
            return;
        }

        var vo = index.ValueObject(f.Type);
        if (vo.Fields.Count == 1)
        {
            var sub = vo.Fields[0];
            w.Line($"builder.Property(x => x.{f.Name})");
            w.Line($"    .HasConversion(v => v.{sub.Name}, v => {vo.Name}.Rehydrate(v))");
            w.Line($"    .HasColumnName(\"{f.Name}\"){ColumnSuffix(sub, index, f.Required)};");
            return;
        }

        using (w.Block($"builder.OwnsOne(x => x.{f.Name}, owned =>", "});"))
        {
            foreach (var sub in vo.Fields)
            {
                w.Line($"owned.Property(p => p.{sub.Name}).HasColumnName(\"{f.Name}_{sub.Name}\"){ColumnSuffix(sub, index, f.Required && sub.Required)};");
            }
        }

        w.Line($"builder.Navigation(x => x.{f.Name}).IsRequired({(f.Required ? "true" : "false")});");
    }

    private static string ColumnSuffix(FieldModel f, ModelIndex index, bool? requiredOverride = null)
    {
        var t = index.Resolve(f.Type);
        var parts = new List<string>();
        if (t.Kind == TypeKind.Enum)
        {
            parts.Add(".HasConversion<string>()");
            parts.Add(".HasMaxLength(32)");
        }

        if (f.MaxLength is { } max)
        {
            parts.Add($".HasMaxLength({max})");
        }

        if (f.Type == "decimal")
        {
            parts.Add($".HasPrecision({f.Precision ?? 18}, {f.Scale ?? 2})");
        }

        var required = requiredOverride ?? f.Required;
        parts.Add($".IsRequired({(required ? "true" : "false")})");
        return string.Concat(parts);
    }
}
