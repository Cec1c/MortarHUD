using System.Text.Json;
using MortarHUD.Core.Configuration;

namespace MortarHUD.Core.Diagnostics;

/// <summary>一次 Debug 转储的产物路径。</summary>
public sealed record DebugArtifact(string BaseName, string? RawPath, string? ProcessedPath, string ResultPath);

/// <summary>
/// 把 Debug 模式下的 ROI 截图与识别结果落盘（TDD §33）。
/// </summary>
/// <remarks>
/// <para>
/// 目录：<c>%AppData%/MortarHUD/Debug/</c>，
/// 命名：<c>2026-09-16_201500_raw.png</c> / <c>_processed.png</c> / <c>_result.json</c>。
/// </para>
/// <para>
/// 默认关闭，且只保存那一小块 ROI——TDD §39 明确要求默认不保存整屏截图。
/// </para>
/// </remarks>
public sealed class DebugArtifactWriter
{
    private readonly string _directory;
    private readonly int _retainedFiles;

    public DebugArtifactWriter(string? directory = null, int retainedFiles = 500)
    {
        _directory = directory ?? AppPaths.DebugDirectory;
        _retainedFiles = retainedFiles;
    }

    public string Directory => _directory;

    /// <summary>生成一次转储用的时间戳前缀。同一次采集的 raw/processed/json 共用它。</summary>
    public static string CreateBaseName(DateTime timestamp)
        => timestamp.ToString("yyyy-MM-dd_HHmmss_fff");

    /// <summary>写入结果 JSON。</summary>
    public string WriteResult(string baseName, object payload)
    {
        System.IO.Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, $"{baseName}_result.json");

        File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
        }));

        TrimOldFiles();
        return path;
    }

    public string ResolveRawPath(string baseName) => Path.Combine(_directory, $"{baseName}_raw.png");

    public string ResolveProcessedPath(string baseName)
        => Path.Combine(_directory, $"{baseName}_processed.png");

    /// <summary>只保留最近若干个文件，避免长期开着 Debug 把磁盘写满。</summary>
    private void TrimOldFiles()
    {
        try
        {
            var files = System.IO.Directory
                .EnumerateFiles(_directory)
                .Select(path => new FileInfo(path))
                .OrderByDescending(info => info.LastWriteTime)
                .Skip(_retainedFiles)
                .ToList();

            foreach (var file in files)
            {
                file.Delete();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"清理 Debug 转储失败：{ex.Message}");
        }
    }
}
