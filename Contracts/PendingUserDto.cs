namespace Beauty.Api.Contracts;

public sealed class PendingUserDto
{
    public string Id { get; init; } = default!;
    public string Email { get; init; } = default!;
    public string? Role { get; init; }
    public string Status { get; init; } = default!;
}


