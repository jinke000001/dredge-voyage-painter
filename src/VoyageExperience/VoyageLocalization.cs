using System;
using System.Collections.Generic;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace Jinke.DredgeVoyage
{
    internal static class VoyageLocalization
    {
        private static readonly Dictionary<string, string[]> entries = new Dictionary<string, string[]>();
        internal static string Key(string key, string zh, string en)
        {
            string full = "jinke.voyage." + key;
            entries[full] = new[] { zh, en };
            Write(full, zh, en);
            return full;
        }
        internal static LocalizedString Text(string key, string zh, string en)
        { return new LocalizedString(LanguageManager.STRING_TABLE, Key(key, zh, en)); }
        internal static void Refresh()
        { foreach (var pair in entries) Write(pair.Key, pair.Value[0], pair.Value[1]); }
        private static void Write(string key, string zh, string en)
        {
            var locale = LocalizationSettings.SelectedLocale;
            if (locale == null) throw new InvalidOperationException("游戏语言尚未准备好。");
            var table = LocalizationSettings.StringDatabase.GetTable(LanguageManager.STRING_TABLE, locale);
            if (table == null) throw new InvalidOperationException("原版文字表尚未准备好。");
            string value = locale.Identifier.Code.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? zh : en;
            var entry = table.GetEntry(key);
            if (entry == null) table.AddEntry(key, value); else entry.Value = value;
        }
    }
}
