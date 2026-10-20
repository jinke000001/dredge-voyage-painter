using System;
using System.Collections.Generic;

namespace Jinke.DredgeMerchantTravel
{
    internal static class TravelRules
    {
        internal static string Eligibility(bool manifestUnlocked, bool visited, bool requiresDlc, bool hasDlc)
        {
            if (!manifestUnlocked) return "先在原版剧情中获得「显形」能力";
            if (requiresDlc && !hasDlc) return "需要「皓寒之渚」DLC";
            if (!visited) return "先亲自航行到这里并完成一次停靠";
            return null;
        }

        internal static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }

        internal static List<string> Blockers(GateState state)
        {
            var reasons = new List<string>();
            if (!state.Ready) reasons.Add("航行场景尚未准备好");
            if (!state.Playing) reasons.Add("请先进入游戏");
            if (!state.Alive) reasons.Add("当前无法航行");
            if (state.Docked) reasons.Add("先离开码头，再选择传送");
            if (state.AutoMoving || state.Docking) reasons.Add("等待进港或离港动作完成");
            if (state.Fishing || state.Harvesting) reasons.Add("先结束捕捞或打捞");
            if (state.Paused || state.WindowOpen || state.Dialogue || state.Popup || state.Cutscene) reasons.Add("先关闭原版界面或结束对话");
            if (state.Finale || state.CastingForbidden || state.ForbidSave) reasons.Add("当前剧情阶段暂不能传送");
            if (state.NativeTeleport || state.OwnTeleport || state.Immune) reasons.Add("等待当前传送或特殊状态结束");
            if (state.ForcedTime || state.FrozenTime) reasons.Add("等待当前时间流程结束");
            if (!state.BaseActionLayer) reasons.Add("等待恢复自由航行");
            if (!state.MovementAllowed || !state.InputEnabled) reasons.Add("当前航行控制不可用");
            if (state.Binding || state.AbilityCharging) reasons.Add("先结束按键设置或当前技能蓄力");
            if (!state.NormalTimeScale || !state.Focused) reasons.Add("请回到正常航行画面");
            if (state.StorageError) reasons.Add("显形扩展暂不可用，请退出后检查运行日志");
            return reasons;
        }
    }

    internal sealed class GateState
    {
        public bool Ready, Playing, Alive, Docked, AutoMoving, Docking, Fishing, Harvesting;
        public bool Paused, WindowOpen, Dialogue, Popup, Cutscene, Finale, CastingForbidden, ForbidSave;
        public bool NativeTeleport, OwnTeleport, Immune, ForcedTime, FrozenTime, BaseActionLayer;
        public bool MovementAllowed, InputEnabled, Binding, AbilityCharging, NormalTimeScale, Focused, StorageError;
    }

    internal sealed class Destination
    {
        internal readonly string Id, Name, Region, Description;
        internal readonly int RequiredDlc;
        internal bool RequiresDlc { get { return RequiredDlc != 0; } }
        internal Destination(string id, string name, string region, string description, int dlc = 0)
        { Id = id; Name = name; Region = region; Description = description; RequiredDlc = dlc; }

        internal static readonly Destination[] All =
        {
            new Destination("dock.pontoon-gc", "积灰的浮码头", "盖尔悬崖", "悬崖与沉船之间的补给站"),
            new Destination("dock.pontoon-sb", "闪亮的浮码头", "斯特拉尔湾", "珊瑚海湾外的商人据点"),
            new Destination("dock.pontoon-ts", "摇晃的浮码头", "蜿蜒海岸", "红树林边缘的补给站"),
            new Destination("dock.pontoon-ds", "焦黑的浮码头", "恶魔之脊", "古老遗迹外的商人据点"),
            new Destination("dock.pontoon-tpr", "荒废的浮码头", "皓寒之渚", "冰海入口的流浪商人", 1),
            new Destination("dock.the-iron-rig", "钢铁钻井", "钻井平台", "平台泊位附近的安全水面", 2)
        };

        internal static Destination Find(string id)
        { foreach (Destination target in All) if (string.Equals(target.Id, id, StringComparison.Ordinal)) return target; return null; }
    }
}
