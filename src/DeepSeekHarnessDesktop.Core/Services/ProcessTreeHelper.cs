using System.Runtime.InteropServices;

namespace DeepSeekHarnessDesktop.Core.Services;

/// <summary>
/// 进程树辅助：通过 NtQueryInformationProcess（ntdll）查询父进程 PID，
/// 用于验证“监听 3080 的进程确实是我们启动的 cmd → dsh.cmd → node 链的后代”。
/// 不依赖 WMI / PowerShell。
/// </summary>
public static class ProcessTreeHelper
{
    private const int ProcessBasicInformationClass = 0;
    private const int ProcessQueryLimitedInformation = 0x1000;

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle, int processInformationClass, IntPtr processInformation,
        int processInformationLength, out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    /// <summary>x64 布局（本程序按 win-x64 发布）。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2_1;
        public IntPtr Reserved2_2;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    /// <summary>父进程 PID；仅在 x64 下可用，失败返回 null。</summary>
    public static int? GetParentPid(int pid)
    {
        if (IntPtr.Size != 8 || pid <= 0) return null;
        IntPtr handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var info = new ProcessBasicInformation();
            int size = Marshal.SizeOf<ProcessBasicInformation>();
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(info, ptr, false);
                int status = NtQueryInformationProcess(handle, ProcessBasicInformationClass, ptr, size, out _);
                if (status != 0) return null;
                var result = Marshal.PtrToStructure<ProcessBasicInformation>(ptr);
                var parent = result.InheritedFromUniqueProcessId.ToInt64();
                return parent is > 0 and <= int.MaxValue ? (int)parent : null;
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>祖先 PID 链（向上最多 maxDepth 层）。</summary>
    public static List<int> GetAncestorChain(int pid, int maxDepth = 16)
    {
        var chain = new List<int>();
        var seen = new HashSet<int>();
        int? current = pid;
        while (current is int c && c > 0 && chain.Count < maxDepth && seen.Add(c))
        {
            int? parent = GetParentPid(c);
            if (parent is not int p) break;
            chain.Add(p);
            current = p;
        }
        return chain;
    }

    /// <summary>pid 是否位于 ancestorPid 的进程树内。</summary>
    public static bool IsDescendantOf(int pid, int ancestorPid)
    {
        if (pid <= 0 || ancestorPid <= 0 || pid == ancestorPid) return false;
        return GetAncestorChain(pid).Contains(ancestorPid);
    }
}
