using System.IO;
using System.Text.Json;

namespace where_am_i.Services
{
    public sealed class UserPreferencesService
    {
        private const int CurrentVersion = 1;
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
                return preferences?.Version == CurrentVersion
                    ? preferences
                    : new UserPreferences();
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
            var preferences = new UserPreferences
            {
                Version = CurrentVersion,
                RememberEmail = true,
                Email = email.Trim()
            };

            Save(preferences);
        }

        public void ClearEmail()
        {
            Save(new UserPreferences());
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
        public int Version { get; set; } = 1;
        public bool RememberEmail { get; set; }
        public string Email { get; set; } = "";
    }
}
