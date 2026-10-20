using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jinke.DredgeSkins
{
    internal sealed class LoadedSkin
    {
        internal SkinDefinition Definition;
        internal Color Hull;
        internal Color Roof;
        internal Texture2D Preview;
        internal byte[] FlagPixels;
    }

    internal sealed class SkinAssets : IDisposable
    {
        private sealed class AtlasEntry
        {
            internal Texture Source;
            internal Texture2D Output;
        }
        internal readonly List<LoadedSkin> Skins = new List<LoadedSkin>();
        private readonly Dictionary<string, AtlasEntry> atlases = new Dictionary<string, AtlasEntry>();
        internal int AtlasCount { get { return atlases.Count; } }

        internal void Load(string directory)
        {
            PackManifest manifest = SkinJson.LoadManifest(directory);
            foreach (SkinDefinition definition in manifest.skins)
            {
                string path = PackContract.ResolveFlagPath(directory, definition.flagFile);
                if (new FileInfo(path).Length > PackContract.MaxPngBytes)
                    throw new InvalidDataException("旗帜 PNG 过大。");
                byte[] bytes = File.ReadAllBytes(path);
                PackContract.ValidatePng(bytes);
                var skin = new LoadedSkin { Definition = definition };
                ColorUtility.TryParseHtmlString(definition.hullColor, out skin.Hull);
                ColorUtility.TryParseHtmlString(definition.roofColor, out skin.Roof);
                Texture2D source = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
                try
                {
                    if (!ImageConversion.LoadImage(source, bytes, true)) throw new InvalidDataException("无法读取旗帜 PNG。");
                    // The game cloth uses the upper 256 x 180 atlas pixels. Keep a modest reusable UI/source image.
                    skin.Preview = CopyReadable(source, 384, 256);
                    skin.Preview.name = "JinkeSkins-" + definition.id;
                    skin.FlagPixels = ToRgba(skin.Preview.GetPixels32());
                    skin.Preview.Apply(false, true);
                    Skins.Add(skin);
                }
                catch
                {
                    if (skin.Preview != null) Object.Destroy(skin.Preview);
                    throw;
                }
                finally { Object.Destroy(source); }
            }
        }

        internal LoadedSkin Find(string id)
        {
            return Skins.Find(s => s.Definition.id == id);
        }

        internal Texture2D GetAtlas(Texture original, LoadedSkin skin)
        {
            if (original == null || original.width != AtlasLayout.Width || original.height != AtlasLayout.Height)
                throw new InvalidDataException("原游戏旗帜图集尺寸与已验证版本不一致。");
            string key = original.GetInstanceID() + ":" + skin.Definition.id;
            AtlasEntry cached;
            if (atlases.TryGetValue(key, out cached))
            {
                if (cached.Source == original && cached.Output != null) return cached.Output;
                if (cached.Output != null) Object.Destroy(cached.Output);
                atlases.Remove(key);
            }
            PruneAtlases();
            if (atlases.Count >= 48) throw new InvalidOperationException("旗帜图集缓存已达安全上限。");
            Texture2D atlas = CopyReadable(original, AtlasLayout.Width, AtlasLayout.Height);
            try
            {
                byte[] pixels = AtlasLayout.Compose(ToRgba(atlas.GetPixels32()), skin.FlagPixels, 384, 256);
                atlas.LoadRawTextureData(pixels);
                atlas.name = "JinkeSkins-atlas-" + key;
                atlas.wrapMode = original.wrapMode;
                atlas.filterMode = original.filterMode;
                atlas.anisoLevel = original.anisoLevel;
                atlas.Apply(false, true);
                atlases[key] = new AtlasEntry { Source = original, Output = atlas };
                return atlas;
            }
            catch { Object.Destroy(atlas); throw; }
        }

        internal void PruneAtlases()
        {
            var dead = new List<string>();
            foreach (KeyValuePair<string, AtlasEntry> entry in atlases)
                if (entry.Value.Source == null || entry.Value.Output == null) dead.Add(entry.Key);
            foreach (string key in dead)
            {
                if (atlases[key].Output != null) Object.Destroy(atlases[key].Output);
                atlases.Remove(key);
            }
        }

        private static byte[] ToRgba(Color32[] pixels)
        {
            byte[] bytes = new byte[pixels.Length * 4];
            for (int i = 0; i < pixels.Length; i++)
            {
                bytes[i * 4] = pixels[i].r;
                bytes[i * 4 + 1] = pixels[i].g;
                bytes[i * 4 + 2] = pixels[i].b;
                bytes[i * 4 + 3] = pixels[i].a;
            }
            return bytes;
        }

        private static Texture2D CopyReadable(Texture source, int width, int height)
        {
            RenderTexture previous = RenderTexture.active;
            bool previousSrgb = GL.sRGBWrite;
            RenderTexture temporary = null;
            Texture2D result = null;
            try
            {
                temporary = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                Graphics.Blit(source, temporary);
                RenderTexture.active = temporary;
                result = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
                result.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                result.Apply(false, false);
                return result;
            }
            catch { if (result != null) Object.Destroy(result); throw; }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = previousSrgb;
                if (temporary != null) RenderTexture.ReleaseTemporary(temporary);
            }
        }

        public void Dispose()
        {
            foreach (AtlasEntry atlas in atlases.Values) if (atlas.Output != null) Object.Destroy(atlas.Output);
            atlases.Clear();
            foreach (LoadedSkin skin in Skins) if (skin.Preview != null) Object.Destroy(skin.Preview);
            Skins.Clear();
        }
    }
}
