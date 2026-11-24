namespace PersonalFinance.Contracts.Auth;

public record AuthRequest(string Email, string Password);
public record RegisterRequest(string DisplayName, string Email, string Password);
public record UserDto(Guid Id, string DisplayName, string Email);
