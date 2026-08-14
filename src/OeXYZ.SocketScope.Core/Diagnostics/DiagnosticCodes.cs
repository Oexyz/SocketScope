namespace OeXYZ.SocketScope.Core.Diagnostics;

public static class DiagnosticCodes
{
    public const string AccessDenied = "provider.access-denied";
    public const string Cancelled = "provider.cancelled";
    public const string CaptureFailed = "provider.capture-failed";
    public const string InvalidNativeData = "provider.invalid-native-data";
    public const string NotSupported = "provider.not-supported";
    public const string ProcessDisappeared = "process.disappeared";
    public const string ProcessIdentityChanged = "process.identity-changed";
    public const string ResultsTruncated = "capture.results-truncated";
    public const string ServiceEnumerationPartial = "service.enumeration-partial";
    public const string TimedOut = "provider.timed-out";
    public const string UnknownOwner = "process.owner-unknown";
    public const string OwnershipCorrelationPartial = "process.ownership-correlation-partial";
    public const string ContainerEvidenceNotFound = "container.evidence-not-found";
    public const string ContainerForwardingProxyUnresolved = "container.forwarding-proxy-unresolved";
}
