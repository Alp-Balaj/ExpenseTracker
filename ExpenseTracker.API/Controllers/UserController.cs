using ExpenseTracker.Application.Validation;
using ExpenseTracker.Domain.Entities.User;
using ExpenseTracker.Domain.Interfaces;
using ExpenseTracker.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;


namespace ExpenseTracker.API.Controllers
{

    [ApiController]
    [Route("api/[controller]")]
    public class UserController : ControllerBase
    {
        private readonly UserManager<User> _userManager;
        private readonly SignInManager<User> _signInManager;
        private readonly IConfiguration _configuration;
        private readonly IRefreshTokenRepository _refreshTokenRepository;
        private readonly ILogger<UserController> _logger;

        public UserController(UserManager<User> userManager, SignInManager<User> signInManager, IConfiguration configuration, IRefreshTokenRepository refreshTokenRepository, ILogger<UserController> logger)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _configuration = configuration;
            _refreshTokenRepository = refreshTokenRepository;
            _logger = logger;
        }

        #region API calls
        [HttpPost("signup")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> SignUp([FromBody] RegisterDto model)
        {
            var user = new User { FirstName = model.FirstName, LastName = model.LastName, UserName = model.FirstName + model.LastName, Email = model.Email };
            
            if(user.UserName == null)
            {
                return BadRequest();
            }

            if(!await CheckValidityOfUsername(user.UserName))
            {
                var rng = Random.Shared;
                int attempts = 0;
                string baseUsername = user.UserName;

                user.UserName = baseUsername + rng.Next(10, 9999);

                while (!await CheckValidityOfUsername(user.UserName))
                {
                    user.UserName = baseUsername + rng.Next(10, 9999);
                    attempts++;

                    if (attempts > 10)
                    {
                        _logger.LogError("Signup failed: could not generate a unique username for {Email}", model.Email);
                        return BadRequest(new { message = "Registration failed — check your details." });
                    }
                }
            }

            var result = await _userManager.CreateAsync(user, model.Password);
            if (!result.Succeeded)
            {
                _logger.LogWarning("Signup failed for {Email}: {Errors}", model.Email,
                    string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}")));

                // Only password-policy errors are safe to echo back; anything else
                // (duplicate email/username) would let a caller probe existing accounts.
                var passwordErrors = result.Errors
                    .Where(e => e.Code.StartsWith("Password"))
                    .Select(e => e.Description)
                    .ToArray();

                return BadRequest(new
                {
                    message = "Registration failed — check your details.",
                    errors = new { password = passwordErrors }
                });
            }

            return Ok("User registered successfully");
        }

        [HttpPost("login")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> Login([FromBody] LoginDto model)
        {
            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null)
                return Unauthorized("Invalid credentials");

            // Records failed attempts and honors LockoutEnd; resets the counter on success.
            var passwordCheck = await _signInManager.CheckPasswordSignInAsync(user, model.Password, lockoutOnFailure: true);
            if (!passwordCheck.Succeeded)
            {
                // Lockout state stays server-side only — the response must not differ from a wrong password.
                if (passwordCheck.IsLockedOut)
                    _logger.LogWarning("Login attempt for locked-out user {UserId}", user.Id);

                return Unauthorized("Invalid credentials");
            }

            // JWT
            var token = GenerateJwtToken(user);

            // Refresh Token
            var rawRefreshToken = TokenUtil.GenerateRefreshToken();

            var pepper = _configuration["RefreshToken:Pepper"];
            var refreshTokenHash = TokenUtil.HashRefreshToken(rawRefreshToken, pepper);

            var daysStr = _configuration["RefreshToken:Longevity"];
            var days = int.TryParse(daysStr, out var d) ? d : 14;
            var refreshExpiresUtc = DateTime.UtcNow.AddDays(days);

            // Add hashed refresh to db
            _refreshTokenRepository.Add(new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TokenHash = refreshTokenHash,
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = refreshExpiresUtc
            });
            await _refreshTokenRepository.SaveAsync();

            // Add raw refresh in HttpOnly cookie
            SetRefreshCookie(rawRefreshToken, refreshExpiresUtc);

            return Ok(new { Token = token });
        }

        [HttpPost("refresh")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> Refresh()
        {
            // Get raw refresh token from HttpOnly cookie
            if (!Request.Cookies.TryGetValue("refresh_token", out var rawToken) || string.IsNullOrWhiteSpace(rawToken))
                return Unauthorized("Missing refresh token");

            // Hash it
            var pepper = _configuration["RefreshToken:Pepper"];
            var tokenHash = TokenUtil.HashRefreshToken(rawToken, pepper);

            // Find it
            var existing = await _refreshTokenRepository.GetByHashAsync(tokenHash);
            if (existing == null)
            {
                ClearRefreshCookie();
                return Unauthorized("Invalid refresh token");
            }

            // Validate it
            if (!existing.IsActive)
            {
                // A revoked token coming back means the rotation chain leaked: someone
                // (user or thief) holds a stale copy. Kill every session for the user.
                if (existing.RevokedAtUtc != null)
                {
                    _logger.LogWarning("Revoked refresh token reused for user {UserId}; revoking all active refresh tokens", existing.UserId);
                    await _refreshTokenRepository.RevokeAllActiveForUserAsync(existing.UserId);
                    await _refreshTokenRepository.SaveAsync();
                }

                ClearRefreshCookie();
                return Unauthorized("Expired or revoked refresh token");
            }

            // Load user
            var user = await _userManager.FindByIdAsync(existing.UserId);
            if (user == null)
            {
                ClearRefreshCookie();
                return Unauthorized("User not found");
            }

            // Generate new one
            var newRaw = TokenUtil.GenerateRefreshToken();
            var newHash = TokenUtil.HashRefreshToken(newRaw, pepper);

            var daysStr = _configuration["RefreshToken:Longevity"];
            var days = int.TryParse(daysStr, out var d) ? d : 14;
            var newExpiresUtc = DateTime.UtcNow.AddDays(days);

            // Revoke + add are staged together and committed in one save, so a crash
            // mid-rotation can't leave the user with no valid token.
            _refreshTokenRepository.Revoke(existing, replacedByTokenHash: newHash);

            _refreshTokenRepository.Add(new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TokenHash = newHash,
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = newExpiresUtc
            });

            await _refreshTokenRepository.SaveAsync();

            SetRefreshCookie(newRaw, newExpiresUtc);

            // Issue new access token
            var newAccessToken = GenerateJwtToken(user);

            return Ok(new { Token = newAccessToken });
        }
        
        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            if (Request.Cookies.TryGetValue("refresh_token", out var raw) &&
                !string.IsNullOrWhiteSpace(raw))
            {
                var pepper = _configuration["RefreshToken:Pepper"];
                var hash = TokenUtil.HashRefreshToken(raw, pepper);

                var existing = await _refreshTokenRepository.GetByHashAsync(hash);
                if (existing != null && existing.RevokedAtUtc == null)
                {
                    _refreshTokenRepository.Revoke(existing);
                    await _refreshTokenRepository.SaveAsync();
                }
            }

            ClearRefreshCookie();
            return Ok();
        }

        [Authorize]
        [HttpPost("change-password")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return Unauthorized();

            // Verifies the current password and runs Identity's password validators.
            var result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
            if (!result.Succeeded)
            {
                _logger.LogWarning("Password change failed for user {UserId}: {Errors}", user.Id,
                    string.Join("; ", result.Errors.Select(e => e.Code)));

                return BadRequest(new
                {
                    message = "Password change failed.",
                    errors = result.Errors.Select(e => e.Description).ToArray()
                });
            }

            // Sessions established under the old password must die with it — a thief holding
            // a refresh token keeps rotating forever otherwise. This also revokes the current
            // session's refresh token; its access token stays valid until expiry, after which
            // the user logs in again with the new password.
            await _refreshTokenRepository.RevokeAllActiveForUserAsync(user.Id);
            await _refreshTokenRepository.SaveAsync();
            ClearRefreshCookie();

            _logger.LogInformation("Password changed for user {UserId}; all refresh tokens revoked", user.Id);

            return Ok(new { message = "Password changed successfully" });
        }

        [Authorize]
        [HttpPut("profile")]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileDto model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return Unauthorized();

            // Email is deliberately not updatable here: it is the login identifier
            // (Login uses FindByEmailAsync), so changing it needs its own verified flow.
            var newUsername = model.Username.Trim();
            if (!string.Equals(user.UserName, newUsername, StringComparison.Ordinal))
            {
                var result = await _userManager.SetUserNameAsync(user, newUsername);
                if (!result.Succeeded)
                {
                    return BadRequest(new
                    {
                        message = "Profile update failed.",
                        errors = result.Errors.Select(e => e.Description).ToArray()
                    });
                }
            }

            // The sidebar reads the username from the JWT's unique_name claim, so hand
            // back a fresh access token carrying the new value.
            var token = GenerateJwtToken(user);
            return Ok(new { Token = token });
        }

        #endregion
        #region Helper functions
        private string GenerateJwtToken(User user)
        {
            var jwtKey = _configuration["Jwt:Key"] 
                ?? throw new InvalidOperationException("JWT Key is not configured in appsettings.json");

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
                new Claim(JwtRegisteredClaimNames.UniqueName, user.UserName ?? string.Empty),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var keyString = _configuration["Jwt:Key"]
                ?? throw new InvalidOperationException("Jwt:Key is not configured");

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyString));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var expires = DateTime.UtcNow.AddMinutes(60);

            var issuer = _configuration["Jwt:Issuer"]
                ?? throw new InvalidOperationException("Jwt:Issuer is not configured");

            var audience = _configuration["Jwt:Audience"]
                ?? throw new InvalidOperationException("Jwt:Audience is not configured");


            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(60),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private void SetRefreshCookie(string rawRefreshToken, DateTime expiresUtc)
        {
            Response.Cookies.Append("refresh_token", rawRefreshToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                Expires = expiresUtc,
                Path = "/api/User"
            });
        }

        private void ClearRefreshCookie()
        {
            Response.Cookies.Append("refresh_token", "", new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                Expires = DateTime.UtcNow.AddDays(-1),
                Path = "/api/User"
            });
        }


        private async Task<bool> CheckValidityOfUsername(string username)
        {
            return await _userManager.FindByNameAsync(username) == null;
        }

        #endregion
    }

    public record RegisterDto(
        [Required, NotWhitespace, MaxLength(50)] string FirstName,
        [Required, NotWhitespace, MaxLength(50)] string LastName,
        [Required, EmailAddress] string Email,
        [Required] string Password);
    public record LoginDto(
        [Required, EmailAddress] string Email,
        [Required] string Password);
    public record ChangePasswordDto(
        [Required] string CurrentPassword,
        [Required] string NewPassword);
    public record UpdateProfileDto(
        [Required, NotWhitespace, MaxLength(100)] string Username);
}
    