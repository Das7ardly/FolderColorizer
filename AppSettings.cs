using System.Text.Json;

namespace FolderColorizer;

// User preferences, stored as JSON in the data folder. Unknown or broken files fall back to defaults.
sealed class AppSettings
{
    public PreviewStyle PreviewStyle { get; set; } = PreviewStyle.Window;

    static string FilePath => Path.Combine(IconMaker.DataDir, "settings.json");
    // Styles are stored by name ("Window"), not number, so the file stays readable and stable.
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(IconMaker.DataDir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
    }
}
