using System;
using Jinke.DredgeSkins;

namespace Jinke.DredgePainter
{
    // Only ordinary strings are added to the existing save dictionaries. Native indices remain native.
    internal sealed class PainterState
    {
        internal const string RoofKey = "jinke-painter-roof-v1";
        internal const string HullKey = "jinke-painter-hull-v1";
        internal const string FlagKey = "jinke-painter-flag-v1";
        private readonly SaveData data;

        internal struct Selection
        {
            internal bool Exists;
            internal string Value;
        }

        internal PainterState(SaveData save)
        {
            if (save == null || save.stringVariables == null) throw new InvalidOperationException("外观存档尚未准备好。");
            data = save;
        }

        internal string Color(int area) { return Read(ColorKey(area)); }
        internal string Flag { get { return Read(FlagKey); } }
        internal void SetColor(int area, string id) { Write(ColorKey(area), id); }
        internal void SetFlag(string id) { Write(FlagKey, id); }
        internal void ClearColor(int area)
        {
            if (area == 0 || area == 1) data.stringVariables.Remove(ColorKey(area));
        }
        internal void ClearFlag() { data.stringVariables.Remove(FlagKey); }
        internal Selection CaptureColor(int area) { return Capture(ColorKey(area)); }
        internal Selection CaptureFlag() { return Capture(FlagKey); }
        internal void RestoreColor(int area, Selection selection) { Restore(ColorKey(area), selection); }
        internal void RestoreFlag(Selection selection) { Restore(FlagKey, selection); }

        private string Read(string key)
        {
            string value;
            return data.stringVariables.TryGetValue(key, out value) && PackContract.IsKnownId(value) ? value : PackContract.Original;
        }
        private void Write(string key, string id)
        {
            if (!PackContract.IsKnownId(id)) throw new ArgumentException("无法识别这款外观。", "id");
            data.stringVariables[key] = id;
        }
        private Selection Capture(string key)
        {
            string value;
            bool exists = data.stringVariables.TryGetValue(key, out value);
            return new Selection { Exists = exists, Value = value };
        }
        private void Restore(string key, Selection selection)
        {
            if (selection.Exists) data.stringVariables[key] = selection.Value;
            else data.stringVariables.Remove(key);
        }
        private static string ColorKey(int area)
        {
            if (area == 0) return RoofKey;
            if (area == 1) return HullKey;
            throw new ArgumentOutOfRangeException("area");
        }
    }
}
