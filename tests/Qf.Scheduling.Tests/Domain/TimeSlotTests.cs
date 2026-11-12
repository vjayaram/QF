using Qf.Scheduling.Domain;

namespace Qf.Scheduling.Tests.Domain;

public class TimeSlotTests
{
    private static readonly DateTimeOffset Nine = new(2026, 11, 10, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ValidSlot_ExposesDuration()
    {
        var slot = TimeSlot.Create(Nine, Nine.AddMinutes(50)).Value;

        Assert.Equal(TimeSpan.FromMinutes(50), slot.Duration);
    }

    [Theory]
    [InlineData(0, "TimeSlot.End.BeforeStart")]
    [InlineData(-30, "TimeSlot.End.BeforeStart")]
    [InlineData(4, "TimeSlot.TooShort")]
    [InlineData(481, "TimeSlot.TooLong")]
    public void InvalidSlot_IsRejected(int minutes, string code)
    {
        var slot = TimeSlot.Create(Nine, Nine.AddMinutes(minutes));

        Assert.Equal(code, Assert.Single(slot.Errors).Code);
    }

    [Fact]
    public void MissingStart_IsRequired()
    {
        var slot = TimeSlot.Create(default, Nine);

        Assert.Contains(slot.Errors, e => e.Code == "TimeSlot.Start.Required");
    }

    [Fact]
    public void Overlap_IsHalfOpen()
    {
        var nineToTen = TimeSlot.Create(Nine, Nine.AddHours(1)).Value;
        var tenToEleven = TimeSlot.Create(Nine.AddHours(1), Nine.AddHours(2)).Value;
        var nineThirty = TimeSlot.Create(Nine.AddMinutes(30), Nine.AddMinutes(90)).Value;

        Assert.False(nineToTen.Overlaps(tenToEleven));
        Assert.True(nineToTen.Overlaps(nineThirty));
        Assert.True(nineThirty.Overlaps(tenToEleven));
    }
}
