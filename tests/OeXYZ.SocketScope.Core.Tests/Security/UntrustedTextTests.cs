using OeXYZ.SocketScope.Core.Security;

namespace OeXYZ.SocketScope.Core.Tests.Security;

public sealed class UntrustedTextTests
{
    [Fact]
    public void ForDisplay_EscapesControlAndBidirectionalCharacters()
    {
        string result = UntrustedText.ForDisplay("safe\nname\u202E.exe");

        Assert.Equal("safe\\u000Aname\\u202E.exe", result);
    }

    [Fact]
    public void ForDisplay_TruncatesByUnicodeScalarValue()
    {
        string result = UntrustedText.ForDisplay("A😀B", maximumRunes: 2);

        Assert.Equal("A😀…", result);
    }
}
