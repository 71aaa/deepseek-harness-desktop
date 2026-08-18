using System.Text.Json;
using System.Text.Json.Serialization;
using DeepSeekHarnessDesktop.Core.Logging;
using DeepSeekHarnessDesktop.Core.Models;

namespace DeepSeekHarnessDesktop.Core.Services;

/// <summary>
/// runtime.json 状态持久化：原子写入（tmp + move）、损坏文件备份、清理。
/// 任何 IO 失败都不会抛出（仅记日志），保证主流程不因状态文件崩溃。
/// </summary>
public sealed class StateService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;
    private readonly ILog _log;

    public StateService(string filePath, ILog log)
    {
        _filePath = filePath;
        _log = log;
    }

    public HarnessRuntimeState? Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return null;
            var json = File.ReadAllText(_filePath);
            var state = JsonSerializer.Deserialize<HarnessRuntimeState>(json, Options);
            if (state is null)
            {
                _log.Warn($"状态文件为空或无法解析: {_filePath}");
                return null;
            }
            return state;
        }
        catch (Exception ex)
        {
            _log.Error($"读取状态文件失败: {_filePath}", ex);
            TryBackupCorruptFile();
            return null;
        }
    }

    public void Save(HarnessRuntimeState state)
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = _filePath + ".tmp";
            var json = JsonSerializer.Serialize(state, Options);
            File.WriteAllText(tmp, json);
            File.Move(tmp, _filePath, overwrite: true);
            _log.Debug($"状态文件已保存: {_filePath}");
        }
        catch (Exception ex)
        {
            _log.Error($"保存状态文件失败: {_filePath}", ex);
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
                _log.Debug($"状态文件已清理: {_filePath}");
            }
        }
        catch (Exception ex)
        {
            _log.Error($"清理状态文件失败: {_filePath}", ex);
        }
    }

    private void TryBackupCorruptFile()
    {
        try
        {
            var backup = _filePath + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss");
            File.Move(_filePath, backup, overwrite: true);
            _log.Warn($"损坏的状态文件已备份为: {backup}");
        }
        catch { }
    }
}
