using ExpenseTracker.Application.Interfaces;
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
        /// <summary>Signup collects no base currency, so every new user starts on EUR
        /// (the same default UserPreferences.BaseCurrency has always had).</summary>
        private const string DefaultBaseCurrency = "EUR";

        private readonly UserManager<User> _userManager;
        private readonly SignInManager<User> _signInManager;
        private readonly IConfiguration _configuration;
        private readonly IRefreshTokenRepository _refreshTokenRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IUserDataSeeder _seeder;
        private readonly IEmailSender _emailSender;
        private readonly IPotService _pots;
        private readonly IMcpTokenService _mcpTokens;
        private readonly ILogger<UserController> _logger;

        public UserController(UserManager<User> userManager, SignInManager<User> signInManager, IConfiguration configuration, IRefreshTokenRepository refreshTokenRepository, IUnitOfWork unitOfWork, IUserDataSeeder seeder, IEmailSender emailSender, IPotService pots, IMcpTokenService mcpTokens, ILogger<UserController> logger)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _configuration = configuration;
            _refreshTokenRepository = refreshTokenRepository;
            _unitOfWork = unitOfWork;
            _seeder = seeder;
            _emailSender = emailSender;
            _pots = pots;
            _mcpTokens = mcpTokens;
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

            // User creation and the starter-catalogue seed commit together: a failed
            // seed must not leave an account with no currencies/categories behind,
            // and a failed creation must not leave orphan catalogue rows.
            await using var transaction = await _unitOfWork.BeginTransactionAsync();

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

            await _seeder.SeedNewUserAsync(user.Id, DefaultBaseCurrency);
            await transaction.CommitAsync();

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
            await _mcpTokens.RevokeAllForUserAsync(user.Id);
            ClearRefreshCookie();

            _logger.LogInformation("Password changed for user {UserId}; all refresh tokens revoked", user.Id);

            return Ok(new { message = "Password changed successfully" });
        }

        /// <summary>
        /// Starts a password reset. Always 200 with the same body whether or not the
        /// email exists — anything else is an account-enumeration oracle. The reset
        /// link points at the SPA's /reset-password route with email + token in the
        /// query string; the sender behind IEmailSender decides how it travels.
        /// </summary>
        [HttpPost("forgot-password")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto model)
        {
            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user?.Email != null)
            {
                var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                var link = BuildSpaLink("/reset-password", user.Email, token);

                await _emailSender.SendAsync(new EmailMessage(
                    user.Email,
                    "Reset your ExpenseTracker password",
                    $"Someone (hopefully you) asked to reset your ExpenseTracker password.\n\n" +
                    $"Open this link to choose a new one:\n{link}\n\n" +
                    "If this wasn't you, ignore this email — your password is unchanged."));

                _logger.LogInformation("Password reset email queued for user {UserId}", user.Id);
            }

            return Ok(new { message = "If that email is registered, a reset link has been sent." });
        }

        /// <summary>
        /// Completes a password reset with the token from the emailed link. Failures
        /// are deliberately vague about *why* (unknown email and bad token read the
        /// same); only password-policy violations are echoed, matching signup.
        /// </summary>
        [HttpPost("reset-password")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto model)
        {
            var invalid = new { message = "This reset link is invalid or has expired. Request a new one." };

            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null)
                return BadRequest(invalid);

            var result = await _userManager.ResetPasswordAsync(user, model.Token, model.NewPassword);
            if (!result.Succeeded)
            {
                _logger.LogWarning("Password reset failed for user {UserId}: {Errors}", user.Id,
                    string.Join("; ", result.Errors.Select(e => e.Code)));

                var passwordErrors = result.Errors
                    .Where(e => e.Code.StartsWith("Password"))
                    .Select(e => e.Description)
                    .ToArray();

                return passwordErrors.Length > 0
                    ? BadRequest(new { message = "Password does not meet the requirements.", errors = new { password = passwordErrors } })
                    : BadRequest(invalid);
            }

            // Same session hygiene as change-password: sessions opened under the old
            // password (possibly the attacker's reason the user is resetting) die now.
            await _refreshTokenRepository.RevokeAllActiveForUserAsync(user.Id);
            await _refreshTokenRepository.SaveAsync();
            await _mcpTokens.RevokeAllForUserAsync(user.Id);

            _logger.LogInformation("Password reset completed for user {UserId}; all refresh tokens revoked", user.Id);

            return Ok(new { message = "Password has been reset. You can now log in." });
        }

        /// <summary>
        /// Emails the signed-in user a confirmation link for their own address.
        /// Confirmation is recorded and surfaced, never enforced — login stays open
        /// to unconfirmed accounts (every pre-existing production user is one).
        /// </summary>
        [Authorize]
        [HttpPost("send-confirmation-email")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> SendConfirmationEmail()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var user = await _userManager.FindByIdAsync(userId);
            if (user?.Email == null)
                return Unauthorized();

            if (user.EmailConfirmed)
                return Ok(new { message = "Email is already confirmed.", alreadyConfirmed = true });

            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            var link = BuildSpaLink("/confirm-email", user.Email, token);

            await _emailSender.SendAsync(new EmailMessage(
                user.Email,
                "Confirm your ExpenseTracker email",
                $"Open this link to confirm your email address:\n{link}"));

            _logger.LogInformation("Confirmation email queued for user {UserId}", user.Id);

            return Ok(new { message = "Confirmation email sent.", alreadyConfirmed = false });
        }

        /// <summary>Records the confirmation from the emailed link. Anonymous — the
        /// user lands here from their inbox, possibly on a device with no session.</summary>
        [HttpPost("confirm-email")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> ConfirmEmail([FromBody] ConfirmEmailDto model)
        {
            var invalid = new { message = "This confirmation link is invalid or has expired. Request a new one from your account settings." };

            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null)
                return BadRequest(invalid);

            // A second click on the same link should read as success, not as a scary
            // error — the token was consumed by the first one.
            if (user.EmailConfirmed)
                return Ok(new { message = "Email is already confirmed." });

            var result = await _userManager.ConfirmEmailAsync(user, model.Token);
            if (!result.Succeeded)
            {
                _logger.LogWarning("Email confirmation failed for user {UserId}: {Errors}", user.Id,
                    string.Join("; ", result.Errors.Select(e => e.Code)));
                return BadRequest(invalid);
            }

            _logger.LogInformation("Email confirmed for user {UserId}", user.Id);

            return Ok(new { message = "Email confirmed." });
        }

        /// <summary>
        /// The signed-in user's own account info. Exists because EmailConfirmed
        /// lives nowhere in the JWT and the SPA needs it to show the (non-blocking)
        /// "email not confirmed" hint in account settings.
        /// </summary>
        [Authorize]
        [HttpGet("me")]
        public async Task<IActionResult> Me()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return Unauthorized();

            return Ok(new
            {
                email = user.Email,
                username = user.UserName,
                firstName = user.FirstName,
                lastName = user.LastName,
                emailConfirmed = user.EmailConfirmed
            });
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

        /// <summary>
        /// C·4: deletes this account and everything it owns, irreversibly.
        /// <para>
        /// The blast radius stops at the account boundary and that is asserted by
        /// <c>AccountDeletionTests</c>, not merely intended: every user-owned table
        /// carries a cascading foreign key to <c>AspNetUsers</c>
        /// (<c>ApplicationDbContext.ConfigureUserOwnership</c>), so one DELETE takes
        /// exactly this user's rows and cannot reach anyone else's.
        /// </para>
        /// <para>
        /// Guarded by the current password rather than by a confirmation dialog
        /// alone. A dialog protects against a misclick; a password protects against
        /// someone else's hands on an unlocked laptop, which is the case that
        /// actually loses years of financial history. Rate-limited on the auth lane
        /// so it cannot be used to test passwords.
        /// </para>
        /// <para>
        /// Cycle U adds a pre-step, and the order is not optional. Pot rows are
        /// <em>not</em> user-owned: <c>PotMembership.UserId</c> is nullable with
        /// <c>OnDelete(Restrict)</c>, precisely so one member leaving the app
        /// cannot take the group's history with them. Anonymizing first strips
        /// this user out of every pot, leaving the rows behind under a neutral
        /// label; only then can Identity remove the user. Skip the pre-step and
        /// the delete fails at the foreign key — loudly, which is the intent.
        /// </para>
        /// </summary>
        [Authorize]
        [HttpPost("delete-account")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> DeleteAccount([FromBody] DeleteAccountDto model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return Unauthorized();

            if (!await _userManager.CheckPasswordAsync(user, model.Password))
            {
                _logger.LogWarning("Account deletion refused for user {UserId}: wrong password", user.Id);
                return BadRequest(new { message = "That password is not correct." });
            }

            // Refresh tokens first: if the delete somehow fails, the session is
            // already dead rather than left half-way, and the tokens are the one
            // thing that would still work afterwards.
            await _refreshTokenRepository.RevokeAllActiveForUserAsync(user.Id);
            await _refreshTokenRepository.SaveAsync();

            // Then the pots. This is the anonymize half of anonymize-then-delete:
            // every membership loses its user id and keeps its row, so the group
            // still adds up and still reads sensibly. Nothing personal survives
            // it — the label becomes "Former member".
            var anonymized = await _pots.AnonymizeMembershipsAsync(user.Id);
            if (anonymized > 0)
                _logger.LogInformation("Anonymized {Count} pot membership(s) before deleting user {UserId}", anonymized, user.Id);

            var result = await _userManager.DeleteAsync(user);
            if (!result.Succeeded)
            {
                _logger.LogError("Account deletion failed for user {UserId}: {Errors}", user.Id,
                    string.Join("; ", result.Errors.Select(e => e.Code)));

                return BadRequest(new
                {
                    message = "The account could not be deleted.",
                    errors = result.Errors.Select(e => e.Description).ToArray()
                });
            }

            ClearRefreshCookie();
            _logger.LogInformation("Account {UserId} deleted at the owner's request", userId);

            return Ok(new { message = "Account deleted." });
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

        /// <summary>
        /// Absolute SPA link for emailed flows. FRONTEND_ORIGIN (already the CORS
        /// source of truth for a split deployment) wins; without it the request's own
        /// origin is used, which is correct wherever the SPA ships in wwwroot
        /// (production serves API and SPA from one host).
        /// </summary>
        private string BuildSpaLink(string path, string email, string token)
        {
            var origin = _configuration["FRONTEND_ORIGIN"];
            if (string.IsNullOrWhiteSpace(origin))
                origin = $"{Request.Scheme}://{Request.Host}";

            return $"{origin.TrimEnd('/')}{path}?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
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
    public record ForgotPasswordDto(
        [Required, EmailAddress] string Email);
    public record ResetPasswordDto(
        [Required, EmailAddress] string Email,
        [Required] string Token,
        [Required] string NewPassword);
    public record ConfirmEmailDto(
        [Required, EmailAddress] string Email,
        [Required] string Token);

    /// <summary>
    /// C·4. The password is the guard; there is deliberately nothing else to
    /// supply, because a second "type DELETE to confirm" field is theatre next to
    /// a credential the person deleting has to actually know.
    /// </summary>
    public record DeleteAccountDto(
        [Required] string Password);
}
    