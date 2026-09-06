namespace BoardingHouse.Api.Common;

public class ApiResponse<T>
{
    public bool Success { get; init; } = true;
    public required T Data { get; init; }
    public string? Message { get; init; }
}
