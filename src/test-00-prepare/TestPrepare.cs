namespace test_00_prepare
{
    [TestClass]
    public sealed class Test00Prepare
    {
        [TestMethod(DisplayName = "T0.1.1 TestWelcome")]
        public void TestWelcome()
        {
            bool isEnvironmentReady = true;
            Assert.IsTrue(isEnvironmentReady, "Welcome to your dotnet-workshop journey!");
        }
    }
}
