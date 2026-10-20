using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jinke.DredgeSkins
{
    internal sealed class SkinWindow : IDisposable
    {
        private readonly SkinController controller;
        private Font font;
        private GUIStyle title;
        private GUIStyle label;
        private GUIStyle small;
        private GUIStyle button;
        private GUIStyle selectedButton;
        private readonly Color ink = new Color(0.91f, 0.89f, 0.81f);
        private readonly Color muted = new Color(0.67f, 0.73f, 0.72f);
        private readonly Color accent = new Color(0.84f, 0.67f, 0.35f);

        internal SkinWindow(SkinController controller) { this.controller = controller; }
        internal static float Scale { get { return Mathf.Clamp(Mathf.Min(Screen.width / 560f, Screen.height / 650f), 0.5f, 1.5f); } }

        private void EnsureStyles()
        {
            if (title != null) return;
            try { font = Font.CreateDynamicFontFromOSFont(new[] { "PingFang SC", "Hiragino Sans GB", "Microsoft YaHei", "Noto Sans CJK SC", "Arial" }, 18); }
            catch (Exception exception) { SkinLog.Warning("[JinkeSkins] System font fallback: " + exception.Message); }
            title = TextStyle(21, ink, FontStyle.Bold);
            label = TextStyle(17, ink, FontStyle.Normal);
            small = TextStyle(13, muted, FontStyle.Normal);
            small.wordWrap = true;
            button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 14, alignment = TextAnchor.MiddleCenter };
            button.normal.textColor = ink;
            button.hover.textColor = Color.white;
            button.focused.textColor = Color.white;
            selectedButton = new GUIStyle(button);
            selectedButton.normal.textColor = accent;
            selectedButton.fontStyle = FontStyle.Bold;
        }

        private GUIStyle TextStyle(int size, Color color, FontStyle weight)
        {
            var style = new GUIStyle(GUI.skin.label) { font = font, fontSize = size, fontStyle = weight, alignment = TextAnchor.MiddleLeft };
            style.normal.textColor = color;
            return style;
        }

        internal void Draw()
        {
            Matrix4x4 oldMatrix = GUI.matrix;
            Color oldColor = GUI.color;
            Color oldBackground = GUI.backgroundColor;
            bool oldEnabled = GUI.enabled;
            int oldDepth = GUI.depth;
            try
            {
                EnsureStyles();
                GUI.depth = -1000;
                GUI.color = Color.white;
                GUI.backgroundColor = new Color(0.35f, 0.44f, 0.45f);
                float scale = Scale;
                GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
                float width = Screen.width / scale;
                float height = Screen.height / scale;
                if (!controller.SafeToShow)
                {
                    if (Time.realtimeSinceStartup < controller.HintUntil)
                    {
                        Fill(new Rect(width - 336, 68, 320, 44), new Color(0.055f, 0.105f, 0.12f, 0.98f));
                        GUI.Label(new Rect(width - 324, 74, 300, 30), controller.HintText, label);
                    }
                    return;
                }
                if (!controller.WindowOpen && GUI.Button(new Rect(width - 144, 16, 128, 32), "外观舱  [F8]", button)) controller.ToggleWindow();
                if (!controller.WindowOpen && Time.realtimeSinceStartup < controller.HintUntil)
                {
                    Fill(new Rect(width - 336, 68, 320, 44), new Color(0.055f, 0.105f, 0.12f, 0.98f));
                    GUI.Label(new Rect(width - 324, 74, 300, 30), controller.HintText, label);
                }
                if (!controller.WindowOpen) return;
                Rect panel = new Rect((width - 496) / 2f, (height - 582) / 2f, 496, 582);
                Fill(panel, new Color(0.055f, 0.105f, 0.12f, 0.98f));
                Fill(new Rect(panel.x, panel.y, panel.width, 3), accent);
                GUI.BeginGroup(panel);
                try { DrawContent(); }
                finally { GUI.EndGroup(); }
            }
            finally
            {
                GUI.matrix = oldMatrix;
                GUI.color = oldColor;
                GUI.backgroundColor = oldBackground;
                GUI.enabled = oldEnabled;
                GUI.depth = oldDepth;
            }
        }

        private void DrawContent()
        {
            GUI.Label(new Rect(24, 16, 380, 34), "船长的外观舱", title);
            if (GUI.Button(new Rect(432, 18, 40, 30), "×", button)) controller.SetWindow(false);
            GUI.Label(new Rect(24, 53, 446, 44), "仅在原游戏暂停时调整。Esc 关闭外观舱，F8 切换；旗帜需先在游戏中装备，此处不解锁。", small);
            DrawRow(102, null);
            for (int i = 0; i < controller.Assets.Skins.Count; i++) DrawRow(168 + i * 98, controller.Assets.Skins[i]);
            GUI.Label(new Rect(24, 474, 448, 49), controller.Notice, small);
            GUI.enabled = true;
            if (GUI.Button(new Rect(24, 534, 220, 30), "全部恢复原版", button))
                controller.Select(PackContract.Original, PackContract.Original, true);
            if (GUI.Button(new Rect(256, 534, 216, 30), "完成  [F8 关闭]", button)) controller.SetWindow(false);
        }

        private void DrawRow(float y, LoadedSkin skin)
        {
            bool original = skin == null;
            string id = original ? PackContract.Original : skin.Definition.id;
            string name = original ? "原版外观" : skin.Definition.name;
            float rowHeight = original ? 56 : 88;
            Fill(new Rect(24, y, 448, rowHeight), new Color(0.10f, 0.16f, 0.17f));
            float contentX = original ? 36 : 154;
            if (!original)
            {
                GUI.DrawTexture(new Rect(36, y + 12, 102, 68), skin.Preview, ScaleMode.ScaleToFit, true);
                Fill(new Rect(145, y + 14, 3, 28), skin.Hull);
                Fill(new Rect(145, y + 46, 3, 28), skin.Roof);
            }
            GUI.Label(new Rect(contentX, y + 5, 294, 28), name, label);
            float buttonY = y + (original ? 23 : 43);
            float boatX = original ? 215 : contentX;
            float flagX = original ? 339 : contentX + 154;
            float buttonWidth = original ? 112 : 142;
            GUI.enabled = original || controller.Available;
            Choice(new Rect(boatX, buttonY, buttonWidth, 30), "船色", id, false);
            Choice(new Rect(flagX, buttonY, buttonWidth, 30), "旗帜", id, true);
            GUI.enabled = true;
        }

        private void Choice(Rect rect, string text, string id, bool flag)
        {
            string current = flag ? controller.Settings.flag : controller.Settings.boat;
            bool selected = current == id;
            if (GUI.Button(rect, selected ? "✓ " + text + " · 已选" : "使用" + text, selected ? selectedButton : button) && !selected)
                controller.Select(flag ? controller.Settings.boat : id, flag ? id : controller.Settings.flag, true);
        }

        private static void Fill(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        public void Dispose() { if (font != null) Object.Destroy(font); }
    }
}
