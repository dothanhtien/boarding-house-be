namespace BoardingHouse.Api.Common;

public record ListQuery : PageRequest
{
    public string? Search { get; init; }
    public string? SortBy { get; init; }
    public SortOrder SortOrder { get; init; } = SortOrder.Desc;
}
