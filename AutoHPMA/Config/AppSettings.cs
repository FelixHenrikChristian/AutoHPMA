using AutoHPMA.GameTask.Permanent;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoHPMA.Config
{
    public class AppSettings
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AutoHPMA",
            "settings.json");

        // Dashboard
        [JsonPropertyName("logWindowEnabled")]
        public bool LogWindowEnabled { get; set; } = true;

        [JsonPropertyName("logWindowMarqueeEnabled")]
        public bool LogWindowMarqueeEnabled { get; set; } = true;

        [JsonPropertyName("debugLogEnabled")]
        public bool DebugLogEnabled { get; set; } = false;

        [JsonPropertyName("maskWindowEnabled")]
        public bool MaskWindowEnabled { get; set; } = true;

        [JsonPropertyName("maskWindowShowTextLabels")]
        public bool MaskWindowShowTextLabels { get; set; } = true;

        [JsonPropertyName("stateMonitorInterval")]
        public int StateMonitorInterval { get; set; } = 200;

        [JsonPropertyName("selectedOCR")]
        public string SelectedOCR { get; set; } = "PaddleOCR";

        // Task
        [JsonPropertyName("answerDelay")]
        public int AnswerDelay { get; set; } = 0;

        [JsonPropertyName("joinOthers")]
        public bool JoinOthers { get; set; } = false;

        [JsonPropertyName("stopWhenContributionFull")]
        public bool StopWhenContributionFull { get; set; } = false;

        [JsonPropertyName("autoForbiddenForestTimes")]
        public int AutoForbiddenForestTimes { get; set; } = 30;

        [JsonPropertyName("selectedTeamPosition")]
        public string SelectedTeamPosition { get; set; } = "Leader";

        [JsonPropertyName("autoCookingTimes")]
        public int AutoCookingTimes { get; set; } = 2;

        [JsonPropertyName("autoCookingSelectedDish")]
        public string AutoCookingSelectedDish { get; set; } = "海鱼黄金焗饭";

        // Notification
        [JsonPropertyName("notificationEnabled")]
        public bool NotificationEnabled { get; set; } = true;

        // Settings
        [JsonPropertyName("hasShownTermsOfUse")]
        public bool HasShownTermsOfUse { get; set; }

        [JsonPropertyName("theme")]
        public string Theme { get; set; } = "Light";

        [JsonPropertyName("language")]
        public string Language { get; set; } = "zh-CN";

        [JsonPropertyName("logFileLimit")]
        public int LogFileLimit { get; set; } = 10;

        // Other
        [JsonPropertyName("lastUsedPath")]
        public string LastUsedPath { get; set; } = string.Empty;

        [JsonPropertyName("isFirstRun")]
        public bool IsFirstRun { get; set; } = true;

        [JsonPropertyName("hotkeyBindings")]
        public Dictionary<string, string> HotkeyBindings { get; set; } = new();

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch (Exception)
            {
                // 如果读取失败，返回默认设置
            }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(SettingsPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory!);
                }

                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception)
            {
                // 处理保存失败的情况
            }
        }


        public void Clear()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    File.Delete(SettingsPath);
                }
            }
            catch (Exception)
            {
                // 处理删除失败的情况
            }
        }
    }
}