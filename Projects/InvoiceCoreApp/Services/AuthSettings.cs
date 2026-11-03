using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace InvoiceCoreApp.Services;

// Secrets: user-secrets in development, environment variables / a secret store elsewhere; never appsettings.json.
public sealed class AuthSettings
{
    private const int MinJwtKeyBytes = 32; // HMAC-SHA256 needs a key of at least 256 bits

    public required string UserName { get; init; }
    public required string Password { get; init; }
    public required string JwtKey { get; init; }

    public SymmetricSecurityKey SigningKey => new(Encoding.UTF8.GetBytes(JwtKey));

    public static AuthSettings FromConfiguration(IConfiguration configuration)
    {
        var settings = new AuthSettings
        {
            UserName = Required(configuration, "Auth:UserName"),
            Password = Required(configuration, "Auth:Password"),
            JwtKey = Required(configuration, "Auth:JwtKey")
        };
        // ReSharper disable once ConvertIfStatementToReturnStatement
        if (Encoding.UTF8.GetByteCount(settings.JwtKey) < MinJwtKeyBytes)
            throw new InvalidOperationException($"Configuration value 'Auth:JwtKey' must be at least {MinJwtKeyBytes} bytes.");
        return settings;
    }

    public bool Matches(string userName, string password) =>
        userName == UserName && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(password), Encoding.UTF8.GetBytes(Password));

    private static string Required(IConfiguration configuration, string name) =>
        configuration[name] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Missing required configuration value '{name}'. " +
                                                  "Set it with 'dotnet user-secrets' in development or an environment variable.");
}
