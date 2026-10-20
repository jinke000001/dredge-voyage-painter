using System;
using UnityEngine;

namespace Jinke.DredgeSkins.NativeQA
{
    public static class PreviewPixels
    {
        public const int Width = 768, Height = 768;
        public const int MinimumVisiblePixels = Width * Height / 100;

        public static int CountNonBackground(Color32[] pixels)
        {
            if (pixels == null || pixels.Length == 0) return 0;
            // The fitted mesh has a margin; the bottom-left pixel is the actually rendered clear color.
            Color32 background = pixels[0]; int count = 0;
            foreach (Color32 pixel in pixels)
                if (Math.Abs(pixel.r - background.r) > 2 || Math.Abs(pixel.g - background.g) > 2 || Math.Abs(pixel.b - background.b) > 2) count++;
            return count;
        }
    }

    internal sealed class PreviewExport
    {
        internal string FileName;
        internal int NonBackgroundPixels, TotalPixels;
    }
}
