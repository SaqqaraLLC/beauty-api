
namespace Beauty.Api.Contracts.Auth;

public record ResetDto(
    string Email,
    string Token,
    string NewPassword
);
