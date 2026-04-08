using Beauty.Api.Contracts.Auth;

namespace Beauty.Api.Contracts.Auth;

public record TokenRequest(string Email, string Password);
