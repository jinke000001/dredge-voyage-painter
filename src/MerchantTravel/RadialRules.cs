using System;

namespace Jinke.DredgeMerchantTravel
{
    internal static class RadialRules
    {
        internal const string BlackstoneId = "manifest.blackstone";
        internal const int Count = 7;

        internal static double NativeCooldownRemaining(double elapsed, double cooldown)
        {
            if (!TravelRules.Finite(cooldown) || cooldown < 0) throw new InvalidOperationException("Invalid native manifest cooldown.");
            // Native GetTimeSinceLastCast returns now - (-Infinity) when there is no saved history.
            if (double.IsPositiveInfinity(elapsed)) return 0;
            if (!TravelRules.Finite(elapsed)) throw new InvalidOperationException("Invalid native manifest cast time.");
            return Math.Max(0, cooldown - elapsed);
        }

        // Index zero is north; indices increase clockwise, matching the native ability radial.
        internal static int Selection(double x, double y, double deadzone)
        {
            if (!TravelRules.Finite(x) || !TravelRules.Finite(y) || !TravelRules.Finite(deadzone) || deadzone < 0)
                return -1;
            if (x * x + y * y <= deadzone * deadzone) return -1;
            double degrees = Math.Atan2(x, y) * 180 / Math.PI;
            if (degrees < 0) degrees += 360;
            double width = 360d / Count;
            return ((int)Math.Floor((degrees + width / 2) / width)) % Count;
        }
    }

    // A callback is authorized once, for exactly the original bar and selected AbilityData objects.
    internal sealed class SingleCommitTicket
    {
        private object owner, ability;
        private bool armed;
        internal bool InProgress { get; private set; }
        internal bool Consumed { get; private set; }
        internal void Begin(object expectedOwner, object expectedAbility)
        {
            if (InProgress || expectedOwner == null || expectedAbility == null) throw new InvalidOperationException("Invalid native commit ticket.");
            owner = expectedOwner; ability = expectedAbility; InProgress = armed = true; Consumed = false;
        }
        internal bool Consume(object candidateOwner, object candidateAbility)
        {
            if (!InProgress || !armed || !ReferenceEquals(owner, candidateOwner) || !ReferenceEquals(ability, candidateAbility)) return false;
            armed = false; Consumed = true; return true;
        }
        internal void End() { owner = ability = null; armed = InProgress = false; }
    }
}
