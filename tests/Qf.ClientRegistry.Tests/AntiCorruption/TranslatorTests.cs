using Qf.ClientRegistry.AntiCorruption;
using Qf.ClientRegistry.Domain;

namespace Qf.ClientRegistry.Tests.AntiCorruption;

public class TranslatorTests
{
    [Fact]
    public void CareLogic_TranslatesRow_AndNormalisesNames()
    {
        var row = new CareLogicPatientRow(1001, "SMITH-JONES", "o'brien  mary", "q", "03/14/1985", "mrn-77", "A");

        var snapshot = CareLogicTranslator.Translate(row, "cl01").Value;

        Assert.Equal(LegacySystem.CareLogic, snapshot.SourceSystem);
        Assert.Equal("CL01:1001", snapshot.ExternalId);
        Assert.Equal("MRN-77", snapshot.Mrn);
        Assert.Equal("Smith-Jones", snapshot.FamilyName);
        Assert.Equal("O'Brien Mary", snapshot.GivenName);
        Assert.Equal("Q", snapshot.MiddleName);
        Assert.Equal(new DateOnly(1985, 3, 14), snapshot.DateOfBirth);
        Assert.True(snapshot.IsActive);
    }

    [Theory]
    [InlineData("I")]
    [InlineData("d")]
    public void CareLogic_InactiveAndDischargedCodes_AreInactive(string code)
    {
        var row = new CareLogicPatientRow(1, "Doe", "Jane", null, "01/02/1990", null, code);

        Assert.False(CareLogicTranslator.Translate(row, "CL01").Value.IsActive);
    }

    [Fact]
    public void CareLogic_RejectsBadDateAndUnknownStatus()
    {
        var badDate = CareLogicTranslator.Translate(new CareLogicPatientRow(1, "Doe", "Jane", null, "1990-01-02", null, "A"), "CL01");
        var badStatus = CareLogicTranslator.Translate(new CareLogicPatientRow(1, "Doe", "Jane", null, "01/02/1990", null, "Z"), "CL01");

        Assert.Equal("CareLogic.BirthDate.Unparseable", Assert.Single(badDate.Errors).Code);
        Assert.Equal("CareLogic.Status.Unknown", Assert.Single(badStatus.Errors).Code);
    }

    [Fact]
    public void Credible_TranslatesDto_DefaultingActive()
    {
        var dto = new CredibleClientDto(55, "jane", "DOE", " ", new DateTime(1985, 3, 14, 0, 0, 0, DateTimeKind.Unspecified), " ", null);

        var snapshot = CredibleTranslator.Translate(dto, "cr01").Value;

        Assert.Equal("CR01:55", snapshot.ExternalId);
        Assert.Null(snapshot.Mrn);
        Assert.Null(snapshot.MiddleName);
        Assert.Equal("Jane", snapshot.GivenName);
        Assert.True(snapshot.IsActive);
    }

    [Fact]
    public void Credible_RequiresDateOfBirth()
    {
        var result = CredibleTranslator.Translate(new CredibleClientDto(1, "a", "b", null, null, null, true), "CR01");

        Assert.Equal("Credible.Dob.Missing", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void InSync_ParsesPidSegment()
    {
        const string pid = "PID|1||00042^^^INSYNC^PI~MRN-9^^^AGENCY^MR||DOE^JANE^Q||19850314|F";

        var snapshot = InSyncHl7Translator.Translate(pid, "ADT^A04", "is01").Value;

        Assert.Equal("IS01:00042", snapshot.ExternalId);
        Assert.Equal("MRN-9", snapshot.Mrn);
        Assert.Equal("Jane", snapshot.GivenName);
        Assert.Equal("Doe", snapshot.FamilyName);
        Assert.Equal("Q", snapshot.MiddleName);
        Assert.Equal(new DateOnly(1985, 3, 14), snapshot.DateOfBirth);
        Assert.True(snapshot.IsActive);
    }

    [Fact]
    public void InSync_DischargeMessage_IsInactive()
    {
        var snapshot = InSyncHl7Translator.Translate("PID|1||7^^^INSYNC^PI||DOE^JOHN||19700101", "ADT^A03", "IS01").Value;

        Assert.False(snapshot.IsActive);
    }

    [Theory]
    [InlineData("MSH|^~\\&|", "InSync.Pid.Missing")]
    [InlineData("PID|1||MRN-9^^^AGENCY^MR||DOE^JANE||19850314", "InSync.Pid3.NoInternalId")]
    [InlineData("PID|1||7^^^INSYNC^PI||DOE^JANE||1985-03-14", "InSync.Pid7.Unparseable")]
    public void InSync_RejectsMalformedSegments(string segment, string code)
    {
        var result = InSyncHl7Translator.Translate(segment, "ADT^A04", "IS01");

        Assert.Equal(code, Assert.Single(result.Errors).Code);
    }
}
