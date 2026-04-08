using Microsoft.AspNetCore.Mvc;

namespace Beauty.Api.Contracts.Auth;

public record LoginDto(
    string Email,
    string Password
);

