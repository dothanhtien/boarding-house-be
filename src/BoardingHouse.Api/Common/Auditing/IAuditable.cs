namespace BoardingHouse.Api.Common;

public interface IAuditable : IUpdateAuditable
{
    DateTimeOffset CreatedAt { get; set; }
    Guid CreatedBy { get; set; }
}
