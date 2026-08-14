using OeXYZ.SocketScope.Core.Diagnostics;
using OeXYZ.SocketScope.Core.Domain;
using OeXYZ.SocketScope.Platform.Linux.Processes;

namespace OeXYZ.SocketScope.Platform.Linux.Tests.Processes;

public sealed class ProcProcessStatParserTests
{
    [Fact]
    public void TryParse_HandlesSpacesAndClosingParenthesesInName()
    {
        const string stat = "42 (odd ) process) S 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 987654 20";

        bool success = ProcProcessStatParser.TryParse(stat, 42, out string name, out ulong startTicks);

        Assert.True(success);
        Assert.Equal("odd ) process", name);
        Assert.Equal(987654UL, startTicks);
    }

    [Theory]
    [InlineData("42 no-parentheses")]
    [InlineData("41 (wrong pid) S 1 2 3")]
    [InlineData("42 (short) S 1 2 3")]
    public void TryParse_RejectsMalformedOrWrongProcess(string stat)
    {
        Assert.False(ProcProcessStatParser.TryParse(stat, 42, out _, out _));
    }

    [Fact]
    public async Task ProcessProvider_ReportsAProcessThatDisappearedDuringCollection()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "socketscope-missing-process-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            LinuxProcessMetadataProvider provider = new(root);

            IReadOnlyDictionary<int, ProcessMetadata> result = await provider.GetProcessesAsync(
                new HashSet<int> { 4242 },
                TestContext.Current.CancellationToken);

            ProcessMetadata process = Assert.Single(result).Value;
            Assert.Equal(DataAvailability.Partial, process.Availability);
            Assert.Equal(DiagnosticCodes.ProcessDisappeared, process.ReasonCode);
            Assert.Null(process.Name);
            Assert.Null(process.ExecutablePath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
