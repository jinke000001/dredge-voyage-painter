using System;

namespace Jinke.DredgeSkins
{
    /// <summary>All arrays use Unity's bottom-up, row-major RGBA order. Non-cloth bytes remain untouched.</summary>
    public static class AtlasLayout
    {
        public const int Width = 256;
        public const int Height = 256;
        // Flag.obj UV: u=0.0079803..0.991211, v=0.305420..0.993652.
        // Cover the whole upper atlas including sampling margins; preserve the pole's lower 76 rows.
        public const int ClothLeft = 0;
        public const int ClothRightExclusive = 256;
        public const int ClothBottom = 76;
        public const int ClothTopExclusive = 256;

        public static byte[] Compose(byte[] original, byte[] flag, int flagWidth, int flagHeight)
        {
            if (original == null || original.Length != Width * Height * 4)
                throw new ArgumentException("Original flag atlas must be 256 x 256 RGBA.", "original");
            if (flagWidth < 1 || flagHeight < 1 || flag == null || flag.Length != flagWidth * flagHeight * 4)
                throw new ArgumentException("Invalid flag RGBA pixels.", "flag");
            byte[] result = (byte[])original.Clone();
            int clothWidth = ClothRightExclusive - ClothLeft;
            int clothHeight = ClothTopExclusive - ClothBottom;
            for (int y = ClothBottom; y < ClothTopExclusive; y++)
            {
                float sourceY = ((y - ClothBottom + 0.5f) / clothHeight) * flagHeight - 0.5f;
                int y0 = Math.Max(0, Math.Min(flagHeight - 1, (int)Math.Floor(sourceY)));
                int y1 = Math.Max(0, Math.Min(flagHeight - 1, (int)Math.Floor(sourceY) + 1));
                float fy = sourceY - (float)Math.Floor(sourceY);
                for (int x = ClothLeft; x < ClothRightExclusive; x++)
                {
                    float sourceX = ((x - ClothLeft + 0.5f) / clothWidth) * flagWidth - 0.5f;
                    int x0 = Math.Max(0, Math.Min(flagWidth - 1, (int)Math.Floor(sourceX)));
                    int x1 = Math.Max(0, Math.Min(flagWidth - 1, (int)Math.Floor(sourceX) + 1));
                    float fx = sourceX - (float)Math.Floor(sourceX);
                    int target = (y * Width + x) * 4;
                    for (int c = 0; c < 4; c++)
                    {
                        float lower = flag[(y0 * flagWidth + x0) * 4 + c] * (1f - fx) + flag[(y0 * flagWidth + x1) * 4 + c] * fx;
                        float upper = flag[(y1 * flagWidth + x0) * 4 + c] * (1f - fx) + flag[(y1 * flagWidth + x1) * 4 + c] * fx;
                        result[target + c] = (byte)Math.Round(lower * (1f - fy) + upper * fy);
                    }
                }
            }
            return result;
        }
    }
}
