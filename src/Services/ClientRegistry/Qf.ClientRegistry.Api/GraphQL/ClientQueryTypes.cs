using HotChocolate.Data.Filters;
using HotChocolate.Data.Sorting;
using Qf.ClientRegistry.Domain;

namespace Qf.ClientRegistry.Api.GraphQL;

/// <summary>
/// Filtering is explicit and excludes PHI. Allowing "where: { name: ... }" would let a caller without
/// phi.read probe demographics through the filter even though the field itself is protected.
/// </summary>
public sealed class ClientFilterType : FilterInputType<Client>
{
    protected override void Configure(IFilterInputTypeDescriptor<Client> descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        descriptor.BindFieldsExplicitly();
        descriptor.Field(c => c.Id);
        descriptor.Field(c => c.Status);
    }
}

public sealed class ClientSortType : SortInputType<Client>
{
    protected override void Configure(ISortInputTypeDescriptor<Client> descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        descriptor.BindFieldsExplicitly();
        descriptor.Field(c => c.Id);
        descriptor.Field(c => c.Status);
    }
}
