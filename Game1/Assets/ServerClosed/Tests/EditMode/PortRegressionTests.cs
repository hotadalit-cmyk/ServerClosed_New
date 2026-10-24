using NUnit.Framework;
using ServerClosed.Validation;

namespace ServerClosed.Port.Tests
{
    public class PortRegressionTests
    {
        [TestCaseSource(typeof(ScRegressionChecks), nameof(ScRegressionChecks.Cases))]
        public void Regression(string name) { ScRegressionChecks.Run(name); }
    }
}
