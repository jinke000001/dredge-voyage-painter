using System;

namespace Jinke.DredgeVoyage
{
    [Serializable]
    internal sealed class VoyageSettings
    {
        public int FishingAssist;
        public int Aberration;
        public int PotYield;
        public int PotDurability;
        public int NetYield;
        public int NetDurability;
        public int Pace;

        internal VoyageSettings Copy() { return (VoyageSettings)MemberwiseClone(); }
        internal bool IsDefault { get { return FishingAssist == 0 && Aberration == 0 && PotYield == 0 && PotDurability == 0 && NetYield == 0 && NetDurability == 0 && Pace == 0; } }
        internal static int MaximumLevel(int row)
        {
            if (row < 0 || row > 6) throw new ArgumentOutOfRangeException("row");
            return row == 0 || row == 6 ? 2 : 3;
        }
        internal void Validate()
        {
            for (int row = 0; row < 7; row++)
                if (Get(row) < 0 || Get(row) > MaximumLevel(row)) throw new InvalidOperationException("航行设置的档位无效。");
        }
        internal int Get(int row)
        {
            switch (row)
            {
                case 0: return FishingAssist; case 1: return Aberration; case 2: return PotYield;
                case 3: return PotDurability; case 4: return NetYield; case 5: return NetDurability; case 6: return Pace;
                default: throw new ArgumentOutOfRangeException("row");
            }
        }
        internal void Set(int row, int value)
        {
            if (value < 0 || value > MaximumLevel(row)) throw new ArgumentOutOfRangeException("value");
            switch (row)
            {
                case 0: FishingAssist = value; break; case 1: Aberration = value; break;
                case 2: PotYield = value; break; case 3: PotDurability = value; break;
                case 4: NetYield = value; break; case 5: NetDurability = value; break; case 6: Pace = value; break;
                default: throw new ArgumentOutOfRangeException("row");
            }
        }
    }
}
