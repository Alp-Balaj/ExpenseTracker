using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ExpenseTracker.Domain.User;


namespace ExpenseTracker.API.Controllers
{

    [ApiController]
    [Route("api/[controller]")]
    public class UserController : ControllerBase
    {
        private readonly UserManager<User> _userManager;
        private readonly IConfiguration _configuration;

        public UserController(UserManager<User> userManager, IConfiguration configuration)
        {
            _userManager = userManager;
            _configuration = configuration;
        }

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

            var token = GenerateJwtToken(user);
            return Ok(new { Token = token });
        }

        private string GenerateJwtToken(User user)
        {
            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(JwtRegisteredClaimNames.UniqueName, user.UserName),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var expires = DateTime.UtcNow.AddMinutes(60);

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                expires: expires,
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private async Task<bool> CheckValidityOfUsername(string username)
        {
            return await _userManager.FindByNameAsync(username) == null;
        }

    }

    public record RegisterDto(string FirstName, string LastName, string Email, string Password);
    public record LoginDto(string Email, string Password);
}
    