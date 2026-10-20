using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace Jinke.DredgeSkins
{
    public sealed class SkinController : MonoBehaviour
    {
        private readonly Dictionary<Renderer, RendererOverrides> overrides = new Dictionary<Renderer, RendererOverrides>();
        private readonly HashSet<Renderer> boats = new HashSet<Renderer>();
        private readonly HashSet<Renderer> flags = new HashSet<Renderer>();
        private readonly Dictionary<Type, Dictionary<string, FieldInfo>> fields = new Dictionary<Type, Dictionary<string, FieldInfo>>();
        internal readonly SkinAssets Assets = new SkinAssets();
        internal SkinSettings Settings = new SkinSettings();
        internal string Notice = "关闭外观舱后，继续使用原游戏的暂停菜单。";
        internal bool Available;
        internal bool WindowOpen;
        internal bool SafeToShow;
        internal float HintUntil;
        internal string HintText = "先按 Esc 暂停，再按 F8";
        private SkinWindow window;
        private PauseUiGuard uiGuard;
        private float nextCleanup;
        private bool failed;
        internal string Diagnostics
        {
            get { return "JinkeSkins: ready=" + Available + "; boat=" + Settings.boat + "; flag=" + Settings.flag + "; boatRenderers=" + boats.Count + "; flagRenderers=" + flags.Count + "; atlasCache=" + Assets.AtlasCount + "; window=" + WindowOpen + "; safePausedUi=" + SafeToShow + "; settings=" + SettingsStore.FilePath; }
        }

        private void Awake()
        {
            window = new SkinWindow(this);
            uiGuard = new PauseUiGuard(transform);
            try
            {
                string assetRoot = Path.Combine(Application.streamingAssetsPath, "JinkeSkins");
                SkinLog.Info("[JinkeSkins] INITIALIZE assetRoot=" + assetRoot);
                Assets.Load(assetRoot);
                Available = true;
                try { Settings = SettingsStore.Read(); }
                catch (Exception exception)
                {
                    Notice = "外观设置无法读取，已使用原版。";
                    SkinLog.Warning("[JinkeSkins] Settings ignored: " + exception.Message);
                }
                SkinLog.Info("[JinkeSkins] READY v1.0.0; 3 skins; F8; cosmetic properties only; " + Diagnostics);
            }
            catch (Exception exception) { FailSafe(exception); }
        }

        internal void Register(object owner, bool flag)
        {
            Component component = owner as Component;
            if (component == null) return;
            if (uiGuard != null) uiGuard.Bind(owner.GetType().Assembly);
            if (flag) RegisterField(owner, "flagMeshRenderers", flags);
            else
            {
                RegisterField(owner, "meshRenderers", boats);
                RegisterField(owner, "buntingMeshRenderers", boats);
            }
            Apply(flag ? flags : boats, flag);
        }

        private void RegisterField(object owner, string fieldName, HashSet<Renderer> destination)
        {
            Type type = owner.GetType();
            Dictionary<string, FieldInfo> byName;
            if (!fields.TryGetValue(type, out byName)) { byName = new Dictionary<string, FieldInfo>(); fields.Add(type, byName); }
            FieldInfo field;
            if (!byName.TryGetValue(fieldName, out field))
            {
                field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
                if (field == null) throw new MissingFieldException(type.FullName, fieldName);
                byName.Add(fieldName, field);
            }
            IEnumerable renderers = field.GetValue(owner) as IEnumerable;
            if (renderers == null) throw new InvalidOperationException("Cosmetic renderer list is unavailable: " + fieldName);
            foreach (object value in renderers)
            {
                Renderer renderer = value as Renderer;
                if (renderer == null) continue;
                destination.Add(renderer);
                if (!overrides.ContainsKey(renderer)) overrides.Add(renderer, new RendererOverrides(renderer));
            }
        }

        private void Apply(HashSet<Renderer> targets, bool flag)
        {
            LoadedSkin skin = Available ? Assets.Find(flag ? Settings.flag : Settings.boat) : null;
            foreach (Renderer renderer in targets)
            {
                if (renderer == null) continue;
                RendererOverrides state = overrides[renderer];
                // Hidden flag objects stay hidden. This class never calls SetActive or changes enabled.
                if (flag) state.ApplyFlag(skin, Assets);
                else state.ApplyColors(skin);
            }
        }

        internal void Select(string boat, string flag, bool persist)
        {
            Settings.boat = Available ? PackContract.NormalizeSelection(boat) : PackContract.Original;
            Settings.flag = Available ? PackContract.NormalizeSelection(flag) : PackContract.Original;
            try { Apply(boats, false); Apply(flags, true); }
            catch (Exception exception) { FailSafe(exception); return; }
            SkinLog.Info("[JinkeSkins] SELECTION boat=" + Settings.boat + " flag=" + Settings.flag + " persist=" + persist);
            if (!persist) return;
            try { SettingsStore.Write(Settings); Notice = "已记住外观选择。游戏进度不受影响。"; }
            catch (Exception exception)
            {
                Notice = "外观已应用，但无法保存偏好；本次游玩仍可使用。";
                SkinLog.Warning("[JinkeSkins] Sidecar write failed: " + exception.Message);
            }
        }

        internal void FailSafe(Exception exception)
        {
            Available = false;
            WindowOpen = false;
            if (uiGuard != null) uiGuard.Dispose();
            Settings = new SkinSettings();
            foreach (RendererOverrides state in overrides.Values)
            {
                try { state.Restore(); }
                catch (Exception restoreError) { SkinLog.Warning("[JinkeSkins] Restore failed: " + restoreError.Message); }
            }
            if (!failed) SkinLog.Error("[JinkeSkins] FAILSAFE: kept original cosmetics. " + exception);
            failed = true;
            Notice = "资源未加载，已保留原版。详见 cosmetics.log（与外观 settings.json 同目录）。";
        }

        private void Update()
        {
            try
            {
                SyncUi();
                if (Input.GetKeyDown(KeyCode.F8)) ToggleWindow();
                else if (WindowOpen && Input.GetKeyDown(KeyCode.Escape)) SetWindow(false);
            }
            catch (Exception exception) { FailSafe(exception); }
            if (Time.realtimeSinceStartup < nextCleanup) return;
            nextCleanup = Time.realtimeSinceStartup + 10f;
            var dead = new List<Renderer>();
            foreach (Renderer renderer in overrides.Keys) if (renderer == null) dead.Add(renderer);
            foreach (Renderer renderer in dead) { overrides.Remove(renderer); boats.Remove(renderer); flags.Remove(renderer); }
            Assets.PruneAtlases();
        }

        private void OnGUI()
        {
            if (window == null) return;
            try { SyncUi(); window.Draw(); }
            catch (Exception exception)
            {
                FailSafe(exception);
                window.Dispose();
                window = null;
                if (uiGuard != null) uiGuard.Dispose();
                uiGuard = null;
                SafeToShow = false;
            }
        }
        internal void ToggleWindow()
        {
            if (!SafeToShow) { HintText = "先按 Esc 暂停，再按 F8"; HintUntil = Time.realtimeSinceStartup + 4f; return; }
            if (!WindowOpen && PauseUiGuard.PointerHeld)
            {
                HintText = "先松开鼠标或触控，再按 F8";
                HintUntil = Time.realtimeSinceStartup + 4f;
                return;
            }
            SetWindow(!WindowOpen);
        }

        internal void SetWindow(bool open)
        {
            WindowOpen = open && uiGuard != null && uiGuard.IsSafe && !PauseUiGuard.PointerHeld;
            SyncUi();
        }

        private void SyncUi()
        {
            SafeToShow = uiGuard != null && uiGuard.IsSafe;
            if (!SafeToShow) WindowOpen = false;
            if (uiGuard != null) uiGuard.Sync(WindowOpen, SkinWindow.Scale);
        }
        private void OnApplicationQuit() { Bootstrap.OnQuitting(); }
        private void OnDisable()
        {
            WindowOpen = false;
            if (uiGuard != null) uiGuard.Dispose();
        }
        private void OnDestroy()
        {
            if (uiGuard != null) uiGuard.Dispose();
            foreach (RendererOverrides state in overrides.Values)
                try { state.Restore(); } catch (Exception exception) { SkinLog.Warning("[JinkeSkins] Cleanup: " + exception.Message); }
            Assets.Dispose();
            if (window != null) window.Dispose();
            Bootstrap.OnDestroyed(this);
        }
    }
}
