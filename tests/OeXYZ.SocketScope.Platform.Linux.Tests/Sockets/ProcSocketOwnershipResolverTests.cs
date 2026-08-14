using OeXYZ.SocketScope.Platform.Linux.Sockets;

namespace OeXYZ.SocketScope.Platform.Linux.Tests.Sockets;

public sealed class ProcSocketOwnershipResolverTests
{
    [Theory]
    [InlineData("socket:[12345]", 12345UL)]
    [InlineData("socket:[18446744073709551615]", ulong.MaxValue)]
    public void TryParseSocketInode_AcceptsExactKernelFormat(string target, ulong expected)
    {
        Assert.True(ProcSocketOwnershipResolver.TryParseSocketInode(target, out ulong actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("socket:123")]
    [InlineData("pipe:[123]")]
    [InlineData("socket:[-1]")]
    [InlineData("socket:[123]suffix")]
    public void TryParseSocketInode_RejectsOtherLinkTargets(string target)
    {
        Assert.False(ProcSocketOwnershipResolver.TryParseSocketInode(target, out _));
    }
}
