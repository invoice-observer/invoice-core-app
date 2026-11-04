using Microsoft.Data.SqlClient;

namespace InvoiceCoreApp.Services;

// The password is a secret: user-secrets in development, environment variables / a secret store elsewhere; never appsettings.json.
public static class DatabaseSettings
{
    public static string BuildConnectionString(IConfiguration configuration)
    {
        var builder = new SqlConnectionStringBuilder(Required(configuration, "ConnectionStrings:InvoiceDb"))
        {
            Password = Required(configuration, "Database:Password")
        };
        return builder.ConnectionString;
    }

    private static string Required(IConfiguration configuration, string name) =>
        configuration[name] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Missing required configuration value '{name}'. " +
                                                  "Set it with 'dotnet user-secrets' in development or an environment variable.");
}
