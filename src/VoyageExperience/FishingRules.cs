using System;

namespace Jinke.DredgeVoyage
{
    internal static class FishingRules
    {
        internal static int Level(int value) { return value >= 0 && value <= 2 ? value : 0; }
        internal static int BoostLevel(int value) { return value >= 0 && value <= 3 ? value : 0; }
        internal static float Multiplier(int value) { return BoostLevel(value) == 1 ? 1.25f : BoostLevel(value) == 2 ? 1.5f : BoostLevel(value) == 3 ? 3f : 1f; }
        internal static float WindowMultiplier(int value) { return Level(value) == 1 ? 1.2f : Level(value) == 2 ? 1.4f : 1f; }
        internal static float NormalTargetSpeed(int value) { return Level(value) == 1 ? 0.85f : Level(value) == 2 ? 0.7f : 1f; }

        // The shipped fishing tools are pot1..pot8 and net1..net8. In particular,
        // tir-pot1/tir-net1/tir-net2 are material/oil tools, not ordinary fishing gear.
        internal static bool IsPot(string id) { return IsNumberedTool(id, "pot"); }
        internal static bool IsNet(string id) { return IsNumberedTool(id, "net"); }
        private static bool IsNumberedTool(string id, string prefix)
        {
            return id != null && id.Length == prefix.Length + 1 && id.StartsWith(prefix, StringComparison.Ordinal)
                && id[id.Length - 1] >= '1' && id[id.Length - 1] <= '8';
        }

        internal static float Interval(float original, int level)
        {
            if (BoostLevel(level) == 0 || !PositiveFinite(original)) return original;
            // A positive floor bounds the crab pot's recursive elapsed-time catch-up.
            return Math.Max(Math.Min(original, 1f / 1440f), original / Multiplier(level));
        }

        internal static float Maintenance(float original, int level)
        {
            if (BoostLevel(level) == 0 || float.IsNaN(original) || float.IsInfinity(original)) return original;
            return 1f - (1f - original) / Multiplier(level);
        }

        internal static float AberrationChance(float original, float maximum, int level)
        {
            if (BoostLevel(level) == 0 || !Finite(original) || !Finite(maximum)) return original;
            float bonus = BoostLevel(level) == 1 ? 0.015f : BoostLevel(level) == 2 ? 0.03f : 0.30f;
            // Never lower an unusual existing value, and preserve the native ceiling.
            return Math.Max(original, Math.Min(maximum, original + bonus));
        }

        internal static int TargetWidth(int original, int maximumTargets, int level)
        {
            if (Level(level) == 0 || original <= 0) return original;
            // Native radial placement reserves 60 degrees plus 20 per target.
            int limit = Math.Max(original, (int)(300f / Math.Max(maximumTargets, 1) - 20f));
            return Math.Min(limit, (int)Math.Round(original * WindowMultiplier(level)));
        }

        internal static float DurabilityFormatInput(float remaining, float researchedMaintenance, int level)
        {
            if (BoostLevel(level) == 0 || remaining <= 0f || !Finite(researchedMaintenance)) return remaining;
            float wear = 1f - researchedMaintenance;
            float nativeDisplayFactor = 1f + researchedMaintenance;
            if (wear <= 0f || nativeDisplayFactor <= 0f) return remaining;
            // Native formatter multiplies by (1 + maintenance); undo that before
            // supplying the actual lifetime implied by the wear calculation.
            return remaining * Multiplier(level) / wear / nativeDisplayFactor;
        }

        internal static bool PositiveFinite(float value) { return value > 0f && Finite(value); }
        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}
