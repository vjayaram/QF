using Qf.ClientRegistry.Domain;
using Qf.TestKit;

namespace Qf.ClientRegistry.Tests.Domain;

public class ClientMatchingPolicyTests
{
    private static readonly DateOnly Dob = new(1985, 3, 14);
    private readonly Client _existing = TestData.NewClient(new FakeClock(), mrn: "MRN-1001", given: "Jane", family: "O'Brien", dob: Dob);

    [Fact]
    public void SameMrnNameAndDob_IsCertain() =>
        Assert.Equal(MatchConfidence.Certain, ClientMatchingPolicy.Evaluate(_existing, "mrn-1001", "JANE", "OBRIEN", Dob));

    [Fact]
    public void SameFamilyAndInitialWithoutMrn_IsProbable() =>
        Assert.Equal(MatchConfidence.Probable, ClientMatchingPolicy.Evaluate(_existing, null, "J", "o brien", Dob));

    [Fact]
    public void DifferentBirthDate_IsNone() =>
        Assert.Equal(MatchConfidence.None, ClientMatchingPolicy.Evaluate(_existing, "MRN-1001", "Jane", "O'Brien", Dob.AddDays(1)));

    [Fact]
    public void DifferentFamilyName_IsNone() =>
        Assert.Equal(MatchConfidence.None, ClientMatchingPolicy.Evaluate(_existing, "MRN-1001", "Jane", "Smith", Dob));

    [Fact]
    public void MergedCandidate_IsNeverMatched()
    {
        var survivor = TestData.NewClient(new FakeClock(), mrn: "MRN-2002");
        _existing.MergeInto(survivor, new FakeClock());

        Assert.Equal(MatchConfidence.None, ClientMatchingPolicy.Evaluate(_existing, "MRN-1001", "Jane", "O'Brien", Dob));
    }
}
