using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using InvoiceCoreApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace InvoiceCoreApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController(AuthSettings authSettings) : ControllerBase
    {
        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginRequest request)
        {
            // ReSharper disable once InvertIf
            if (authSettings.Matches(request.Username, request.Password))
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var tokenDescriptor = new SecurityTokenDescriptor
                {
                    Subject = new ClaimsIdentity([
                        new Claim(ClaimTypes.Name, request.Username), new Claim(ClaimTypes.Role, "Admin")
                    ]),
                    Expires = DateTime.UtcNow.AddHours(1),
                    SigningCredentials = new SigningCredentials(authSettings.SigningKey,
                        SecurityAlgorithms.HmacSha256Signature)
                };
                var token = tokenHandler.CreateToken(tokenDescriptor);
                var jwt = tokenHandler.WriteToken(token);
                return Ok(new { token = jwt });
            }

            return Unauthorized();
        }
    }

    public class LoginRequest
    {
        // ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        // ReSharper restore AutoPropertyCanBeMadeGetOnly.Global
    }
}