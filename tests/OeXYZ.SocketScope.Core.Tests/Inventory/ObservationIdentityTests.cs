using OeXYZ.SocketScope.Core.Domain;
using OeXYZ.SocketScope.Core.Inventory;

namespace OeXYZ.SocketScope.Core.Tests.Inventory;

public sealed class ObservationIdentityTests
{
    [Fact]
    public void Create_NativeIdentity_IsStableAndStrong()
    {
        RawSocketRecord socket = Socket("kernel-cookie-42", 123);

        (string first, IdentityStrength firstStrength) = ObservationIdentity.Create(socket, null);
        (string second, IdentityStrength secondStrength) = ObservationIdentity.Create(socket, null);

        Assert.Equal(first, second);
        Assert.Equal(32, first.Length);
        Assert.Equal(IdentityStrength.OperatingSystemStable, firstStrength);
        Assert.Equal(firstStrength, secondStrength);
    }

    [Fact]
    public void Create_FallbackIdentity_ChangesWhenPidIsReused()
    {
        RawSocketRecord socket = Socket(string.Empty, 123);
        ProcessMetadata oldProcess = Process(123, "old-start");
        ProcessMetadata reusedProcess = Process(123, "new-start");

        (string oldId, IdentityStrength oldStrength) = ObservationIdentity.Create(socket, oldProcess);
        (string newId, IdentityStrength newStrength) = ObservationIdentity.Create(socket, reusedProcess);

        Assert.NotEqual(oldId, newId);
        Assert.Equal(IdentityStrength.ProcessQualified, oldStrength);
        Assert.Equal(oldStrength, newStrength);
    }

    [Fact]
    public void Create_FallbackWithoutProcessIdentity_IsWeak()
    {
        (string _, IdentityStrength strength) = ObservationIdentity.Create(Socket(string.Empty, null), null);

        Assert.Equal(IdentityStrength.WeakTuple, strength);
    }

    private static RawSocketRecord Socket(string nativeId, int? pid)
    {
        return new RawSocketRecord(
            nativeId,
            TransportProtocol.Tcp,
            IpVersion.IPv4,
            SocketRole.TcpConnection,
            new IpEndpoint("127.0.0.1", 55000),
            new IpEndpoint("127.0.0.1", 443),
            SocketState.Established,
            pid,
            null,
            "host",
            "test");
    }

    private static ProcessMetadata Process(int pid, string token)
    {
        return new ProcessMetadata(
            new ProcessIdentity(pid, null, token),
            "fixture",
            null,
            DataAvailability.Available);
    }
}
