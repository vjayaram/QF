using Qf.ModelGen.Emitters;

namespace Qf.ModelGen;

internal static class Generator
{
    public static IReadOnlyList<GeneratedFile> Generate(DomainModel model)
    {
        var errors = ModelValidator.Validate(model);
        if (errors.Count > 0)
        {
            throw new ModelValidationException(errors);
        }

        var index = new ModelIndex(model);
        return
        [
            .. DomainEmitter.Emit(model, index),
            .. EfCoreEmitter.Emit(model, index),
            .. GraphQLEmitter.Emit(model, index),
            .. SqlSchemaEmitter.Emit(model, index),
        ];
    }
}

internal sealed class ModelValidationException(IReadOnlyList<string> errors)
    : Exception($"Model validation failed with {errors.Count} error(s).")
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
