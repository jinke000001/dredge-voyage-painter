using System;
using System.IO;
using UnityEngine;

namespace Jinke.DredgeSkins
{
    internal static class SettingsStore
    {
        internal static string FilePath
        {
            get
            {
                string isolatedDirectory = Environment.GetEnvironmentVariable("JINKE_DREDGE_SKINS_SETTINGS_DIR");
                if (!string.IsNullOrEmpty(isolatedDirectory))
                {
                    if (!Path.IsPathRooted(isolatedDirectory)) throw new InvalidOperationException("测试外观设置目录必须为绝对路径。");
                    return Path.Combine(Path.GetFullPath(isolatedDirectory), "settings.json");
                }
                string directory = Application.platform == RuntimePlatform.OSXPlayer
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
                    : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(directory)) throw new InvalidOperationException("无法定位独立外观设置目录。");
                return Path.Combine(directory, "JinkeDredgeSkins", "settings.json");
            }
        }

        internal static SkinSettings Read()
        {
            return SkinJson.ReadSettings(FilePath);
        }

        internal static void Write(SkinSettings settings)
        {
            SkinJson.WriteSettings(FilePath, settings);
        }
    }
}
