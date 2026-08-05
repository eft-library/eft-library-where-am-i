using System.IO;
using System.Text.Json;

namespace where_am_i.Services
{
    public sealed class UserPreferencesService
    {
        private const int CurrentVersion = 2;
        private readonly string settingsPath;

        public UserPreferencesService()
        {
            var settingsDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EFTLibrary",
                "WhereAmI"
            );
            settingsPath = Path.Combine(settingsDirectory, "settings.json");
        }

        public UserPreferences Load()
        {
            try
            {
                if (!File.Exists(settingsPath))
                    return new UserPreferences();

                var json = File.ReadAllText(settingsPath);
                var preferences = JsonSerializer.Deserialize<UserPreferences>(json);
                if (preferences is null)
                    return new UserPreferences();

                // Version 1 only contained the email fields and can be upgraded safely.
                preferences.Version = CurrentVersion;
                return preferences;
            }
            catch (JsonException)
            {
                return new UserPreferences();
            }
            catch (IOException)
            {
                return new UserPreferences();
            }
            catch (UnauthorizedAccessException)
            {
                return new UserPreferences();
            }
        }

        public void SaveEmail(string email)
        {
            var preferences = Load();
            preferences.Version = CurrentVersion;
            preferences.RememberEmail = true;
            preferences.Email = email.Trim();

            Save(preferences);
        }

        public void ClearEmail()
        {
            var preferences = Load();
            preferences.RememberEmail = false;
            preferences.Email = "";
            Save(preferences);
        }

        public void SaveMonitoringEnabled(bool enabled)
        {
            var preferences = Load();
            preferences.MonitoringEnabled = enabled;
            Save(preferences);
        }

        public void MarkWelcomeShown()
        {
            var preferences = Load();
            preferences.HasShownWelcome = true;
            Save(preferences);
        }

        private void Save(UserPreferences preferences)
        {
            var directory = Path.GetDirectoryName(settingsPath)
                ?? throw new InvalidOperationException("설정 폴더를 확인할 수 없습니다.");

            Directory.CreateDirectory(directory);
            var json = JsonSerializer.Serialize(
                preferences,
                new JsonSerializerOptions { WriteIndented = true }
            );
            File.WriteAllText(settingsPath, json);
        }
    }

    public sealed class UserPreferences
    {
        public int Version { get; set; } = 2;
        public bool RememberEmail { get; set; }
        public string Email { get; set; } = "";
        public bool MonitoringEnabled { get; set; } = true;
        public bool HasShownWelcome { get; set; }
    }
}
