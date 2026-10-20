using System;
using UnityEngine;

namespace Jinke.DredgeSkins.NativeQA
{
    public static class EffectiveAppearanceComparison
    {
        public const double ColorTolerance = 0.000001d;

        public static bool Equivalent(EffectiveState[] before, EffectiveState[] after, out double maxAbsChannelDelta)
        {
            maxAbsChannelDelta = 0d;
            if (before == null || after == null || before.Length != after.Length) return false;
            bool equal = true;
            for (int i = 0; i < before.Length; i++)
            {
                EffectiveState left = before[i], right = after[i];
                if (left == null || right == null) { equal = false; continue; }
                equal &= left.rendererId == right.rendererId && left.category == right.category && left.flagRgbaSha256 == right.flagRgbaSha256;
                equal &= CompareColor(left.hull, right.hull, ref maxAbsChannelDelta);
                equal &= CompareColor(left.roof, right.roof, ref maxAbsChannelDelta);
            }
            return equal;
        }

        private static bool CompareColor(Color left, Color right, ref double maxDelta)
        {
            bool equal = true;
            float[] a = { left.r, left.g, left.b, left.a }, b = { right.r, right.g, right.b, right.a };
            for (int i = 0; i < a.Length; i++)
            {
                double delta = Math.Abs((double)a[i] - b[i]);
                if (double.IsNaN(delta) || double.IsInfinity(delta)) { maxDelta = double.MaxValue; equal = false; continue; }
                maxDelta = Math.Max(maxDelta, delta); equal &= delta <= ColorTolerance;
            }
            return equal;
        }
    }
}
