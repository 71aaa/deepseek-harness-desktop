namespace DeepSeekHarnessDesktop.Core.Services;

public sealed class EnvironmentCheckResult
{
    public bool NodeFound { get; init; }
    public string? NodePath { get; init; }

    public bool IsOk => NodeFound;
}

/// <summary>
/// 启动前环境检测：node 是否可用。
/// 纯 PATH 目录扫描实现（可测试），不依赖 spawn 子进程。
/// </summary>
public static class EnvironmentService
{
    public static EnvironmentCheckResult Check() => Check(GetSearchDirectories());

    internal static IEnumerable<string> GetSearchDirectories()
    {
        var dirs = new List<string>();
        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
            dirs.AddRange(path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return dirs.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    internal static EnvironmentCheckResult Check(IEnumerable<string> directories)
    {
        string? node = null;
        foreach (var dir in directories)
        {
            if (node is null) node = FindFile(dir, "node.exe");
        }
        return new EnvironmentCheckResult
        {
            NodeFound = node is not null,
            NodePath = node,
        };
    }

    private static string? FindFile(string dir, string name)
    {
        var full = Path.Combine(dir, name);
        return File.Exists(full) ? full : null;
    }
}
