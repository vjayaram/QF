namespace Qf.BuildingBlocks.Domain;

/// <summary>
/// Marks protected health information (HIPAA identifiers such as name, DOB, MRN).
/// The model generator uses it to: redact ToString output, require the phi.read policy
/// on GraphQL fields, add access auditing, and forbid the value in domain events.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, Inherited = true)]
public sealed class PhiAttribute : Attribute;
