using System.Reflection;
using Qf.BuildingBlocks.Domain;

namespace Qf.ArchitectureTests;

/// <summary>
/// Dependency rules of the hexagonal/DDD layering, enforced on compiled assemblies.
/// Domain and application layers must stay framework-free so they are portable and fast to test.
/// </summary>
public class LayeringTests
{
    private static readonly Assembly[] DomainAssemblies =
    [
        typeof(Qf.ClientRegistry.Domain.Client).Assembly,
        typeof(Qf.Scheduling.Domain.Appointment).Assembly,
    ];

    private static readonly Assembly[] ApplicationAssemblies =
    [
        typeof(Qf.ClientRegistry.Application.Commands.RegisterClientHandler).Assembly,
        typeof(Qf.Scheduling.Application.Commands.ScheduleAppointmentHandler).Assembly,
        typeof(Qf.ClientRegistry.AntiCorruption.CareLogicTranslator).Assembly,
    ];

    private static readonly string[] Forbidden = ["Microsoft.EntityFrameworkCore", "HotChocolate", "Azure.", "Microsoft.AspNetCore", "Qf.BuildingBlocks.Infrastructure", "Qf.BuildingBlocks.Hosting"];

    private static bool IsFramework(AssemblyName a) =>
        a.Name is not null && (a.Name.StartsWith("System", StringComparison.Ordinal) || a.Name is "netstandard" or "mscorlib");

    [Fact]
    public void Domain_DependsOnlyOnBclAndSharedKernel()
    {
        foreach (var assembly in DomainAssemblies)
        {
            var illegal = assembly.GetReferencedAssemblies()
                .Where(r => !IsFramework(r) && r.Name != "Qf.BuildingBlocks")
                .Select(r => r.Name)
                .ToList();

            Assert.True(illegal.Count == 0, $"{assembly.GetName().Name} references {string.Join(", ", illegal)}");
        }
    }

    [Fact]
    public void Domains_DoNotReferenceEachOther()
    {
        var names = DomainAssemblies.Select(a => a.GetName().Name).ToHashSet();
        foreach (var assembly in DomainAssemblies)
        {
            var crossContext = assembly.GetReferencedAssemblies().Where(r => names.Contains(r.Name)).ToList();
            Assert.Empty(crossContext);
        }
    }

    [Fact]
    public void ApplicationAndAcl_DoNotReferenceInfrastructureOrFrameworks()
    {
        foreach (var assembly in ApplicationAssemblies)
        {
            var illegal = assembly.GetReferencedAssemblies()
                .Where(r => Forbidden.Any(f => r.Name!.StartsWith(f, StringComparison.Ordinal)))
                .Select(r => r.Name)
                .ToList();

            Assert.True(illegal.Count == 0, $"{assembly.GetName().Name} references {string.Join(", ", illegal)}");
        }
    }

    [Fact]
    public void Contracts_ReferenceNothingButTheBcl()
    {
        var refs = typeof(Qf.Contracts.Topics).Assembly.GetReferencedAssemblies().Where(r => !IsFramework(r)).ToList();

        Assert.Empty(refs);
    }

    [Fact]
    public void Aggregates_AreSealed_AndHaveNoPublicSetters()
    {
        var aggregates = DomainAssemblies.SelectMany(a => a.GetTypes())
            .Where(t => t.BaseType is { IsGenericType: true } b && b.GetGenericTypeDefinition() == typeof(AggregateRoot<>))
            .ToList();

        Assert.NotEmpty(aggregates);
        foreach (var type in aggregates)
        {
            Assert.True(type.IsSealed, $"{type.Name} must be sealed");
            var publicSetters = type.GetProperties().Where(p => p.SetMethod?.IsPublic == true).Select(p => p.Name).ToList();
            Assert.True(publicSetters.Count == 0, $"{type.Name} has public setters: {string.Join(", ", publicSetters)}");
        }
    }

    [Fact]
    public void DomainEvents_CarryNoPhi()
    {
        var events = DomainAssemblies.SelectMany(a => a.GetTypes()).Where(t => typeof(IDomainEvent).IsAssignableFrom(t) && !t.IsAbstract).ToList();

        Assert.NotEmpty(events);
        foreach (var e in events)
        {
            var phi = e.GetProperties().Where(p => p.GetCustomAttribute<PhiAttribute>() is not null).ToList();
            Assert.Empty(phi);
        }
    }

    [Fact]
    public void PhiOnAggregates_IsMarked()
    {
        var client = typeof(Qf.ClientRegistry.Domain.Client);

        foreach (var name in new[] { "Name", "DateOfBirth", "Mrn", "LegacyIdentities" })
        {
            Assert.NotNull(client.GetProperty(name)!.GetCustomAttribute<PhiAttribute>());
        }

        Assert.Null(client.GetProperty("Status")!.GetCustomAttribute<PhiAttribute>());
    }
}
