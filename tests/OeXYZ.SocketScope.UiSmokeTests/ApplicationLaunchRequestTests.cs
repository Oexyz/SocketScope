using OeXYZ.SocketScope.Desktop;

namespace OeXYZ.SocketScope.UiSmokeTests;

public sealed class ApplicationLaunchRequestTests
{
    [Fact]
    public void Parse_NoArguments_OpensDesktop()
    {
        var request = ApplicationLaunchRequest.Parse([]);

        Assert.Equal(ApplicationLaunchMode.Desktop, request.Mode);
        Assert.Empty(request.Arguments);
    }

    [Theory]
    [InlineData("list")]
    [InlineData("watch")]
    [InlineData("snapshot")]
    [InlineData("report")]
    public void Parse_DirectCliCommand_UsesIntegratedCli(string command)
    {
        var request = ApplicationLaunchRequest.Parse([command, "--help"]);

        Assert.Equal(ApplicationLaunchMode.Cli, request.Mode);
        Assert.Equal([command, "--help"], request.Arguments);
    }

    [Fact]
    public void Parse_ExplicitCli_RemovesDispatcherToken()
    {
        var request = ApplicationLaunchRequest.Parse(["cli", "--language", "de", "list"]);

        Assert.Equal(ApplicationLaunchMode.Cli, request.Mode);
        Assert.Equal(["--language", "de", "list"], request.Arguments);
    }

    [Fact]
    public void Parse_Update_RemovesDispatcherToken()
    {
        var request = ApplicationLaunchRequest.Parse(["update", "--help"]);

        Assert.Equal(ApplicationLaunchMode.Update, request.Mode);
        Assert.Equal(["--help"], request.Arguments);
    }

    [Fact]
    public void Parse_Shell_RemovesDispatcherToken()
    {
        var request = ApplicationLaunchRequest.Parse(["shell", "install"]);

        Assert.Equal(ApplicationLaunchMode.Shell, request.Mode);
        Assert.Equal(["install"], request.Arguments);
    }

    [Fact]
    public void Parse_Gui_OpensDesktopWithoutForwardingCommand()
    {
        var request = ApplicationLaunchRequest.Parse(["gui"]);

        Assert.Equal(ApplicationLaunchMode.Desktop, request.Mode);
        Assert.Empty(request.Arguments);
    }

    [Fact]
    public void Parse_Portable_RemainsDesktopArgument()
    {
        var request = ApplicationLaunchRequest.Parse(["--portable"]);

        Assert.Equal(ApplicationLaunchMode.Desktop, request.Mode);
        Assert.Equal(["--portable"], request.Arguments);
    }

    [Fact]
    public void Parse_Notices_UsesEmbeddedLegalDocuments()
    {
        var request = ApplicationLaunchRequest.Parse(["--notices"]);

        Assert.Equal(ApplicationLaunchMode.Notices, request.Mode);
        Assert.Empty(request.Arguments);
    }
}
