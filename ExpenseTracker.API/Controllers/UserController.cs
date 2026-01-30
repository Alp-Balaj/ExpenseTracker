using DnsClient.Protocol;
using ExpenseTracker.Domain.Entities.User;
using ExpenseTracker.Domain.Interfaces;
using ExpenseTracker.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
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
        private readonly IConfiguration _configuration;
        private readonly IRefreshTokenRepository _refreshTokenRepository;

        public UserController(UserManager<User> userManager, IConfiguration configuration, IRefreshTokenRepository refreshTokenRepository)
        {
            _userManager = userManager;
            _configuration = configuration;
            _refreshTokenRepository = refreshTokenRepository;
        }

        #region API calls
        [HttpPost("signup")]
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
                        throw new Exception("Could not generate a unique username");
                }
            }

            var result = await _userManager.CreateAsync(user, model.Password);
            if (!result.Succeeded)
                return BadRequest(result.Errors);

            return Ok("User registered successfully");
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto model)
        {
            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null || !await _userManager.CheckPasswordAsync(user, model.Password))
                return Unauthorized("Invalid credentials");

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
            await _refreshTokenRepository.AddAsync(new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TokenHash = refreshTokenHash,
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = refreshExpiresUtc
            });

            // Add raw refresh in HttpOnly cookie
            SetRefreshCookie(rawRefreshToken, refreshExpiresUtc);

            return Ok(new { Token = token });
        }

        [HttpPost("refresh")]
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

            await _refreshTokenRepository.RevokeAsync(existing, replacedByTokenHash: newHash);

            await _refreshTokenRepository.AddAsync(new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TokenHash = newHash,
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = newExpiresUtc
            });

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
                    await _refreshTokenRepository.RevokeAsync(existing);
                }
            }

            ClearRefreshCookie();
            return Ok();
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

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            
            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
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
                Path = "/api/User/refresh"
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
                Path = "/api/User/refresh"
            });
        }

        private async Task<bool> CheckValidityOfUsername(string username)
        {
            return await _userManager.FindByNameAsync(username) == null;
        }

        #endregion
    }

    public record RegisterDto(string FirstName, string LastName, string Email, string Password);
    public record LoginDto(string Email, string Password);
}
    