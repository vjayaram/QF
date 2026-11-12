namespace Qf.ModelGen.Tests;

public class ValidationTests
{
    [Fact]
    public void MinimalModel_IsValid() => Assert.Empty(ModelFixtures.Errors(ModelFixtures.Minimal));

    [Fact]
    public void PhiInEvent_IsRejected()
    {
        var json = ModelFixtures.Minimal.Replace(
            "{ \"name\": \"WidgetId\", \"type\": \"Guid\" }",
            "{ \"name\": \"WidgetId\", \"type\": \"Guid\", \"phi\": true }",
            StringComparison.Ordinal);

        Assert.Contains(ModelFixtures.Errors(json), e => e.Contains("events must not carry PHI", StringComparison.Ordinal));
    }

    [Fact]
    public void ValueObjectInEvent_IsRejected()
    {
        var json = ModelFixtures.Minimal.Replace("\"name\": \"Color\", \"type\": \"Color\" } ] }", "\"name\": \"Code\", \"type\": \"Code\" } ] }", StringComparison.Ordinal);

        Assert.Contains(ModelFixtures.Errors(json), e => e.Contains("primitives and enums only", StringComparison.Ordinal));
    }

    [Fact]
    public void UnknownType_IsRejected()
    {
        var json = ModelFixtures.Minimal.Replace("\"type\": \"decimal\"", "\"type\": \"Money\"", StringComparison.Ordinal);

        Assert.Contains(ModelFixtures.Errors(json), e => e.Contains("unknown type 'Money'", StringComparison.Ordinal));
    }

    [Fact]
    public void UnboundedString_IsRejected()
    {
        var json = ModelFixtures.Minimal.Replace("\"type\": \"string\", \"maxLength\": 10,", "\"type\": \"string\",", StringComparison.Ordinal);

        Assert.Contains(ModelFixtures.Errors(json), e => e.Contains("must declare maxLength", StringComparison.Ordinal));
    }

    [Fact]
    public void TransitionToUndeclaredValue_IsRejected()
    {
        var json = ModelFixtures.Minimal.Replace("\"to\": \"Green\"", "\"to\": \"Blue\"", StringComparison.Ordinal);

        Assert.Contains(ModelFixtures.Errors(json), e => e.Contains("not declared", StringComparison.Ordinal));
    }

    [Fact]
    public void SelfTransition_IsRejected()
    {
        var json = ModelFixtures.Minimal.Replace("\"to\": \"Green\"", "\"to\": \"Red\"", StringComparison.Ordinal);

        Assert.Contains(ModelFixtures.Errors(json), e => e.Contains("self-transitions", StringComparison.Ordinal));
    }

    [Fact]
    public void ReservedAggregateMember_IsRejected()
    {
        var json = ModelFixtures.Minimal.Replace("{ \"name\": \"Price\"", "{ \"name\": \"TenantId\"", StringComparison.Ordinal);

        Assert.Contains(ModelFixtures.Errors(json), e => e.Contains("provided by AggregateRoot", StringComparison.Ordinal));
    }

    [Fact]
    public void NullableSuffix_IsRejected()
    {
        var json = ModelFixtures.Minimal.Replace("\"type\": \"decimal\"", "\"type\": \"decimal?\"", StringComparison.Ordinal);

        Assert.Contains(ModelFixtures.Errors(json), e => e.Contains("\"required\": false", StringComparison.Ordinal));
    }

    [Fact]
    public void InvalidRegex_IsRejected()
    {
        var json = ModelFixtures.Minimal.Replace("^[A-Z]+$", "^[A-Z+$", StringComparison.Ordinal);

        Assert.Contains(ModelFixtures.Errors(json), e => e.Contains("not a valid regular expression", StringComparison.Ordinal));
    }

    [Fact]
    public void UniqueOnMultiColumnValueObject_IsRejected()
    {
        var json = ModelFixtures.Minimal.Replace("\"type\": \"Secret\" }", "\"type\": \"Secret\", \"unique\": true }", StringComparison.Ordinal);

        Assert.Contains(ModelFixtures.Errors(json), e => e.Contains("'unique' is only supported", StringComparison.Ordinal));
    }

    [Fact]
    public void MisspelledProperty_FailsToLoad()
    {
        var json = ModelFixtures.Minimal.Replace("\"maxLength\": 10", "\"maxLenght\": 10", StringComparison.Ordinal);

        Assert.ThrowsAny<System.Text.Json.JsonException>(() => ModelFixtures.Load(json));
    }

    [Fact]
    public void DuplicateTypeNames_AreRejected()
    {
        var json = ModelFixtures.Minimal.Replace("{ \"name\": \"WidgetMade\"", "{ \"name\": \"Widget\"", StringComparison.Ordinal);

        Assert.Contains(ModelFixtures.Errors(json), e => e.Contains("declared more than once", StringComparison.Ordinal));
    }

    [Fact]
    public void Generate_ThrowsWithAllErrors_WhenInvalid()
    {
        var json = ModelFixtures.Minimal
            .Replace("\"type\": \"decimal\"", "\"type\": \"Money\"", StringComparison.Ordinal)
            .Replace("\"to\": \"Green\"", "\"to\": \"Red\"", StringComparison.Ordinal);

        var ex = Assert.Throws<ModelValidationException>(() => Generator.Generate(ModelFixtures.Load(json)));
        Assert.Equal(2, ex.Errors.Count);
    }
}
