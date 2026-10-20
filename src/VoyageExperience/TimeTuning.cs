using System;
using UnityEngine;

namespace Jinke.DredgeVoyage
{
    internal static class TimeTuning
    {
        private static GameConfigData modifiedConfig;
        private static float originalHourDuration;
        private static float originalForcedModifier;
        private static float multiplier = 1f;

        internal static void Apply(VoyageSettings settings)
        {
            float next = TimeCargoRules.PaceMultiplier(settings == null ? 0 : settings.Pace);
            if (next == 1f) { Restore(); return; }
            GameManager game = GameManager.Instance;
            GameConfigData config = game == null ? null : game.GameConfigData;
            if (config == null) { Restore(); return; }
            if (!ReferenceEquals(config, modifiedConfig))
            {
                Restore();
                if (!Positive(config.HourDurationInSeconds) || !Positive(config.ForcedTimePassageSpeedModifier)) return;
                originalHourDuration = config.HourDurationInSeconds;
                originalForcedModifier = config.ForcedTimePassageSpeedModifier;
                modifiedConfig = config;
            }
            if (multiplier == next) return;
            // Only the game clock changes. Unity time, movement, minigame input,
            // dawn/dusk thresholds and native ability history are untouched.
            config.HourDurationInSeconds = originalHourDuration * next;
            // Fixed game-hour actions (sleep/install/repair) keep their original
            // real duration. Their sanity rate is compensated by the hook below.
            config.ForcedTimePassageSpeedModifier = originalForcedModifier * next;
            multiplier = next;
        }

        internal static float AdjustSanityDeltaTime(float original, object owner)
        {
            if (multiplier == 1f || modifiedConfig == null) return original;
            GameManager game = GameManager.Instance;
            PlayerSanity sanity = owner as PlayerSanity;
            if (game == null || !ReferenceEquals(game.GameConfigData, modifiedConfig) || sanity == null ||
                sanity.PlayerRef == null || sanity.PlayerRef.SanityModifierDetector == null ||
                sanity.PlayerRef.SanityModifierDetector.IgnoreTimescale) return original;
            return original / multiplier;
        }

        internal static void Restore()
        {
            if (modifiedConfig != null)
            {
                modifiedConfig.HourDurationInSeconds = originalHourDuration;
                modifiedConfig.ForcedTimePassageSpeedModifier = originalForcedModifier;
            }
            modifiedConfig = null;
            multiplier = 1f;
        }

        private static bool Positive(float value) { return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}
