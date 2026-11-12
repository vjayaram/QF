using Qf.ClientRegistry.Domain;

namespace Qf.ClientRegistry.Tests.Domain;

public class ValueObjectTests
{
    [Fact]
    public void Mrn_IsTrimmed_WhenValid()
    {
        var mrn = MedicalRecordNumber.Create("  MRN-1001 ");

        Assert.True(mrn.IsSuccess);
        Assert.Equal("MRN-1001", mrn.Value.Value);
    }

    [Theory]
    [InlineData("", "MedicalRecordNumber.Value.Required")]
    [InlineData("   ", "MedicalRecordNumber.Value.Required")]
    [InlineData("AB1", "MedicalRecordNumber.Value.TooShort")]
    [InlineData("mrn-1001", "MedicalRecordNumber.Value.InvalidFormat")]
    [InlineData("MRN 1001", "MedicalRecordNumber.Value.InvalidFormat")]
    [InlineData("A123456789012345678901234567890123", "MedicalRecordNumber.Value.TooLong")]
    public void Mrn_IsRejected_WhenInvalid(string value, string expectedCode)
    {
        var mrn = MedicalRecordNumber.Create(value);

        Assert.True(mrn.IsFailure);
        Assert.Contains(mrn.Errors, e => e.Code == expectedCode);
    }

    [Fact]
    public void PersonName_ReportsEveryMissingPart()
    {
        var name = PersonName.Create("", " ", null);

        Assert.True(name.IsFailure);
        Assert.Equal(2, name.Errors.Count);
        Assert.Contains(name.Errors, e => e.Code == "PersonName.Given.Required");
        Assert.Contains(name.Errors, e => e.Code == "PersonName.Family.Required");
    }

    [Fact]
    public void PersonName_TreatsBlankMiddleNameAsAbsent()
    {
        var name = PersonName.Create("Jane", "Doe", "   ");

        Assert.True(name.IsSuccess);
        Assert.Null(name.Value.Middle);
    }

    [Fact]
    public void PersonName_RejectsOverlongParts()
    {
        var name = PersonName.Create(new string('a', 101), "Doe", null);

        Assert.Contains(name.Errors, e => e.Code == "PersonName.Given.TooLong");
    }

    [Fact]
    public void ValueObjects_CompareByValue()
    {
        Assert.Equal(TestData.Name("Jane", "Doe"), TestData.Name("Jane", "Doe"));
        Assert.NotEqual(TestData.Name("Jane", "Doe"), TestData.Name("John", "Doe"));
    }

    [Fact]
    public void ToString_RedactsPhi()
    {
        var text = TestData.Name("Jane", "Doe").ToString() + TestData.Mrn("MRN-1001");

        Assert.DoesNotContain("Jane", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Doe", text, StringComparison.Ordinal);
        Assert.DoesNotContain("MRN-1001", text, StringComparison.Ordinal);
        Assert.Contains("***", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ToString_KeepsNonPhiFields()
    {
        var text = TestData.Legacy(LegacySystem.InSync, "IS01:42").ToString();

        Assert.Contains("SourceSystem = InSync", text, StringComparison.Ordinal);
        Assert.DoesNotContain("IS01:42", text, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyIdentity_RejectsUndefinedSourceSystem()
    {
        var identity = LegacyIdentity.Create((LegacySystem)0, "X:1");

        Assert.Contains(identity.Errors, e => e.Code == "LegacyIdentity.SourceSystem.Invalid");
    }
}
