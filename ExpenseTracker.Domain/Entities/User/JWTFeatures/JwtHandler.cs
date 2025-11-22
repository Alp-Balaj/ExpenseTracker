using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ExpenseTracker.Domain.User.JWTFeatures.Interface;
using ExpenseTracker.Domain.Interfaces;

namespace ExpenseTracker.Domain.User.JWTFeatures
{
    public class JwtHandler : IJwtHandler
    {
        private readonly IConfiguration _configuration;
        private readonly IConfigurationSection _jwtSettings;
        private readonly UserManager<User> _userManager;
        private readonly IRefreshTokenRepository _refreshTokenRepository;
        public JwtHandler(IConfiguration configuration, UserManager<User> userManager, IRefreshTokenRepository refreshTokenRepository)
        {
            _configuration = configuration;
            _jwtSettings = configuration.GetSection("JWTSettings");
            _userManager = userManager;
            _refreshTokenRepository = refreshTokenRepository;
        }

        public async Task<(string, RefreshToken)> CreateTokenAsync(User user, IList<string> roles, string? testRefreshToken)
        {
            var signinCredentials = GetSigningCredentials();
            var claims = await GetClaimsAsync(user, roles);
            var tokenOptions = GenerateTokenOptions(signinCredentials, claims);

            var jwtToken = new JwtSecurityTokenHandler().WriteToken(tokenOptions);
            var refreshToken = await GenerateRefreshTokenAsync(testRefreshToken, user.Id);

            if(testRefreshToken != refreshToken.Token)
            {
                await _refreshTokenRepository.SaveRefreshTokenAsync(user.Id, refreshToken.Token); 
            }

            return (jwtToken, refreshToken);
        }

        private async Task<RefreshToken> GenerateRefreshTokenAsync(string testRefreshToken, string userId)
        {
            if (string.IsNullOrEmpty(testRefreshToken))
            {
                return new RefreshToken
                {
                    Token = Guid.NewGuid().ToString(),
                    UserId = userId,
                    ExpiresAt = DateTime.UtcNow,
                };
            }

            var existingToken = await _refreshTokenRepository.GetRefreshTokenAsync(testRefreshToken);

            return
            existingToken ?? new RefreshToken
                            {
                                Token = Guid.NewGuid().ToString(),
                                UserId = userId,
                                ExpiresAt = DateTime.UtcNow,
                            };
        }


        private SigningCredentials GetSigningCredentials()
        {
            var key = Encoding.UTF8.GetBytes(_jwtSettings["securityKey"]);
            var secret = new SymmetricSecurityKey(key);

            return new SigningCredentials(secret, SecurityAlgorithms.HmacSha256);
        }

        private async Task<List<Claim>> GetClaimsAsync(User user, IList<string> roles)
        {
            var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, user.UserName),
                    new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                    new Claim(JwtRegisteredClaimNames.Sub, user.Id)
                };

            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var userClaims = await _userManager.GetClaimsAsync(user);
            claims.AddRange(userClaims);

            return claims;
        }

        private JwtSecurityToken GenerateTokenOptions(SigningCredentials signingCredentials, List<Claim> claims)
        {
            var tokenOptions = new JwtSecurityToken(
                issuer: _jwtSettings["validIssuer"],
                audience: _jwtSettings["validAudience"],
                claims: claims,
                expires: DateTime.Now.AddMinutes(Convert.ToDouble(_jwtSettings["expiryInMinutes"])),
                signingCredentials: signingCredentials
            );

            return tokenOptions;
        }

    }
}
