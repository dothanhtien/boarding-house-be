using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Domain.Entities;

public sealed record LateFeePolicy
{
    public static readonly LateFeePolicy None = new(null, null, null);

    public LateFeeType? Type { get; }
    public decimal? Value { get; }
    public int? GraceDays { get; }

    public LateFeePolicy(LateFeeType? type, decimal? value, int? graceDays)
    {
        if ((type is null) != (value is null))
        {
            throw new DomainException("LateFeeType and LateFeeValue must be set together");
        }

        if (value < 0)
        {
            throw new DomainException("LateFeeValue must not be negative");
        }

        if (type == LateFeeType.Percent && value > 100)
        {
            throw new DomainException("LateFeeValue must not exceed 100 for a percent late fee");
        }

        if (graceDays < 0)
        {
            throw new DomainException("LateFeeGraceDays must not be negative");
        }

        Type = type;
        Value = value;
        GraceDays = graceDays;
    }
}
