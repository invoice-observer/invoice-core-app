using NUnit.Framework;

[assembly: Parallelizable(ParallelScope.Fixtures)]

namespace Tests.NUnit
{
    [SetUpFixture]
    public class GlobalTestSetup
    {
        [OneTimeSetUp]
        public void RunBeforeAnyTests()
        {
            // Global test setup
            Console.WriteLine("Starting NUnit Tests for Invoice Core Application");
            Console.WriteLine($"Test execution started at: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        }

        [OneTimeTearDown]
        public void RunAfterAllTests()
        {
            // Global test cleanup
            Console.WriteLine($"Test execution completed at: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        }
    }
}