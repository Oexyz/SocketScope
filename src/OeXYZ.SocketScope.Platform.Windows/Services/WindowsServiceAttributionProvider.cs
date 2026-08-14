using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using OeXYZ.SocketScope.Core.Abstractions;
using OeXYZ.SocketScope.Core.Domain;

namespace OeXYZ.SocketScope.Platform.Windows.Services;

[SupportedOSPlatform("windows")]
public sealed partial class WindowsServiceAttributionProvider : IServiceAttributionProvider
{
    private const uint ScManagerEnumerateService = 0x0004;
    private const uint ServiceWin32 = 0x00000030;
    private const uint ServiceStateAll = 0x00000003;
    private const uint ScEnumProcessInfo = 0;
    private const int ErrorAccessDenied = 5;
    private const int ErrorMoreData = 234;
    private const int MaximumBufferBytes = 64 * 1024 * 1024;
    private const int MaximumChunks = 64;

    public string Name => "windows.service-control-manager";

    public ValueTask<IReadOnlyDictionary<int, ServiceAttribution>> GetServicesAsync(
        IReadOnlySet<int> processIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(processIds);
        cancellationToken.ThrowIfCancellationRequested();
        nint nativeManager = OpenSCManager(null, null, ScManagerEnumerateService);
        using SafeServiceHandle manager = new(nativeManager);
        if (manager.IsInvalid)
        {
            ThrowNativeFailure(Marshal.GetLastPInvokeError());
        }

        Dictionary<int, List<(string Id, string DisplayName)>> matches = [];
        uint resumeHandle = 0;
        uint bytesNeeded = 0;
        uint returned = 0;
        _ = EnumServicesStatusEx(
            manager.DangerousGetHandle(),
            ScEnumProcessInfo,
            ServiceWin32,
            ServiceStateAll,
            0,
            0,
            ref bytesNeeded,
            ref returned,
            ref resumeHandle,
            null);
        int initialError = Marshal.GetLastPInvokeError();
        if (initialError != ErrorMoreData && initialError != 0)
        {
            ThrowNativeFailure(initialError);
        }

        resumeHandle = 0;

        for (int chunk = 0; chunk < MaximumChunks && bytesNeeded > 0; chunk++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (bytesNeeded > MaximumBufferBytes)
            {
                throw new InvalidDataException("The Windows service table exceeds the supported size.");
            }

            nint buffer = Marshal.AllocHGlobal(checked((int)bytesNeeded));
            try
            {
                uint bufferSize = bytesNeeded;
                bytesNeeded = 0;
                returned = 0;
                bool success = EnumServicesStatusEx(
                    manager.DangerousGetHandle(),
                    ScEnumProcessInfo,
                    ServiceWin32,
                    ServiceStateAll,
                    buffer,
                    bufferSize,
                    ref bytesNeeded,
                    ref returned,
                    ref resumeHandle,
                    null);
                int error = success ? 0 : Marshal.GetLastPInvokeError();
                if (!success && error != ErrorMoreData)
                {
                    ThrowNativeFailure(error);
                }

                ParseChunk(buffer, returned, processIds, matches);
                if (success)
                {
                    break;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        Dictionary<int, ServiceAttribution> result = new(processIds.Count);
        foreach (int processId in processIds)
        {
            List<(string Id, string DisplayName)> services = matches.TryGetValue(processId, out List<(string, string)>? found)
                ? found
                : [];
            bool sharesProcess = services.Count > 1;
            ImmutableArray<ServiceIdentity> identities = [.. services
                .OrderBy(service => service.Id, StringComparer.OrdinalIgnoreCase)
                .Select(service => new ServiceIdentity(
                    service.Id,
                    service.DisplayName,
                    "windows-scm",
                    sharesProcess))];
            result[processId] = new ServiceAttribution(
                processId,
                identities,
                DataAvailability.Available);
        }

        return ValueTask.FromResult<IReadOnlyDictionary<int, ServiceAttribution>>(result);
    }

    private static void ParseChunk(
        nint buffer,
        uint returned,
        IReadOnlySet<int> requestedProcessIds,
        Dictionary<int, List<(string Id, string DisplayName)>> matches)
    {
        int rowSize = Marshal.SizeOf<EnumServiceStatusProcess>();
        if (returned > MaximumBufferBytes / rowSize)
        {
            throw new InvalidDataException("The Windows service row count is invalid.");
        }

        for (int index = 0; index < returned; index++)
        {
            EnumServiceStatusProcess row = Marshal.PtrToStructure<EnumServiceStatusProcess>(
                nint.Add(buffer, checked(index * rowSize)));
            uint nativePid = row.Status.ProcessId;
            if (nativePid is 0 or > int.MaxValue || !requestedProcessIds.Contains((int)nativePid))
            {
                continue;
            }

            string? id = Marshal.PtrToStringUni(row.ServiceName);
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            string displayName = Marshal.PtrToStringUni(row.DisplayName) ?? id;
            int processId = (int)nativePid;
            if (!matches.TryGetValue(processId, out List<(string Id, string DisplayName)>? services))
            {
                services = [];
                matches[processId] = services;
            }

            services.Add((id, displayName));
        }
    }

    private static void ThrowNativeFailure(int error)
    {
        if (error == ErrorAccessDenied)
        {
            throw new UnauthorizedAccessException("Windows denied read access to the service table.");
        }

        throw new InvalidDataException($"Windows service enumeration failed with code {error}.");
    }

    [LibraryImport("advapi32.dll", EntryPoint = "OpenSCManagerW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint OpenSCManager(
        string? machineName,
        string? databaseName,
        uint desiredAccess);

    [LibraryImport("advapi32.dll", EntryPoint = "EnumServicesStatusExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumServicesStatusEx(
        nint manager,
        uint infoLevel,
        uint serviceType,
        uint serviceState,
        nint services,
        uint bufferSize,
        ref uint bytesNeeded,
        ref uint servicesReturned,
        ref uint resumeHandle,
        string? groupName);

    [LibraryImport("advapi32.dll", EntryPoint = "CloseServiceHandle")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseServiceHandle(nint handle);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct EnumServiceStatusProcess
    {
        internal readonly nint ServiceName;
        internal readonly nint DisplayName;
        internal readonly ServiceStatusProcess Status;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct ServiceStatusProcess
    {
        internal readonly uint ServiceType;
        internal readonly uint CurrentState;
        internal readonly uint ControlsAccepted;
        internal readonly uint Win32ExitCode;
        internal readonly uint ServiceSpecificExitCode;
        internal readonly uint CheckPoint;
        internal readonly uint WaitHint;
        internal readonly uint ProcessId;
        internal readonly uint ServiceFlags;
    }

    private sealed class SafeServiceHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal SafeServiceHandle(nint nativeHandle)
            : base(true)
        {
            SetHandle(nativeHandle);
        }

        protected override bool ReleaseHandle()
        {
            return CloseServiceHandle(handle);
        }
    }
}
