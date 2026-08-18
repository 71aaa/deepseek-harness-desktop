using DeepSeekHarnessDesktop.Core.Logging;
using DeepSeekHarnessDesktop.Core.Models;
using DeepSeekHarnessDesktop.Core.Services;
using Xunit;

namespace DeepSeekHarnessDesktop.Tests;

/// <summary>runtime.json 序列化 / 恢复 / 损坏备份 / 清理（临时目录，不接触真实状态文件）。</summary>
public class StateServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public StateServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "dshd-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "runtime.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static HarnessRuntimeState SampleState() => new()
    {
        SchemaVersion = 1,
        Phase = HarnessRuntimePhase.Running,
        SessionId = "sess-123",
        Port = 3080,
        Command = @"dsh-runtime\node_modules\.bin\dsh.cmd web",
        Url = "http://127.0.0.1:3080",
        LauncherPid = 1111,
        LauncherStartTimeTicksUtc = 600_000_000_000_000_000,
        LauncherStartTimeIsoUtc = "2025-01-01T00:00:00.0000000Z",
        LauncherProcessName = "cmd",
        HarnessPid = 2222,
        HarnessStartTimeTicksUtc = 600_000_000_000_001_000,
        HarnessStartTimeIsoUtc = "2025-01-01T00:00:00.1000000Z",
        HarnessProcessName = "node",
        OwnedByDesktop = true,
        RecordedAtIsoUtc = "2025-01-01T00:00:01.0000000Z",
        AdoptedAtIsoUtc = null,
    };

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var service = new StateService(_path, NullLogger.Instance);
        var original = SampleState();

        service.Save(original);
        var loaded = service.Load();

        Assert.NotNull(loaded);
        Assert.Equal(original.SchemaVersion, loaded!.SchemaVersion);
        Assert.Equal(original.Phase, loaded.Phase);
        Assert.Equal(original.SessionId, loaded.SessionId);
        Assert.Equal(original.Port, loaded.Port);
        Assert.Equal(original.Command, loaded.Command);
        Assert.Equal(original.Url, loaded.Url);
        Assert.Equal(original.LauncherPid, loaded.LauncherPid);
        Assert.Equal(original.LauncherStartTimeTicksUtc, loaded.LauncherStartTimeTicksUtc);
        Assert.Equal(original.LauncherStartTimeIsoUtc, loaded.LauncherStartTimeIsoUtc);
        Assert.Equal(original.HarnessPid, loaded.HarnessPid);
        Assert.Equal(original.HarnessStartTimeTicksUtc, loaded.HarnessStartTimeTicksUtc);
        Assert.Equal(original.HarnessStartTimeIsoUtc, loaded.HarnessStartTimeIsoUtc);
        Assert.Equal(original.HarnessProcessName, loaded.HarnessProcessName);
        Assert.Equal(original.OwnedByDesktop, loaded.OwnedByDesktop);
        Assert.Equal(original.RecordedAtIsoUtc, loaded.RecordedAtIsoUtc);
    }

    [Fact]
    public void Load_MissingFile_ReturnsNull()
    {
        var service = new StateService(_path, NullLogger.Instance);
        Assert.Null(service.Load());
    }

    [Fact]
    public void Load_CorruptFile_ReturnsNullAndBacksUp()
    {
        File.WriteAllText(_path, "{{ definitely not json");
        var service = new StateService(_path, NullLogger.Instance);

        Assert.Null(service.Load());
        Assert.Single(Directory.GetFiles(_dir, "runtime.json.corrupt-*"));
        Assert.False(File.Exists(_path)); // 损坏文件已被移走备份
    }

    [Fact]
    public void Clear_RemovesFile()
    {
        var service = new StateService(_path, NullLogger.Instance);
        service.Save(SampleState());
        Assert.True(File.Exists(_path));

        service.Clear();
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void Save_OverwritesExistingFile()
    {
        var service = new StateService(_path, NullLogger.Instance);
        var first = SampleState();
        first.SessionId = "first";
        service.Save(first);

        var second = SampleState();
        second.SessionId = "second";
        service.Save(second);

        Assert.Equal("second", service.Load()!.SessionId);
    }
}
