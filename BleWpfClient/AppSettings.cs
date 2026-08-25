using System.IO;
using System.Text.Json;

namespace BleWpfClient;

public sealed class AppSettings
{
    public byte Mode { get; set; } = 1;
    public ushort ThresholdMm { get; set; } = 100;
    public ushort RangeMinMm { get; set; } = 300;
    public ushort RangeMaxMm { get; set; } = 2000;
    public byte GraphMaxDistanceMetres { get; set; } = 10;
    public int GraphWindowSeconds { get; set; } = 30;

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TfLumaBleWpfClient", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings is not null)
                {
                    return settings;
                }
            }
        }
        catch
        {
            // Fall back to defaults if the settings file is missing or corrupt.
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // Best-effort persistence; ignore failures (e.g. read-only profile).
        }
    }
}
