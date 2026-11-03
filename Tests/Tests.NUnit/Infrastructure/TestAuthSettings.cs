using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;

namespace Tests.NUnit.Infrastructure;

// Fresh random credentials per test host: the app refuses to start without them and none are committed.
public sealed class TestAuthSettings
{
    public string UserName { get; } = "user-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
    public string Password { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));
    public string JwtKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

    public void Apply(IWebHostBuilder builder)
    {
        builder.UseSetting("Auth:UserName", UserName);
        builder.UseSetting("Auth:Password", Password);
        builder.UseSetting("Auth:JwtKey", JwtKey);
    }
}
