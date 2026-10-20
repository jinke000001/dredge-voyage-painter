using System;

namespace Jinke.DredgeVoyage
{
    internal static class TimeCargoRules
    {
        internal const decimal CargoCost = 200m;
        internal const int CargoMaxLevel = 8;
        internal static decimal CargoPrice(int level)
        {
            if (level < 0 || level > CargoMaxLevel) throw new ArgumentOutOfRangeException("level");
            return level == CargoMaxLevel ? 0m : CargoCost * (level + 1);
        }
        internal static float PaceMultiplier(int pace)
        {
            return pace == 1 ? 1.25f : pace == 2 ? 1.5f : 1f;
        }

        // Ordered pairs: the original v1 pair is always first. All positions
        // are hidden NONE cells, outside every shipped native slot upgrade.
        // Tier 1 has 16 such cells, so every hull keeps the same eight levels.
        // Keeping a common limit preserves capacity while native upgrades repack.
        internal static int[] CargoCoordinates(int tier)
        { return CargoCoordinates(tier, 1); }

        internal static int[] CargoCoordinates(int tier, int level)
        {
            if (level < 0 || level > CargoMaxLevel) throw new ArgumentOutOfRangeException("level");
            int[] all;
            switch (tier)
            {
                case 1: all = new[] { 1,0, 4,0, 0,1, 5,1, 0,0, 5,0, 0,6, 5,6, 1,7, 4,7, 0,7, 5,7, 1,8, 4,8, 0,8, 5,8 }; break;
                case 2: all = new[] { 0,2, 6,2, 0,1, 6,1, 0,0, 6,0, 1,0, 5,0, 0,6, 6,6, 1,7, 5,7, 0,7, 6,7, 1,8, 5,8 }; break;
                case 3: all = new[] { 1,1, 6,1, 0,2, 7,2, 0,1, 7,1, 2,0, 5,0, 1,0, 6,0, 0,0, 7,0, 1,8, 6,8, 0,8, 7,8 }; break;
                case 4: all = new[] { 1,1, 7,1, 0,2, 8,2, 0,1, 8,1, 2,0, 6,0, 1,0, 7,0, 0,0, 8,0, 1,9, 7,9, 0,9, 8,9 }; break;
                case 5: all = new[] { 1,1, 8,1, 0,2, 9,2, 0,1, 9,1, 2,0, 7,0, 1,0, 8,0, 0,0, 9,0, 1,9, 8,9, 0,9, 9,9 }; break;
                default: throw new ArgumentOutOfRangeException("tier");
            }
            var result = new int[level * 4];
            Array.Copy(all, result, result.Length);
            return result;
        }

        internal static int Columns(int tier) { return tier + 5; }
        internal static int Rows(int tier) { return Math.Min(tier + 8, 12); }
    }
}
