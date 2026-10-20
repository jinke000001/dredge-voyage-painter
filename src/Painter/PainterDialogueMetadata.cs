using System;
using System.Collections.Generic;
using System.Reflection;
using Yarn.Unity;

namespace Jinke.DredgePainter
{
    internal static class PainterDialogueMetadata
    {
        internal static LineMetadata CopyWithEntries(LineMetadata source)
        {
            if (source == null) throw new InvalidOperationException("原版对话标签尚未准备好。");
            FieldInfo field = typeof(LineMetadata).GetField("_lineMetadata", BindingFlags.NonPublic | BindingFlags.Instance);
            var original = field == null ? null : field.GetValue(source) as IDictionary<string, string>;
            if (original == null) throw new InvalidOperationException("原版对话标签接口不匹配。");
            var table = Activator.CreateInstance(field.FieldType, true) as IDictionary<string, string>;
            if (table == null) throw new InvalidOperationException("无法复制原版对话标签。");
            foreach (var entry in original) table.Add(entry.Key, entry.Value);
            foreach (PainterDialogueProgram.Entry entry in PainterDialogueProgram.Entries)
            {
                if (table.ContainsKey(entry.Id)) throw new InvalidOperationException("新增对话标签与既有内容冲突。");
                table.Add(entry.Id, entry.Metadata);
            }
            var memberwise = typeof(object).GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance);
            var clone = (LineMetadata)memberwise.Invoke(source, null);
            field.SetValue(clone, table);
            return clone;
        }
    }
}
