namespace OeXYZ.SocketScope.Updater;

public enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    NoPublishedRelease,
    UnsupportedRuntime,
    VerificationBlocked,
}

public sealed record ReleaseAsset(
    long Id,
    string Name,
    long Size,
    string Sha256,
    Uri ApiUrl,
    Uri BrowserDownloadUrl);

public sealed record UpdateRelease(
    ReleaseVersion Version,
    string Tag,
    Uri ReleasePage,
    DateTimeOffset PublishedAt,
    bool IsImmutable,
    ReleaseAsset Asset);

public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    ReleaseVersion CurrentVersion,
    string RuntimeIdentifier,
    UpdateRelease? Release,
    string MessageCode);

public sealed record StagedUpdate(
    UpdateRelease Release,
    string PackagePath,
    string VerifiedSha256);

public sealed class UpdateSecurityException(string messageCode, string message) : IOException(message)
{
    public string MessageCode { get; } = messageCode;
}
