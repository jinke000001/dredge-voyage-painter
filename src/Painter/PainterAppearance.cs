using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Jinke.DredgeSkins;
using Newtonsoft.Json;
using UnityEngine;

namespace Jinke.DredgePainter
{
    internal static class PainterAppearance
    {
        private static readonly Dictionary<Renderer, RendererOverrides> overrides = new Dictionary<Renderer, RendererOverrides>();
        private static readonly HashSet<Renderer> boats = new HashSet<Renderer>();
        private static readonly HashSet<Renderer> flags = new HashSet<Renderer>();
        private static SaveData data;
        private static PainterState state;
        private static GameEvents events;
        private static Player boundPlayer;
        private static PlayerColorCustomizer customizer;
        private static SkinAssets assets;
        private static bool failed, enteredWorld;
        private static float nextCleanup;
        private static string lastError;

        internal static bool Ready
        {
            get
            {
                GameManager game = GameManager.Instance;
                return !failed && IsCurrent() && assets != null && game != null && game.IsPlaying && boundPlayer != null && boundPlayer == game.Player &&
                    customizer != null && boats.Count > 0 && flags.Count > 0;
            }
        }

        internal static void OnSaveReady(SaveData save)
        {
            try
            {
                GameManager game = GameManager.Instance;
                if (game == null || save == null || !ReferenceEquals(save, game.SaveData)) return;
                if (!ReferenceEquals(data, save))
                {
                    ResetSession();
                    data = save;
                    state = new PainterState(save);
                }
                Subscribe();
            }
            catch (Exception error) { Fail(error); }
        }

        internal static void OnColors(object owner) { Register(owner, false); }
        internal static void OnFlag(object owner) { Register(owner, true); }

        private static void Register(object owner, bool flag)
        {
            try
            {
                var candidate = owner as PlayerColorCustomizer;
                GameManager game = GameManager.Instance;
                if (candidate == null || game == null || game.SaveData == null) return;
                Player player = candidate.GetComponentInParent<Player>();
                if (player == null || (game.Player != null && player != game.Player)) return;
                if (boundPlayer != null && boundPlayer != player) ResetSession();
                OnSaveReady(game.SaveData);
                if (failed || state == null) return;
                if (customizer != null && customizer != candidate) ClearRenderers();
                boundPlayer = player;
                customizer = candidate;
                enteredWorld |= game.IsPlaying;
                EnsureAssets();
                if (flag) RegisterField(candidate, "flagMeshRenderers", flags);
                else
                {
                    RegisterField(candidate, "meshRenderers", boats);
                    RegisterField(candidate, "buntingMeshRenderers", boats);
                }
                if (flag) ApplyFlags(); else ApplyColors();
            }
            catch (Exception error) { Fail(error); }
        }

        private static void RegisterField(PlayerColorCustomizer owner, string name, HashSet<Renderer> destination)
        {
            FieldInfo field = typeof(PlayerColorCustomizer).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            IEnumerable renderers = field == null ? null : field.GetValue(owner) as IEnumerable;
            if (renderers == null) throw new InvalidOperationException("原版外观接口不可用：" + name);
            foreach (object value in renderers)
            {
                Renderer renderer = value as Renderer;
                if (renderer == null) continue;
                if (!overrides.ContainsKey(renderer)) overrides.Add(renderer, new RendererOverrides(renderer));
                destination.Add(renderer);
            }
        }

        private static void EnsureAssets()
        {
            if (assets != null) return;
            var loaded = new SkinAssets();
            try { loaded.Load(Path.Combine(Application.streamingAssetsPath, "JinkeSkins")); }
            catch { loaded.Dispose(); throw; }
            assets = loaded;
        }

        private static void ApplyColors()
        {
            if (failed || assets == null || state == null) return;
            LoadedSkin hull = assets.Find(state.Color(1)), roof = assets.Find(state.Color(0));
            foreach (Renderer renderer in boats)
                if (renderer != null) overrides[renderer].ApplyPalette(hull, roof);
        }

        private static void ApplyFlags()
        {
            if (failed || assets == null || state == null) return;
            LoadedSkin flag = assets.Find(state.Flag);
            foreach (Renderer renderer in flags)
                if (renderer != null) overrides[renderer].ApplyFlag(flag, assets);
        }

        private static void Subscribe()
        {
            if (events == GameEvents.Instance) return;
            Unsubscribe();
            events = GameEvents.Instance;
            if (events == null) return;
            events.OnBoatColorsChanged += OnNativeColor;
            events.OnBoatFlagChanged += OnNativeFlag;
        }

        private static bool IsCurrent()
        {
            GameManager game = GameManager.Instance;
            return game != null && state != null && ReferenceEquals(data, game.SaveData);
        }

        private static void OnNativeColor(int area, int index)
        {
            try
            {
                if (!IsCurrent()) return;
                state.ClearColor(area);
                ApplyColors();
            }
            catch (Exception error) { Fail(error); }
        }

        private static void OnNativeFlag(int index)
        {
            try
            {
                if (!IsCurrent()) return;
                state.ClearFlag();
                ApplyFlags();
            }
            catch (Exception error) { Fail(error); }
        }

        internal static bool ChangeColor(int area, string skinId)
        {
            GameManager game;
            if ((area != 0 && area != 1) || !TryCommand(skinId, out game)) return false;
            SaveData save = data;
            PainterState selection = state;
            PainterState.Selection previous = selection.CaptureColor(area);
            int index = area == 0 ? save.RoofColorIndex : save.HullColorIndex;
            bool changed = save.HasChangedBoatColors;
            try
            {
                // The native event first clears this area's extension, even when the index is unchanged.
                game.DialogueRunner.ChangeBoatColor(area, index);
                RequireCommandSession(save);
                selection.SetColor(area, skinId);
                ApplyColors();
                Debug.Log("[Painter] COLOR_SELECTED area=" + area + " skin=" + skinId);
                return true;
            }
            catch (Exception error)
            {
                if (area == 0) save.RoofColorIndex = index; else save.HullColorIndex = index;
                save.HasChangedBoatColors = changed;
                selection.RestoreColor(area, previous);
                Fail(error);
                return false;
            }
        }

        internal static bool ChangeFlag(string skinId)
        {
            GameManager game;
            if (!TryCommand(skinId, out game)) return false;
            SaveData save = data;
            PainterState selection = state;
            PainterState.Selection previous = selection.CaptureFlag();
            int index = save.BoatFlagStyle;
            int fallback = index > 0 && save.GetBoolVariable("has-flag-" + index) ? index : 3;
            if (!save.GetBoolVariable("has-flag-" + fallback)) return Reject("请先取得原版旗帜。");
            try
            {
                game.DialogueRunner.ChangeBoatFlag(fallback);
                RequireCommandSession(save);
                selection.SetFlag(skinId);
                ApplyFlags();
                Debug.Log("[Painter] FLAG_SELECTED skin=" + skinId);
                return true;
            }
            catch (Exception error)
            {
                // Restore visibility/material through the native path before restoring the extension key.
                try { if (ReferenceEquals(game.SaveData, save)) game.DialogueRunner.ChangeBoatFlag(index); }
                catch (Exception rollbackError) { Debug.LogError("[Painter] FLAG_ROLLBACK_FAILED " + rollbackError); }
                save.BoatFlagStyle = index;
                selection.RestoreFlag(previous);
                Fail(error);
                return false;
            }
        }

        private static bool TryCommand(string skinId, out GameManager game)
        {
            game = GameManager.Instance;
            if (!PackContract.IsKnownId(skinId)) return Reject("无法识别这款外观。");
            if (game == null || !game.IsPlaying || game.Player == null || !game.Player.IsAlive || !game.Player.IsDocked ||
                game.SaveData == null || game.SaveManager == null || !ReferenceEquals(game.SaveData, game.SaveManager.ActiveSaveData) ||
                game.SaveData.ForbidSave || game.DialogueRunner == null || game.SaveData.availableDestinations == null ||
                !game.SaveData.availableDestinations.Contains("destination.lm-painter")) return Reject("请在已解锁的油漆匠处改装外观。");
            if (!ReferenceEquals(data, game.SaveData)) OnSaveReady(game.SaveData);
            if (!Ready)
                return Reject("外观素材尚未准备好，已保留原版。");
            if (data.RoofColorIndex < 0 || data.RoofColorIndex > 7 || data.HullColorIndex < 0 || data.HullColorIndex > 7 ||
                data.BoatFlagStyle < 0 || data.BoatFlagStyle > 7) return Reject("当前原版外观索引不受支持。");
            lastError = null;
            return true;
        }

        private static void RequireCommandSession(SaveData save)
        {
            if (failed || !IsCurrent() || !ReferenceEquals(save, data)) throw new InvalidOperationException("外观会话已改变，未保存本次选择。");
        }

        private static bool Reject(string reason)
        {
            lastError = reason;
            Debug.LogWarning("[Painter] CHOICE_REJECTED " + reason);
            return false;
        }

        internal static void Tick()
        {
            try
            {
                GameManager game = GameManager.Instance;
                if (game == null || !game.IsPlaying || game.Player == null)
                {
                    if (enteredWorld) ResetSession();
                    return;
                }
                if (!ReferenceEquals(data, game.SaveData)) OnSaveReady(game.SaveData);
                enteredWorld = true;
                Subscribe();
                if (Time.unscaledTime < nextCleanup || failed) return;
                nextCleanup = Time.unscaledTime + 10f;
                var dead = new List<Renderer>();
                foreach (Renderer renderer in overrides.Keys) if (renderer == null) dead.Add(renderer);
                foreach (Renderer renderer in dead) { overrides.Remove(renderer); boats.Remove(renderer); flags.Remove(renderer); }
                if (assets != null) assets.PruneAtlases();
            }
            catch (Exception error) { Fail(error); }
        }

        private static void ClearRenderers()
        {
            foreach (RendererOverrides value in overrides.Values)
                try { value.Restore(); } catch (Exception error) { Debug.LogWarning("[Painter] RESTORE_FAILED " + error.Message); }
            overrides.Clear(); boats.Clear(); flags.Clear();
        }

        private static void Fail(Exception error)
        {
            if (!failed) Debug.LogError("[Painter] APPEARANCE_DISABLED " + error);
            failed = true;
            lastError = error.Message;
            ClearRenderers();
            DisposeAssets();
        }

        private static void Unsubscribe()
        {
            if (events != null)
            {
                events.OnBoatColorsChanged -= OnNativeColor;
                events.OnBoatFlagChanged -= OnNativeFlag;
            }
            events = null;
        }

        internal static void ResetSession()
        {
            Unsubscribe();
            ClearRenderers();
            DisposeAssets();
            data = null; state = null; boundPlayer = null; customizer = null;
            failed = enteredWorld = false; nextCleanup = 0; lastError = null;
        }

        private static void DisposeAssets()
        {
            if (assets == null) return;
            try { assets.Dispose(); }
            catch (Exception error) { Debug.LogWarning("[Painter] ASSET_CLEANUP_FAILED " + error.Message); }
            finally { assets = null; }
        }

        internal static string Diagnostics()
        {
            return JsonConvert.SerializeObject(new { ready = Ready,
                roof = state == null ? PackContract.Original : state.Color(0), hull = state == null ? PackContract.Original : state.Color(1),
                flag = state == null ? PackContract.Original : state.Flag, nativeRoof = data == null ? -1 : data.RoofColorIndex,
                nativeHull = data == null ? -1 : data.HullColorIndex, nativeFlag = data == null ? -1 : data.BoatFlagStyle,
                boatRenderers = boats.Count, flagRenderers = flags.Count, atlasCache = assets == null ? 0 : assets.AtlasCount, error = lastError });
        }
    }
}
