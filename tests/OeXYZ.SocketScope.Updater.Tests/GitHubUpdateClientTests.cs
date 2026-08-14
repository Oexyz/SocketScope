using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace OeXYZ.SocketScope.Updater.Tests;

public sealed class GitHubUpdateClientTests
{
    [Fact]
    public async Task CheckAsync_AcceptsOneExpectedAssetFromImmutableStableRelease()
    {
        byte[] package = Encoding.UTF8.GetBytes("verified package bytes");
        using HttpClient http = CreateHttpClient((request, _) =>
        {
            Assert.Equal(
                "https://api.github.com/repos/Oexyz/SocketScope/releases/latest",
                request.RequestUri?.AbsoluteUri);
            return JsonResponse(CreateReleaseJson(package));
        });
        using GitHubUpdateClient client = new(http);

        UpdateCheckResult result = await client.CheckAsync(
            new ReleaseVersion(1, 0, 0),
            "win-x64",
            TestContext.Current.CancellationToken);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal(new ReleaseVersion(1, 1, 0), result.Release?.Version);
        Assert.True(result.Release?.IsImmutable);
        Assert.Equal(Sha256(package), result.Release?.Asset.Sha256);
    }

    [Fact]
    public async Task CheckAsync_BlocksMutableRelease()
    {
        byte[] package = Encoding.UTF8.GetBytes("package");
        using HttpClient http = CreateHttpClient((_, _) =>
            JsonResponse(CreateReleaseJson(package, immutable: false)));
        using GitHubUpdateClient client = new(http);

        UpdateCheckResult result = await client.CheckAsync(
            new ReleaseVersion(1, 0, 0),
            "win-x64",
            TestContext.Current.CancellationToken);

        Assert.Equal(UpdateCheckStatus.VerificationBlocked, result.Status);
        Assert.Equal("update.release-mutable", result.MessageCode);
        Assert.Null(result.Release);
    }

    [Fact]
    public async Task CheckAsync_BlocksMalformedDigest()
    {
        byte[] package = Encoding.UTF8.GetBytes("package");
        using HttpClient http = CreateHttpClient((_, _) =>
            JsonResponse(CreateReleaseJson(package, digest: "sha256:not-a-digest")));
        using GitHubUpdateClient client = new(http);

        UpdateCheckResult result = await client.CheckAsync(
            new ReleaseVersion(1, 0, 0),
            "win-x64",
            TestContext.Current.CancellationToken);

        Assert.Equal(UpdateCheckStatus.VerificationBlocked, result.Status);
        Assert.Equal("update.digest-invalid", result.MessageCode);
    }

    [Fact]
    public async Task CheckAsync_TreatsOlderReleaseAsCurrent()
    {
        byte[] package = Encoding.UTF8.GetBytes("package");
        using HttpClient http = CreateHttpClient((_, _) => JsonResponse(CreateReleaseJson(package)));
        using GitHubUpdateClient client = new(http);

        UpdateCheckResult result = await client.CheckAsync(
            new ReleaseVersion(2, 0, 0),
            "win-x64",
            TestContext.Current.CancellationToken);

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
        Assert.Null(result.Release);
    }

    [Fact]
    public async Task DownloadVerifiedAsync_WritesOnlyMatchingCompletePackage()
    {
        byte[] package = Encoding.UTF8.GetBytes("immutable release package");
        using HttpClient http = CreateHttpClient((request, _) =>
        {
            Assert.Equal(
                "https://api.github.com/repos/Oexyz/SocketScope/releases/assets/42",
                request.RequestUri?.AbsoluteUri);
            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(package),
            };
            response.Content.Headers.ContentType = new("application/octet-stream");
            return response;
        });
        using GitHubUpdateClient client = new(http);
        using TestDirectory directory = new();
        UpdateRelease release = CreateRelease(package);

        StagedUpdate staged = await client.DownloadVerifiedAsync(
            release,
            directory.Path,
            TestContext.Current.CancellationToken);

        Assert.Equal(package, await File.ReadAllBytesAsync(staged.PackagePath, TestContext.Current.CancellationToken));
        Assert.Equal(Sha256(package), staged.VerifiedSha256);
        Assert.Single(Directory.EnumerateFiles(directory.Path));
    }

    [Fact]
    public async Task DownloadVerifiedAsync_RemovesPartialFileAfterDigestMismatch()
    {
        byte[] expected = Encoding.UTF8.GetBytes("expected");
        byte[] malicious = Encoding.UTF8.GetBytes("tampered");
        using HttpClient http = CreateHttpClient((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(malicious),
        });
        using GitHubUpdateClient client = new(http);
        using TestDirectory directory = new();
        UpdateRelease release = CreateRelease(expected) with
        {
            Asset = CreateRelease(expected).Asset with { Size = malicious.Length },
        };

        UpdateSecurityException exception = await Assert.ThrowsAsync<UpdateSecurityException>(
            () => client.DownloadVerifiedAsync(
                release,
                directory.Path,
                TestContext.Current.CancellationToken));

        Assert.Equal("update.digest-mismatch", exception.MessageCode);
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory.Path));
    }

    [Fact]
    public async Task DownloadVerifiedAsync_RejectsRedirectOutsideGitHubAssetHosts()
    {
        byte[] package = Encoding.UTF8.GetBytes("package");
        using HttpClient http = CreateHttpClient((_, _) =>
        {
            HttpResponseMessage response = new(HttpStatusCode.Found);
            response.Headers.Location = new Uri("https://attacker.invalid/package.exe");
            return response;
        });
        using GitHubUpdateClient client = new(http);
        using TestDirectory directory = new();

        UpdateSecurityException exception = await Assert.ThrowsAsync<UpdateSecurityException>(
            () => client.DownloadVerifiedAsync(
                CreateRelease(package),
                directory.Path,
                TestContext.Current.CancellationToken));

        Assert.Equal("update.download-origin-invalid", exception.MessageCode);
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory.Path));
    }

    private static HttpClient CreateHttpClient(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
    {
        return new HttpClient(new StubHttpMessageHandler(responder), disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    private static string CreateReleaseJson(
        byte[] package,
        bool immutable = true,
        string? digest = null)
    {
        return JsonSerializer.Serialize(new
        {
            tag_name = "v1.1.0",
            html_url = "https://github.com/Oexyz/SocketScope/releases/tag/v1.1.0",
            published_at = "2026-08-13T10:00:00Z",
            draft = false,
            prerelease = false,
            immutable,
            assets = new[]
            {
                new
                {
                    id = 42,
                    name = "OeXYZ-SocketScope-v1.1.0-win-x64.exe",
                    state = "uploaded",
                    size = package.LongLength,
                    digest = digest ?? $"sha256:{Sha256(package)}",
                    url = "https://api.github.com/repos/Oexyz/SocketScope/releases/assets/42",
                    browser_download_url = "https://github.com/Oexyz/SocketScope/releases/download/v1.1.0/OeXYZ-SocketScope-v1.1.0-win-x64.exe",
                },
            },
        });
    }

    private static UpdateRelease CreateRelease(byte[] package)
    {
        ReleaseVersion version = new(1, 1, 0);
        return new UpdateRelease(
            version,
            "v1.1.0",
            new Uri("https://github.com/Oexyz/SocketScope/releases/tag/v1.1.0"),
            DateTimeOffset.Parse("2026-08-13T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            true,
            new ReleaseAsset(
                42,
                RuntimeAssetNames.GetPackageName(version, "win-x64"),
                package.LongLength,
                Sha256(package),
                new Uri("https://api.github.com/repos/Oexyz/SocketScope/releases/assets/42"),
                new Uri("https://github.com/Oexyz/SocketScope/releases/download/v1.1.0/OeXYZ-SocketScope-v1.1.0-win-x64.exe")));
    }

    private static string Sha256(byte[] content)
    {
        return Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(responder(request, cancellationToken));
        }
    }

    private sealed class TestDirectory : IDisposable
    {
        public TestDirectory()
        {
            Path = System.IO.Path.Combine(
                AppContext.BaseDirectory,
                "update-test-output",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
