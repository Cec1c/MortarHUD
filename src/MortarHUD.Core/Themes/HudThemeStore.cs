using System.Text.Json;
using System.Text.Json.Serialization;
using MortarHUD.Core.Configuration;

namespace MortarHUD.Core.Themes;

/// <summary>
/// 主题的读写：内置主题来自 <see cref="HudThemeLibrary"/>，
/// 自定义主题以独立 JSON 存在 <c>%AppData%/MortarHUD/Themes/</c>（TDD §26）。
/// </summary>
public sealed class HudThemeStore
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    private readonly string _directory;

    public HudThemeStore(string? directory = null)
        => _directory = directory ?? AppPaths.ThemesDirectory;

    public string Directory => _directory;

    public static JsonSerializerOptions CreateSerializerOptions() => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>内置主题 + 磁盘上的自定义主题。同名时自定义主题优先。</summary>
    public IReadOnlyList<HudTheme> LoadAll()
    {
        var byName = new Dictionary<string, HudTheme>(StringComparer.OrdinalIgnoreCase);

        foreach (var builtIn in HudThemeLibrary.BuiltIn)
        {
            byName[builtIn.Name] = builtIn;
        }

        foreach (var custom in LoadCustom())
        {
            byName[custom.Name] = custom;
        }

        return byName.Values
            .OrderByDescending(t => t.IsBuiltIn)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<HudTheme> LoadCustom()
    {
        if (!System.IO.Directory.Exists(_directory))
        {
            return [];
        }

        var themes = new List<HudTheme>();

        foreach (var file in System.IO.Directory.EnumerateFiles(_directory, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var theme = JsonSerializer.Deserialize<HudTheme>(json, SerializerOptions);
                if (theme is null || string.IsNullOrWhiteSpace(theme.Name))
                {
                    continue;
                }

                // 磁盘上的主题一律视为自定义，即使 JSON 里写了 isBuiltIn。
                theme.IsBuiltIn = false;
                themes.Add(theme);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                // 单个坏文件不该让整个主题列表加载失败。
                System.Diagnostics.Debug.WriteLine($"主题读取失败 {file}: {ex.Message}");
            }
        }

        return themes;
    }

    public void Save(HudTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        if (string.IsNullOrWhiteSpace(theme.Name))
        {
            throw new ArgumentException("主题必须有名字。", nameof(theme));
        }

        System.IO.Directory.CreateDirectory(_directory);
        var path = GetPathFor(theme.Name);
        File.WriteAllText(path, JsonSerializer.Serialize(theme, SerializerOptions));
    }

    /// <summary>删除自定义主题。内置主题会直接抛异常。</summary>
    public bool Delete(string name)
    {
        if (HudThemeLibrary.FindBuiltIn(name) is not null)
        {
            throw new InvalidOperationException($"内置主题「{name}」不能删除。");
        }

        var path = GetPathFor(name);
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    public bool Exists(string name) => File.Exists(GetPathFor(name));

    public string GetPathFor(string name) => Path.Combine(_directory, SanitizeFileName(name) + ".json");

    /// <summary>把主题名转成安全的文件名。</summary>
    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var result = new string(chars).Trim();
        return string.IsNullOrEmpty(result) ? "theme" : result;
    }
}
