using System.IO;
using System.Text.Json;

namespace where_am_i.Services
{
    public sealed class UserPreferencesService
    {
        private const int CurrentVersion = 3;
        private readonly string settingsPath;

        public UserPreferencesService() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EFTLibrary", "WhereAmI", "settings.json")) { }

        internal UserPreferencesService(string path)
        {
            settingsPath = path;
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
                if (preferences.ScreenshotRetentionDays is < 1 or > 365)
                { preferences.ScreenshotRetentionDays = 3; preferences.ScreenshotCleanupEnabled = false; }
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

        public void MarkWelcomeShown()
        {
            var preferences = Load();
            preferences.HasShownWelcome = true;
            Save(preferences);
        }

        public void SaveScreenshotCleanup(bool enabled, int days, string? folder)
        {
            if (days is < 1 or > 365) throw new ArgumentOutOfRangeException(nameof(days));
            var preferences = Load();
            preferences.ScreenshotCleanupEnabled = enabled;
            preferences.ScreenshotRetentionDays = days;
            preferences.ScreenshotCleanupFolder = folder;
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
            var temporary = settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, json);
                File.Move(temporary, settingsPath, overwrite: true);
            }
            finally
            {
                try { File.Delete(temporary); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    public sealed class UserPreferences
    {
        public int Version { get; set; } = 3;
        public bool RememberEmail { get; set; }
        public string Email { get; set; } = "";
        public bool HasShownWelcome { get; set; }
        public bool ScreenshotCleanupEnabled { get; set; }
        public int ScreenshotRetentionDays { get; set; } = 3;
        public string? ScreenshotCleanupFolder { get; set; }
    }
}
