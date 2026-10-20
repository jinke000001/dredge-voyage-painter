namespace Jinke.DredgeMerchantTravel
{
    internal enum LandingColliderRole
    {
        Disabled,
        LayerIgnored,
        OrdinarySensor,
        Solid,
        AutomaticHazard,
        UnknownTrigger
    }

    internal static class LandingRules
    {
        internal const float MinimumWaterDepth = .02f;
        internal const int WaterSampleCount = 9;
        internal const int GeometryShapeCount = 7;

        internal static LandingColliderRole Classify(bool enabled, bool automaticHazard, bool isTrigger,
            bool ordinarySensor, bool collidesWithHull)
        {
            if (!enabled) return LandingColliderRole.Disabled;
            // Some hazards use a separate player detector layer, so hull filtering cannot remove them.
            if (automaticHazard) return LandingColliderRole.AutomaticHazard;
            // An ordinary-looking non-trigger is still solid when the actual hull collides with its layer.
            if (!isTrigger) return collidesWithHull ? LandingColliderRole.Solid : LandingColliderRole.LayerIgnored;
            if (ordinarySensor) return LandingColliderRole.OrdinarySensor;
            return collidesWithHull ? LandingColliderRole.UnknownTrigger : LandingColliderRole.LayerIgnored;
        }

        internal static bool BlocksLanding(LandingColliderRole role)
        {
            return role == LandingColliderRole.Solid || role == LandingColliderRole.AutomaticHazard ||
                role == LandingColliderRole.UnknownTrigger;
        }

        internal static bool HasWaterFootprint(float[] samples)
        {
            if (samples == null || samples.Length != WaterSampleCount) return false;
            foreach (float sample in samples)
                if (!TravelRules.Finite(sample) || sample < MinimumWaterDepth || sample > 1f) return false;
            return true;
        }

        internal static bool GeometrySelfTestPassed(bool isolated, bool[] positive, bool[] negative)
        {
            if (!isolated || positive == null || negative == null || positive.Length != GeometryShapeCount ||
                negative.Length != GeometryShapeCount) return false;
            for (int i = 0; i < GeometryShapeCount; i++) if (!positive[i] || negative[i]) return false;
            return true;
        }
    }
}
