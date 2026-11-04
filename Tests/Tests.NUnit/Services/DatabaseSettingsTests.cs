using System.Security.Cryptography;
using InvoiceCoreApp.Services;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;

namespace Tests.NUnit.Services
{
    [TestFixture]
    public class DatabaseSettingsTests
    {
        private const string ConnectionStringKey = "ConnectionStrings:InvoiceDb";
        private const string PasswordKey = "Database:Password";
        private const string BaseConnectionString =
            "Server=localhost,1433;Database=InvoiceDb;User Id=sa;TrustServerCertificate=True;Encrypt=False;Application Name=InvoiceCoreApp";

        // Random per run: no credentials are committed. The ';' and '=' check that the builder quotes the value.
        private static readonly string Password = "p;w=" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8));

        private static IConfiguration Configuration(string? connectionString, string? password) =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [ConnectionStringKey] = connectionString,
                    [PasswordKey] = password
                })
                .Build();

        [Test]
        public void BuildConnectionString_SetsPasswordFromConfiguration()
        {
            var result = DatabaseSettings.BuildConnectionString(Configuration(BaseConnectionString, Password));

            Assert.That(new SqlConnectionStringBuilder(result).Password, Is.EqualTo(Password));
        }

        [Test]
        public void BuildConnectionString_KeepsOtherPartsUnchanged()
        {
            var result = new SqlConnectionStringBuilder(
                DatabaseSettings.BuildConnectionString(Configuration(BaseConnectionString, Password)));
            var expected = new SqlConnectionStringBuilder(BaseConnectionString) { Password = Password };

            Assert.Multiple(() =>
            {
                Assert.That(result.DataSource, Is.EqualTo("localhost,1433"));
                Assert.That(result.InitialCatalog, Is.EqualTo("InvoiceDb"));
                Assert.That(result.UserID, Is.EqualTo("sa"));
                Assert.That(result.TrustServerCertificate, Is.True);
                Assert.That(result.ApplicationName, Is.EqualTo("InvoiceCoreApp"));
                Assert.That(result.ConnectionString, Is.EqualTo(expected.ConnectionString));
            });
        }

        [TestCase(ConnectionStringKey, null)]
        [TestCase(ConnectionStringKey, "")]
        [TestCase(PasswordKey, null)]
        [TestCase(PasswordKey, "")]
        public void BuildConnectionString_MissingOrEmptyValue_ThrowsNamingTheKey(string key, string? value)
        {
            var connectionString = key == ConnectionStringKey ? value : BaseConnectionString;
            var password = key == PasswordKey ? value : Password;

            var ex = Assert.Throws<InvalidOperationException>(
                () => DatabaseSettings.BuildConnectionString(Configuration(connectionString, password)));

            Assert.That(ex!.Message, Does.Contain(key));
        }

        [Test]
        public void BuildConnectionString_KeyAbsentFromConfiguration_ThrowsNamingTheKey()
        {
            var configuration = new ConfigurationBuilder().Build();

            var ex = Assert.Throws<InvalidOperationException>(() => DatabaseSettings.BuildConnectionString(configuration));

            Assert.That(ex!.Message, Does.Contain(ConnectionStringKey));
        }
    }
}
