using System.Reflection;
using Xunit;

namespace Dw3.Formats.Tests;

public sealed class AssemblyVersionTests
{
    [Fact]
    public void FormatsAssemblyExposesTheSolutionVersion()
    {
        Assembly assembly = Assembly.Load("Dw3.Formats");
        Assert.Equal(new Version(0, 1, 0, 0), assembly.GetName().Version);
    }
}
