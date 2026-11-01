using System.Reflection;
using Xunit;

namespace Dw3.Disc.Tests;

public sealed class AssemblyVersionTests
{
    [Fact]
    public void DiscAssemblyExposesTheSolutionVersion()
    {
        Assembly assembly = Assembly.Load("Dw3.Disc");
        Assert.Equal(new Version(0, 1, 0, 0), assembly.GetName().Version);
    }
}
