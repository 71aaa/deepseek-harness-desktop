using System.Runtime.InteropServices;

namespace DeepSeekHarnessDesktop.Core.Services;

/// <summary>MIB_TCPROW_OWNER_PID 的一行。</summary>
public sealed record TcpOwnerRow(uint State, uint LocalAddress, int LocalPort, int OwningPid);

/// <summary>
/// 通过 GetExtendedTcpTable（iphlpapi.dll）在 C# 内部直接完成 “TCP 端口 → 监听 PID” 查询。
/// 不依赖 PowerShell / Get-NetTCPConnection / netstat 文本解析 / WMI。
/// </summary>
public static class PortProcessResolver
{
    private const int AfInet = 2;
    private const uint ErrorInsufficientBuffer = 122u;
    private const uint LoopbackV4 = 0x0100007Fu; // 127.0.0.1（网络字节序）

    private enum TcpTableClass
    {
        OwnerPidListener = 3,
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable, ref int dwOutBufLen, [MarshalAs(UnmanagedType.Bool)] bool sort,
        int ipVersion, TcpTableClass tableClass, int reserved);

    /// <summary>查询指定端口的 TCP 监听进程 PID；找不到返回 null（含 API 失败等任何情况）。</summary>
    public static TcpOwnerRow? FindListener(int port)
    {
        int size = 0;
        uint result = GetExtendedTcpTable(IntPtr.Zero, ref size, false, AfInet, TcpTableClass.OwnerPidListener, 0);
        if (result != ErrorInsufficientBuffer)
            return null;

        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            result = GetExtendedTcpTable(buffer, ref size, false, AfInet, TcpTableClass.OwnerPidListener, 0);
            if (result != 0)
                return null;
            var rows = ParseRows(buffer, size);
            return SelectListener(rows, port);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    internal static List<TcpOwnerRow> ParseRows(IntPtr buffer, int length)
    {
        if (buffer == IntPtr.Zero || length < 4)
            return new List<TcpOwnerRow>();
        var bytes = new byte[length];
        Marshal.Copy(buffer, bytes, 0, length);
        return ParseBytes(bytes);
    }

    /// <summary>解析 GetExtendedTcpTable 返回的原始字节（4 字节行数 + 每行 24 字节 MIB_TCPROW_OWNER_PID）。</summary>
    internal static List<TcpOwnerRow> ParseBytes(byte[] bytes)
    {
        var rows = new List<TcpOwnerRow>();
        if (bytes.Length < 4)
            return rows;
        int count = BitConverter.ToInt32(bytes, 0);
        int offset = 4;
        for (int i = 0; i < count && offset + 24 <= bytes.Length; i++, offset += 24)
        {
            uint state = BitConverter.ToUInt32(bytes, offset);
            uint localAddr = BitConverter.ToUInt32(bytes, offset + 4);
            uint localPortRaw = BitConverter.ToUInt32(bytes, offset + 8);
            uint owningPid = BitConverter.ToUInt32(bytes, offset + 20);
            rows.Add(new TcpOwnerRow(state, localAddr, SwapPort(localPortRaw), (int)owningPid));
        }
        return rows;
    }

    /// <summary>dwLocalPort 的低 16 位是网络字节序的端口号，转为主机字节序。</summary>
    internal static int SwapPort(uint networkOrderPort)
        => (int)(((networkOrderPort & 0xFFu) << 8) | ((networkOrderPort >> 8) & 0xFFu));

    /// <summary>从表中挑选目标端口的监听行；存在多个地址监听时优先 127.0.0.1。</summary>
    internal static TcpOwnerRow? SelectListener(IReadOnlyList<TcpOwnerRow> rows, int port)
    {
        TcpOwnerRow? fallback = null;
        foreach (var row in rows)
        {
            if (row.LocalPort != port)
                continue;
            if (row.LocalAddress == LoopbackV4)
                return row;
            fallback ??= row;
        }
        return fallback;
    }
}
