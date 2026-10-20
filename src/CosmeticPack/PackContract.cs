using System;
using System.Collections.Generic;
using System.IO;

namespace Jinke.DredgeSkins
{
    [Serializable]
    public sealed class PackManifest
    {
        public int schemaVersion;
        public SkinDefinition[] skins;
    }

    [Serializable]
    public sealed class SkinDefinition
    {
        public string id;
        public string name;
        public string hullColor;
        public string roofColor;
        public string flagFile;
    }

    [Serializable]
    public sealed class SkinSettings
    {
        public int schemaVersion = 1;
        public string boat = PackContract.Original;
        public string flag = PackContract.Original;
    }

    /// <summary>Pure managed validation; does not load Unity, read a game save or invoke game code.</summary>
    public static class PackContract
    {
        public const string Original = "original";
        public const int MaxManifestBytes = 65536;
        public const int MaxPngBytes = 8 * 1024 * 1024;

        public static bool IsKnownId(string id)
        {
            return id == "ramen" || id == "octopus" || id == "moon";
        }

        public static void Validate(PackManifest manifest, string root)
        {
            if (manifest == null || manifest.schemaVersion != 1 || manifest.skins == null || manifest.skins.Length != 3)
                throw new InvalidDataException("皮肤清单必须为 schemaVersion 1，且包含 3 套皮肤。");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (SkinDefinition skin in manifest.skins)
            {
                if (skin == null || !IsKnownId(skin.id) || !ids.Add(skin.id))
                    throw new InvalidDataException("皮肤 ID 必须分别为 ramen / octopus / moon。");
                if (string.IsNullOrWhiteSpace(skin.name) || skin.name.Length > 24)
                    throw new InvalidDataException("皮肤名称缺失或过长。");
                if (!IsRgbHex(skin.hullColor) || !IsRgbHex(skin.roofColor))
                    throw new InvalidDataException("船色必须是 #RRGGBB。");
                ResolveFlagPath(root, skin.flagFile);
            }
        }

        public static string ResolveFlagPath(string root, string relative)
        {
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.IndexOf('\\') >= 0)
                throw new InvalidDataException("旗帜图片必须使用皮肤目录内的相对路径。");
            string directory = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(Path.Combine(directory, relative));
            if (!path.StartsWith(directory, StringComparison.Ordinal) || !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("旗帜图片必须是皮肤目录内的 PNG。");
            return path;
        }

        public static bool IsRgbHex(string text)
        {
            if (text == null || text.Length != 7 || text[0] != '#') return false;
            for (int i = 1; i < text.Length; i++)
            {
                char c = text[i];
                if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f') && !(c >= 'A' && c <= 'F')) return false;
            }
            return true;
        }

        public static string NormalizeSelection(string id)
        {
            return IsKnownId(id) ? id : Original;
        }

        public static void ValidatePng(byte[] png)
        {
            byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
            if (png == null || png.Length < 33 || png.Length > MaxPngBytes)
                throw new InvalidDataException("旗帜 PNG 文件大小不受支持。");
            for (int i = 0; i < signature.Length; i++)
                if (png[i] != signature[i]) throw new InvalidDataException("旗帜图片不是 PNG。");
            if (png[12] != 'I' || png[13] != 'H' || png[14] != 'D' || png[15] != 'R')
                throw new InvalidDataException("旗帜 PNG 缺少尺寸头。");
            int width = ReadBigEndian(png, 16);
            int height = ReadBigEndian(png, 20);
            if (width < 3 || height < 2 || width > 2048 || height > 2048 || width * 2 != height * 3)
                throw new InvalidDataException("旗面 PNG 必须为 3:2，且不超过 2048 像素。");
        }

        private static int ReadBigEndian(byte[] data, int offset)
        {
            return (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
        }
    }
}
