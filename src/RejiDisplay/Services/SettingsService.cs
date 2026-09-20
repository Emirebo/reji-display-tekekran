using System;
using System.IO;
using System.Text.Json;
using RejiDisplay.Models;

namespace RejiDisplay.Services
{
    public class SettingsService
    {
        private readonly string _settingsFilePath;

        public SettingsService(string? customPath = null)
        {
            if (!string.IsNullOrWhiteSpace(customPath))
            {
                _settingsFilePath = customPath;
            }
            else
            {
                string appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RejiDisplay-TekEkran");
                Directory.CreateDirectory(appDataDir);
                _settingsFilePath = Path.Combine(appDataDir, "settings.json");
            }
        }

        public string SettingsFilePath => _settingsFilePath;

        public AppSettings LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    string json = File.ReadAllText(_settingsFilePath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null)
                    {
                        return settings;
                    }
                }
            }
            catch
            {
                // Fallback to default settings on error
            }

            return new AppSettings();
        }

        public bool SaveSettings(AppSettings settings)
        {
            try
            {
                string dir = Path.GetDirectoryName(_settingsFilePath)!;
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string tempPath = _settingsFilePath + ".tmp";
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(settings, options);

                File.WriteAllText(tempPath, json);
                File.Copy(tempPath, _settingsFilePath, overwrite: true);
                File.Delete(tempPath);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
