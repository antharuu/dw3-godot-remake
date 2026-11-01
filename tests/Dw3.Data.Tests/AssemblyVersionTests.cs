using System.Reflection;
using Xunit;

namespace Dw3.Data.Tests;

public sealed class AssemblyVersionTests
{
    [Fact]
    public void DataAssemblyExposesTheSolutionVersion()
    {
        Assembly assembly = Assembly.Load("Dw3.Data");
        Assert.Equal(new Version(0, 1, 0, 0), assembly.GetName().Version);
    }
}
