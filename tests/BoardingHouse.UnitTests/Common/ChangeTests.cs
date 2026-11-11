using BoardingHouse.Application.Common;
using BoardingHouse.Domain.Common;

namespace BoardingHouse.UnitTests.Common;

public class ChangeTests
{
    private static readonly DateOnly Current = new(2026, 1, 1);
    private static readonly DateOnly Sent = new(2026, 2, 1);

    [Fact]
    public void Default_IsNotSet_KeepsCurrent()
    {
        Change<DateOnly?> change = default;

        Assert.False(change.IsSet);
        Assert.Equal(Current, change.OrKeep(Current));
    }

    [Fact]
    public void ImplicitNull_IsSet_Clears()
    {
        Change<DateOnly?> change = null;

        Assert.True(change.IsSet);
        Assert.Null(change.OrKeep(Current));
    }

    [Fact]
    public void ImplicitValue_IsSet_ReplacesCurrent()
    {
        Change<DateOnly?> change = Sent;

        Assert.True(change.IsSet);
        Assert.Equal(Sent, change.OrKeep(Current));
    }

    [Fact]
    public void OptionalToChange_KeepsSetAndValue()
    {
        Optional<DateOnly?> omitted = default;
        Optional<DateOnly?> sentNull = null;
        Optional<DateOnly?> sentValue = Sent;

        Assert.False(omitted.ToChange().IsSet);
        Assert.True(sentNull.ToChange().IsSet);
        Assert.Null(sentNull.ToChange().Value);
        Assert.Equal(Sent, sentValue.ToChange().Value);
    }
}
