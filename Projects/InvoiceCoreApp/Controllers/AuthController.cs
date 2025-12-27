using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace InvoiceCoreApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginRequest request)
        {
            // ReSharper disable once InvertIf
            if (request is { Username: "Admin", Password: "edanziger" })
            {
                const string key = "super_secret_jwt_key_12345_super_secret_key";
                var tokenHandler = new JwtSecurityTokenHandler();
                var tokenDescriptor = new SecurityTokenDescriptor
                {
                    Subject = new ClaimsIdentity([
                        new Claim(ClaimTypes.Name, request.Username), new Claim(ClaimTypes.Role, "Admin")
                    ]),
                    Expires = DateTime.UtcNow.AddHours(1),
                    SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
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