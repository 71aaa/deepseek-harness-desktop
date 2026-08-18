using System.Runtime.InteropServices;

namespace DeepSeekHarnessDesktop.Core.Services;

/// <summary>
/// 通过 kernel32 读取进程创建时间（FILETIME → UTC Ticks，100ns 精度），
/// 用于 “PID + StartTime” 所有权验证（防止 PID 复用导致误杀）。
/// </summary>
public static class NativeProcessInfo
{
    private const int ProcessQueryLimitedInformation = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(
        IntPtr process, out long creationFileTime, out long exitFileTime, out long kernelFileTime, out long userFileTime);

    /// <summary>进程创建时间（UTC Ticks）；进程不存在/无权限/失败一律返回 null（调用方按“不可验证”处理）。</summary>
    public static long? GetStartTimeTicksUtc(int pid)
    {
        if (pid <= 0) return null;
        IntPtr handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            if (!GetProcessTimes(handle, out long creation, out _, out _, out _))
                return null;
            return DateTime.FromFileTimeUtc(creation).Ticks;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    public static string ToIsoUtc(long ticksUtc) => new DateTime(ticksUtc, DateTimeKind.Utc).ToString("o");
}
