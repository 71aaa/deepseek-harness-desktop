using DeepSeekHarnessDesktop.Core.Services;
using Xunit;

namespace DeepSeekHarnessDesktop.Tests;

/// <summary>用模拟字节数据测试 GetExtendedTcpTable 结果解析（不接触真实端口表）。</summary>
public class PortProcessResolverTests
{
    private static byte[] BuildTable(params (uint State, uint LocalAddr, uint LocalPortRaw, uint Pid)[] rows)
    {
        using var ms = new MemoryStream();
        ms.Write(BitConverter.GetBytes(rows.Length), 0, 4);
        foreach (var (state, addr, port, pid) in rows)
        {
            ms.Write(BitConverter.GetBytes(state), 0, 4);   // dwState
            ms.Write(BitConverter.GetBytes(addr), 0, 4);    // dwLocalAddr（网络字节序）
            ms.Write(BitConverter.GetBytes(port), 0, 4);    // dwLocalPort（低 16 位网络字节序）
            ms.Write(BitConverter.GetBytes(0u), 0, 4);      // dwRemoteAddr
            ms.Write(BitConverter.GetBytes(0u), 0, 4);      // dwRemotePort
            ms.Write(BitConverter.GetBytes(pid), 0, 4);     // dwOwningPid
        }
        return ms.ToArray();
    }

    [Fact]
    public void ParseBytes_ExtractsPortsAndPids()
    {
        // 3080 = 0x0C08 → 网络字节序 [0x0C,0x08] → LE u32 = 0x0000080C = 2060
        // 8080 = 0x1F90 → 网络字节序 [0x1F,0x90] → LE u32 = 0x0000901F = 36895
        var bytes = BuildTable(
            (5u, 0x0100007Fu, 2060u, 1234u),
            (5u, 0x0100007Fu, 0x901Fu, 5678u));

        var rows = PortProcessResolver.ParseBytes(bytes);

        Assert.Equal(2, rows.Count);
        Assert.Equal(3080, rows[0].LocalPort);
        Assert.Equal(1234, rows[0].OwningPid);
        Assert.Equal(0x0100007Fu, rows[0].LocalAddress);
        Assert.Equal(8080, rows[1].LocalPort);
        Assert.Equal(5678, rows[1].OwningPid);
    }

    [Fact]
    public void SwapPort_ConvertsNetworkToHostOrder()
    {
        Assert.Equal(3080, PortProcessResolver.SwapPort(2060u));
        Assert.Equal(8080, PortProcessResolver.SwapPort(0x901Fu));
        Assert.Equal(0, PortProcessResolver.SwapPort(0u));
        Assert.Equal(1, PortProcessResolver.SwapPort(0x0100u)); // 端口 1 → 网络字节序 [0x00,0x01] → LE u32 0x00000100
    }

    [Fact]
    public void SelectListener_PrefersLoopback()
    {
        var rows = new List<TcpOwnerRow>
        {
            new(5u, 0x00000000u, 3080, 42),   // 0.0.0.0:3080
            new(5u, 0x0100007Fu, 3080, 77),   // 127.0.0.1:3080
        };
        var picked = PortProcessResolver.SelectListener(rows, 3080);
        Assert.NotNull(picked);
        Assert.Equal(77, picked!.OwningPid);
    }

    [Fact]
    public void SelectListener_ReturnsNonLoopbackWhenOnlyOption()
    {
        var rows = new List<TcpOwnerRow> { new(5u, 0x00000000u, 3080, 42) };
        var picked = PortProcessResolver.SelectListener(rows, 3080);
        Assert.NotNull(picked);
        Assert.Equal(42, picked!.OwningPid);
    }

    [Fact]
    public void SelectListener_ReturnsNullWhenPortAbsent()
    {
        var rows = new List<TcpOwnerRow> { new(5u, 0x0100007Fu, 8080, 42) };
        Assert.Null(PortProcessResolver.SelectListener(rows, 3080));
    }

    [Fact]
    public void ParseBytes_HandlesEmptyAndTruncated()
    {
        Assert.Empty(PortProcessResolver.ParseBytes(new byte[] { 0, 0, 0, 0 }));
        Assert.Empty(PortProcessResolver.ParseBytes(new byte[3]));

        // 声明 3 行但只给 1 行数据：只解析出实际存在的那 1 行
        var bytes = BuildTable((5u, 0x0100007Fu, 2060u, 9u));
        bytes[0] = 3;
        var rows = PortProcessResolver.ParseBytes(bytes);
        Assert.Single(rows);
        Assert.Equal(9, rows[0].OwningPid);
    }
}
