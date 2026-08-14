using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(OeXYZ.SocketScope.UiSmokeTests.TestAppBuilder))]

namespace OeXYZ.SocketScope.UiSmokeTests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<OeXYZ.SocketScope.Desktop.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }
}
