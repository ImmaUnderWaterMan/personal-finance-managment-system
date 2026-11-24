using System.Net.Mail;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Contracts.Auth;
using PersonalFinance.Persistence.Data;
using PersonalFinance.Persistence.Entities;

namespace PersonalFinance.Api;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(FinanceDbContext context, IPasswordHasher<User> hasher) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<UserDto>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Trim().Length > 100 ||
            string.IsNullOrWhiteSpace(email) || email.Length > 256 || !MailAddress.TryCreate(email, out _) ||
            request.Password is null || request.Password.Length < 8 || request.Password.Length > 128)
            return Problem(statusCode: 400, title: "Укажите имя, корректный email и пароль от 8 до 128 символов.");
        var user = new User { Id = Guid.NewGuid(), Email = email, DisplayName = request.DisplayName.Trim(), IsActive = true };
        user.Password = hasher.HashPassword(user, request.Password);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        context.Users.Add(user);
        await context.SaveChangesAsync(cancellationToken);
        context.Accounts.Add(new Account { Id = Guid.NewGuid(), UserId = user.Id, Name = "Основной счёт", OpeningBalance = 0 });
        foreach (var name in new[] { "Продукты", "Транспорт", "Развлечения", "Жильё", "Здоровье", "Другой расход" })
            context.Categories.Add(new Category { Id = Guid.NewGuid(), UserId = user.Id, Name = name, Type = 1 });
        foreach (var name in new[] { "Зарплата", "Подработка", "Другой доход" })
            context.Categories.Add(new Category { Id = Guid.NewGuid(), UserId = user.Id, Name = name, Type = 0 });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await SignIn(user);
        return new UserDto(user.Id, user.DisplayName, user.Email);
    }

    [HttpPost("login")]
    public async Task<ActionResult<UserDto>> Login(AuthRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || request.Email.Length > 256 || request.Password is null || request.Password.Length > 128)
            return Problem(statusCode: 401, title: "Неверный email или пароль.");
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await context.Users.SingleOrDefaultAsync(u => u.Email.Trim().ToLower() == email, cancellationToken);
        if (user is null || !user.IsActive) return Problem(statusCode: 401, title: "Неверный email или пароль.");
        PasswordVerificationResult verification;
        try { verification = hasher.VerifyHashedPassword(user, user.Password, request.Password); }
        catch (Exception exception) when (exception is FormatException or ArgumentException) { verification = PasswordVerificationResult.Failed; }
        if (verification == PasswordVerificationResult.Failed) return Problem(statusCode: 401, title: "Неверный email или пароль.");
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.Password = hasher.HashPassword(user, request.Password);
            await context.SaveChangesAsync(cancellationToken);
        }
        await SignIn(user);
        return new UserDto(user.Id, user.DisplayName, user.Email);
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> Me(CancellationToken cancellationToken)
    {
        var id = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await context.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id && u.IsActive, cancellationToken);
        return user is null ? Unauthorized() : new UserDto(user.Id, user.DisplayName, user.Email);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync();
        return NoContent();
    }

    private Task SignIn(User user) => HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier,user.Id.ToString()),
            new Claim(ClaimTypes.Name,user.DisplayName) }, CookieAuthenticationDefaults.AuthenticationScheme)));
}
