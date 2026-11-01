using Dw3.Formats.Field;
using Xunit;

namespace Dw3.Formats.Tests.Field;

public sealed class FieldOverlayClassifierTests
{
    [Fact]
    public void RejectsOverlayWhoseRawBytesCannotFormMipsWords()
    {
        Assert.False(FieldOverlayClassifier.IsLoadable(new byte[6]));
    }

    [Fact]
    public void AcceptsWordAlignedOverlayBytes()
    {
        Assert.True(FieldOverlayClassifier.IsLoadable(new byte[8]));
    }
}
