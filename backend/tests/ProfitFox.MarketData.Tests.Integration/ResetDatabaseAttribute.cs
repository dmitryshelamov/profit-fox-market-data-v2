using NUnit.Framework.Interfaces;

using ProfitFox.MarketData.Tests.Integration;

[assembly: ResetDatabase]

namespace ProfitFox.MarketData.Tests.Integration;

/// <summary>Empties the test database before every test of the assembly, so no test sees the rows of another.</summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class ResetDatabaseAttribute : Attribute, ITestAction
{
    public ActionTargets Targets => ActionTargets.Test;

    public void BeforeTest(ITest test)
    {
        IntegrationEnvironment.ResetDatabase();
    }

    public void AfterTest(ITest test)
    {
    }
}