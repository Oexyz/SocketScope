using System.Buffers;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace OeXYZ.SocketScope.Updater;

public sealed class GitHubUpdateClient : IDisposable
{
    public const string RepositoryOwner = "Oexyz";
    public const string RepositoryName = "SocketScope";
    public const long MaximumPackageBytes = 512L * 1024 * 1024;

    private const int MaximumMetadataBytes = 2 * 1024 * 1024;
    private const int MaximumAssets = 128;
    private const int MaximumRedirects = 5;
    private static readonly Uri LatestReleaseUri = new(
        $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases/latest");
    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;
    private readonly string userAgentVersion;
    private bool disposed;

    public GitHubUpdateClient(HttpClient httpClient, string userAgentVersion = "1.0.0")
        : this(httpClient, userAgentVersion, ownsHttpClient: false)
    {
    }

    private GitHubUpdateClient(HttpClient httpClient, string userAgentVersion, bool ownsHttpClient)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.userAgentVersion = ValidateUserAgentVersion(userAgentVersion);
        this.ownsHttpClient = ownsHttpClient;
    }

    public static GitHubUpdateClient CreateDefault(string userAgentVersion)
    {
        SocketsHttpHandler handler = new()
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            SslOptions = new SslClientAuthenticationOptions
            {
                CertificateRevocationCheckMode = X509RevocationMode.Online,
            },
        };
        HttpClient client = new(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        return new GitHubUpdateClient(client, userAgentVersion, ownsHttpClient: true);
    }

    public async Task<UpdateCheckResult> CheckAsync(
        ReleaseVersion currentVersion,
        string runtimeIdentifier,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeIdentifier);
        if (!RuntimeAssetNames.IsSupported(runtimeIdentifier))
        {
            return new UpdateCheckResult(
                UpdateCheckStatus.UnsupportedRuntime,
                currentVersion,
                runtimeIdentifier,
                null,
                "update.runtime-unsupported");
        }

        using HttpRequestMessage request = CreateRequest(LatestReleaseUri, "application/vnd.github+json");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using HttpResponseMessage response = await httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new UpdateCheckResult(
                UpdateCheckStatus.NoPublishedRelease,
                currentVersion,
                runtimeIdentifier,
                null,
                "update.no-release");
        }

        response.EnsureSuccessStatusCode();
        byte[] metadata = await ReadBoundedAsync(
            response.Content,
            MaximumMetadataBytes,
            "update.metadata-too-large",
            timeout.Token).ConfigureAwait(false);
        try
        {
            using var document = JsonDocument.Parse(metadata, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32,
            });
            ParsedRelease parsed = ParseRelease(document.RootElement);
            if (parsed.IsDraft || parsed.IsPrerelease)
            {
                return Blocked(currentVersion, runtimeIdentifier, "update.release-not-stable");
            }

            if (parsed.Version.CompareTo(currentVersion) <= 0)
            {
                return new UpdateCheckResult(
                    UpdateCheckStatus.UpToDate,
                    currentVersion,
                    runtimeIdentifier,
                    null,
                    "update.current");
            }

            if (!parsed.IsImmutable)
            {
                return Blocked(currentVersion, runtimeIdentifier, "update.release-mutable");
            }

            string expectedName = RuntimeAssetNames.GetPackageName(parsed.Version, runtimeIdentifier);
            JsonElement[] matchingAssets = [.. parsed.Assets.Where(asset => string.Equals(GetRequiredString(asset, "name"), expectedName, StringComparison.Ordinal))];
            if (matchingAssets.Length != 1)
            {
                return Blocked(currentVersion, runtimeIdentifier, "update.asset-missing-or-duplicate");
            }

            ReleaseAsset asset = ParseAsset(matchingAssets[0], expectedName, parsed.Tag);
            UpdateRelease release = new(
                parsed.Version,
                parsed.Tag,
                parsed.ReleasePage,
                parsed.PublishedAt,
                parsed.IsImmutable,
                asset);
            return new UpdateCheckResult(
                UpdateCheckStatus.UpdateAvailable,
                currentVersion,
                runtimeIdentifier,
                release,
                "update.available");
        }
        catch (UpdateSecurityException exception)
        {
            return Blocked(currentVersion, runtimeIdentifier, exception.MessageCode);
        }
        catch (JsonException)
        {
            return Blocked(currentVersion, runtimeIdentifier, "update.metadata-invalid");
        }
    }

    public async Task<StagedUpdate> DownloadVerifiedAsync(
        UpdateRelease release,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(release);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ValidateReleaseForDownload(release);

        string directory = PrepareOutputDirectory(outputDirectory);
        string destination = GetDestinationPath(directory, release.Asset.Name);
        if (File.Exists(destination) || Directory.Exists(destination))
        {
            throw new IOException("The verified update destination already exists.");
        }

        string temporary = Path.Combine(
            directory,
            $".{release.Asset.Name}.{Guid.NewGuid():N}.partial");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(15));
            using HttpResponseMessage response = await SendAssetRequestAsync(release, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long contentLength
                && contentLength != release.Asset.Size)
            {
                throw new UpdateSecurityException(
                    "update.size-mismatch",
                    "The release asset length differs from immutable GitHub metadata.");
            }

            if (response.Content.Headers.ContentEncoding.Count > 0)
            {
                throw new UpdateSecurityException(
                    "update.encoded-asset-rejected",
                    "Encoded release asset responses are not accepted for digest verification.");
            }

            FileStreamOptions streamOptions = new()
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                BufferSize = 128 * 1024,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough,
            };
            if (!OperatingSystem.IsWindows())
            {
                streamOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            await using FileStream output = new(temporary, streamOptions);
            await using Stream input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
            long written = 0;
            try
            {
                while (true)
                {
                    int read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), timeout.Token).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    written = checked(written + read);
                    if (written > release.Asset.Size || written > MaximumPackageBytes)
                    {
                        throw new UpdateSecurityException(
                            "update.asset-too-large",
                            "The release asset exceeded its verified size limit.");
                    }

                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token).ConfigureAwait(false);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            if (written != release.Asset.Size)
            {
                throw new UpdateSecurityException(
                    "update.size-mismatch",
                    "The downloaded release asset size differs from immutable GitHub metadata.");
            }

            byte[] actualDigest = hash.GetHashAndReset();
            byte[] expectedDigest = Convert.FromHexString(release.Asset.Sha256);
            if (!CryptographicOperations.FixedTimeEquals(actualDigest, expectedDigest))
            {
                throw new UpdateSecurityException(
                    "update.digest-mismatch",
                    "The downloaded release asset failed SHA-256 verification.");
            }

            await output.FlushAsync(timeout.Token).ConfigureAwait(false);
            output.Flush(flushToDisk: true);
            output.Close();
            File.Move(temporary, destination, overwrite: false);
            SetPrivateFileModeIfSupported(destination);
            return new StagedUpdate(release, destination, release.Asset.Sha256);
        }
        catch
        {
            TryDeleteOwnedTemporaryFile(temporary);
            throw;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (ownsHttpClient)
        {
            httpClient.Dispose();
        }
    }

    private static ParsedRelease ParseRelease(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new UpdateSecurityException("update.metadata-invalid", "The GitHub release response is not an object.");
        }

        string tag = GetRequiredString(root, "tag_name");
        if (!ReleaseVersion.TryParse(tag, out ReleaseVersion version))
        {
            throw new UpdateSecurityException("update.version-invalid", "The GitHub release tag is not a stable SocketScope version.");
        }

        Uri releasePage = GetRequiredHttpsUri(root, "html_url");
        Uri expectedPage = new($"https://github.com/{RepositoryOwner}/{RepositoryName}/releases/tag/v{version}");
        if (releasePage != expectedPage)
        {
            throw new UpdateSecurityException("update.release-origin-invalid", "The release page does not belong to the fixed SocketScope repository.");
        }

        string published = GetRequiredString(root, "published_at");
        if (!DateTimeOffset.TryParse(
                published,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset publishedAt))
        {
            throw new UpdateSecurityException("update.published-at-invalid", "The release publication timestamp is invalid.");
        }

        JsonElement assetsElement = GetRequiredProperty(root, "assets");
        if (assetsElement.ValueKind != JsonValueKind.Array || assetsElement.GetArrayLength() > MaximumAssets)
        {
            throw new UpdateSecurityException("update.assets-invalid", "The release asset collection is invalid or too large.");
        }

        return new ParsedRelease(
            version,
            tag,
            releasePage,
            publishedAt,
            GetRequiredBoolean(root, "draft"),
            GetRequiredBoolean(root, "prerelease"),
            GetRequiredBoolean(root, "immutable"),
            [.. assetsElement.EnumerateArray()]);
    }

    private static ReleaseAsset ParseAsset(JsonElement element, string expectedName, string tag)
    {
        if (!string.Equals(GetRequiredString(element, "state"), "uploaded", StringComparison.Ordinal))
        {
            throw new UpdateSecurityException("update.asset-incomplete", "The selected release asset is not fully uploaded.");
        }

        long id = GetRequiredInt64(element, "id");
        long size = GetRequiredInt64(element, "size");
        if (id <= 0 || size <= 0 || size > MaximumPackageBytes)
        {
            throw new UpdateSecurityException("update.asset-size-invalid", "The selected release asset has an invalid size.");
        }

        string digest = NormalizeSha256(GetRequiredString(element, "digest"));
        Uri apiUrl = GetRequiredHttpsUri(element, "url");
        Uri expectedApiUrl = new($"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases/assets/{id.ToString(CultureInfo.InvariantCulture)}");
        if (apiUrl != expectedApiUrl)
        {
            throw new UpdateSecurityException("update.asset-origin-invalid", "The release asset API URL does not belong to SocketScope.");
        }

        Uri browserUrl = GetRequiredHttpsUri(element, "browser_download_url");
        Uri expectedBrowserUrl = new($"https://github.com/{RepositoryOwner}/{RepositoryName}/releases/download/{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(expectedName)}");
        if (browserUrl != expectedBrowserUrl)
        {
            throw new UpdateSecurityException("update.asset-origin-invalid", "The release download URL does not belong to SocketScope.");
        }

        return new ReleaseAsset(id, expectedName, size, digest, apiUrl, browserUrl);
    }

    private async Task<HttpResponseMessage> SendAssetRequestAsync(
        UpdateRelease release,
        CancellationToken cancellationToken)
    {
        Uri current = release.Asset.ApiUrl;
        for (int redirect = 0; redirect <= MaximumRedirects; redirect++)
        {
            ValidateDownloadUri(current, release);
            using HttpRequestMessage request = CreateRequest(current, "application/octet-stream");
            HttpResponseMessage response = await httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            Uri effective = response.RequestMessage?.RequestUri ?? current;
            try
            {
                ValidateDownloadUri(effective, release);
                if (!IsRedirect(response.StatusCode))
                {
                    return response;
                }

                if (redirect == MaximumRedirects || response.Headers.Location is null)
                {
                    throw new UpdateSecurityException(
                        "update.redirect-invalid",
                        "The release asset redirect chain is missing or too long.");
                }

                current = response.Headers.Location.IsAbsoluteUri
                    ? response.Headers.Location
                    : new Uri(effective, response.Headers.Location);
            }
            catch
            {
                response.Dispose();
                throw;
            }

            response.Dispose();
        }

        throw new UpdateSecurityException("update.redirect-invalid", "The release asset redirect chain is too long.");
    }

    private HttpRequestMessage CreateRequest(Uri uri, string accept)
    {
        HttpRequestMessage request = new(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        request.Headers.UserAgent.ParseAdd($"OeXYZ-SocketScope-Updater/{userAgentVersion}");
        request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
        return request;
    }

    private static void ValidateReleaseForDownload(UpdateRelease release)
    {
        if (!release.IsImmutable)
        {
            throw new UpdateSecurityException("update.release-mutable", "Mutable releases cannot be accepted by the updater.");
        }

        string expectedName = RuntimeAssetNames.GetPackageName(
            release.Version,
            ExtractRuntimeIdentifier(release.Asset.Name, release.Version));
        if (!string.Equals(release.Asset.Name, expectedName, StringComparison.Ordinal)
            || release.Asset.Size <= 0
            || release.Asset.Size > MaximumPackageBytes)
        {
            throw new UpdateSecurityException("update.asset-invalid", "The release asset metadata is invalid.");
        }

        _ = NormalizeSha256($"sha256:{release.Asset.Sha256}");
        ValidateDownloadUri(release.Asset.ApiUrl, release);
        if (release.Asset.BrowserDownloadUrl.Scheme != Uri.UriSchemeHttps
            || !string.Equals(release.Asset.BrowserDownloadUrl.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdateSecurityException("update.asset-origin-invalid", "The release asset URL is not a trusted GitHub URL.");
        }
    }

    private static string ExtractRuntimeIdentifier(string assetName, ReleaseVersion version)
    {
        string prefix = $"OeXYZ-SocketScope-v{version}-";
        if (!assetName.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new UpdateSecurityException("update.asset-name-invalid", "The release asset name is invalid.");
        }

        string value = assetName[prefix.Length..];
        if (value.EndsWith(".tar.gz", StringComparison.Ordinal))
        {
            value = value[..^7];
        }
        else if (value.EndsWith(".exe", StringComparison.Ordinal))
        {
            value = value[..^4];
        }
        else
        {
            throw new UpdateSecurityException("update.asset-name-invalid", "The release asset extension is invalid.");
        }

        if (!RuntimeAssetNames.IsSupported(value))
        {
            throw new UpdateSecurityException("update.runtime-unsupported", "The release asset runtime is unsupported.");
        }

        return value;
    }

    private static void ValidateDownloadUri(Uri uri, UpdateRelease release)
    {
        if (!uri.IsAbsoluteUri
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.IsDefaultPort
            || uri.UserInfo.Length != 0
            || uri.Fragment.Length != 0)
        {
            throw new UpdateSecurityException("update.download-origin-invalid", "The download URI is not a safe HTTPS URI.");
        }

        string host = uri.IdnHost;
        if (string.Equals(host, "api.github.com", StringComparison.OrdinalIgnoreCase))
        {
            if (uri != release.Asset.ApiUrl)
            {
                throw new UpdateSecurityException("update.download-origin-invalid", "The GitHub API redirect target is unexpected.");
            }

            return;
        }

        if (string.Equals(host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            if (uri.GetLeftPart(UriPartial.Path) != release.Asset.BrowserDownloadUrl.GetLeftPart(UriPartial.Path))
            {
                throw new UpdateSecurityException("update.download-origin-invalid", "The GitHub release redirect target is unexpected.");
            }

            return;
        }

        if (!string.Equals(host, "release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(host, "objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdateSecurityException("update.download-origin-invalid", "The release asset redirect left the GitHub download service.");
        }
    }

    private static string PrepareOutputDirectory(string outputDirectory)
    {
        string fullPath = Path.GetFullPath(outputDirectory);
        string? root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(root)
            || string.Equals(
                Path.TrimEndingDirectorySeparator(fullPath),
                Path.TrimEndingDirectorySeparator(root),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new ArgumentException("The update output directory cannot be a filesystem root.", nameof(outputDirectory));
        }

        bool existed = Directory.Exists(fullPath);
        if (!existed && !OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(
                fullPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        else
        {
            Directory.CreateDirectory(fullPath);
        }

        DirectoryInfo information = new(fullPath);
        if ((information.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new UpdateSecurityException("update.output-reparse-point", "A reparse point cannot be used as the update output directory.");
        }

        return fullPath;
    }

    private static string GetDestinationPath(string directory, string assetName)
    {
        if (!string.Equals(Path.GetFileName(assetName), assetName, StringComparison.Ordinal)
            || assetName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new UpdateSecurityException("update.asset-name-invalid", "The release asset name is not a safe file name.");
        }

        string destination = Path.GetFullPath(Path.Combine(directory, assetName));
        string? parent = Path.GetDirectoryName(destination);
        if (!string.Equals(
                Path.TrimEndingDirectorySeparator(parent ?? string.Empty),
                Path.TrimEndingDirectorySeparator(directory),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new UpdateSecurityException("update.asset-name-invalid", "The release asset path escaped the update directory.");
        }

        return destination;
    }

    private static void SetPrivateFileModeIfSupported(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static void TryDeleteOwnedTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // A failed cleanup must not hide the verification failure.
        }
        catch (UnauthorizedAccessException)
        {
            // A failed cleanup must not hide the verification failure.
        }
    }

    private static UpdateCheckResult Blocked(
        ReleaseVersion currentVersion,
        string runtimeIdentifier,
        string messageCode)
    {
        return new UpdateCheckResult(
            UpdateCheckStatus.VerificationBlocked,
            currentVersion,
            runtimeIdentifier,
            null,
            messageCode);
    }

    private static string NormalizeSha256(string value)
    {
        const string prefix = "sha256:";
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || value.Length != prefix.Length + 64)
        {
            throw new UpdateSecurityException("update.digest-invalid", "The release asset has no valid SHA-256 digest.");
        }

        string digest = value[prefix.Length..];
        try
        {
            _ = Convert.FromHexString(digest);
        }
        catch (FormatException)
        {
            throw new UpdateSecurityException("update.digest-invalid", "The release asset SHA-256 digest is invalid.");
        }

        return digest.ToLowerInvariant();
    }

    private static JsonElement GetRequiredProperty(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
        {
            throw new UpdateSecurityException("update.metadata-invalid", $"The GitHub release response is missing '{name}'.");
        }

        return value;
    }

    private static string GetRequiredString(JsonElement element, string name)
    {
        JsonElement value = GetRequiredProperty(element, name);
        string? result = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        if (string.IsNullOrWhiteSpace(result) || result.Length > 2_048)
        {
            throw new UpdateSecurityException("update.metadata-invalid", $"The GitHub release field '{name}' is invalid.");
        }

        return result;
    }

    private static bool GetRequiredBoolean(JsonElement element, string name)
    {
        JsonElement value = GetRequiredProperty(element, name);
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new UpdateSecurityException("update.metadata-invalid", $"The GitHub release field '{name}' is invalid.");
        }

        return value.GetBoolean();
    }

    private static long GetRequiredInt64(JsonElement element, string name)
    {
        JsonElement value = GetRequiredProperty(element, name);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out long result))
        {
            throw new UpdateSecurityException("update.metadata-invalid", $"The GitHub release field '{name}' is invalid.");
        }

        return result;
    }

    private static Uri GetRequiredHttpsUri(JsonElement element, string name)
    {
        string value = GetRequiredString(element, name);
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.IsDefaultPort
            || uri.UserInfo.Length != 0
            || uri.Fragment.Length != 0)
        {
            throw new UpdateSecurityException("update.metadata-invalid", $"The GitHub release URI '{name}' is invalid.");
        }

        return uri;
    }

    private static async Task<byte[]> ReadBoundedAsync(
        HttpContent content,
        int maximumBytes,
        string messageCode,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is long contentLength && contentLength > maximumBytes)
        {
            throw new UpdateSecurityException(messageCode, "The HTTP response exceeded the configured size limit.");
        }

        await using Stream stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using MemoryStream result = new(Math.Min(maximumBytes, 64 * 1024));
        byte[] buffer = ArrayPool<byte>.Shared.Rent(32 * 1024);
        try
        {
            while (true)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    return result.ToArray();
                }

                if (result.Length + read > maximumBytes)
                {
                    throw new UpdateSecurityException(messageCode, "The HTTP response exceeded the configured size limit.");
                }

                result.Write(buffer, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Found
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;
    }

    private static string ValidateUserAgentVersion(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > 32 || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '-')))
        {
            throw new ArgumentException("The updater version is not safe for an HTTP user-agent.", nameof(value));
        }

        return value;
    }

    private sealed record ParsedRelease(
        ReleaseVersion Version,
        string Tag,
        Uri ReleasePage,
        DateTimeOffset PublishedAt,
        bool IsDraft,
        bool IsPrerelease,
        bool IsImmutable,
        JsonElement[] Assets);
}
