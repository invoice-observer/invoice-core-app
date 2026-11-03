using InvoiceCoreApp.Services;
using NUnit.Framework;

namespace Tests.NUnit.Services
{
    [TestFixture]
    public class InvoiceServiceModeTests
    {
        private const string Key = "InvoiceService";

        [TestCase("SqlServer", InvoiceServiceMode.SqlServer)]
        [TestCase("sqlserver", InvoiceServiceMode.SqlServer)]
        [TestCase("SQLSERVER", InvoiceServiceMode.SqlServer)]
        [TestCase("Mock", InvoiceServiceMode.Mock)]
        [TestCase("mock", InvoiceServiceMode.Mock)]
        [TestCase("MOCK", InvoiceServiceMode.Mock)]
        public void Parse_KnownName_IgnoresCase(string value, InvoiceServiceMode expected)
        {
            Assert.That(InvoiceServiceMode.Parse(value), Is.EqualTo(expected));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("SQLite")]
        [TestCase("sqlite")]
        [TestCase("Unknown")]
        [TestCase("0")]
        [TestCase("1")]
        [TestCase(" Mock")]
        [TestCase("SqlServer,Mock")]
        public void Parse_InvalidValue_ThrowsNamingTheKey(string? value)
        {
            var ex = Assert.Throws<InvalidOperationException>(() => InvoiceServiceMode.Parse(value));

            Assert.That(ex!.Message, Does.Contain(Key));
        }
    }
}
