using System;
using System.Collections.Generic;
using System.Reflection;

namespace Jinke.DredgeVoyage
{
    // Holds the first observed native values, never the result of a previous Apply.
    // No save objects, fish instances, durability values, or grid layouts are changed.
    internal sealed class FishingTuning
    {
        private static readonly FieldInfo DayChance = RequireField(typeof(GameConfigData), "baseAberrationSpawnChance");
        private static readonly FieldInfo NightChance = RequireField(typeof(GameConfigData), "nightAberrationSpawnChance");
        private static readonly FieldInfo CatchInterval = RequireField(typeof(DeployableItemData), "timeBetweenCatchRolls");
        private readonly Dictionary<HarvestDifficultyConfigData, DifficultyBaseline> difficulties = new Dictionary<HarvestDifficultyConfigData, DifficultyBaseline>();
        private readonly Dictionary<DeployableItemData, float> intervals = new Dictionary<DeployableItemData, float>();
        private readonly Dictionary<GameConfigData, ChanceBaseline> chances = new Dictionary<GameConfigData, ChanceBaseline>();

        internal bool IsReady { get; private set; }

        internal void Apply(VoyageSettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            GameManager manager = GameManager.Instance;
            IsReady = manager != null && manager.GameConfigData != null && manager.GameConfigData.FishingDifficultyConfigs != null
                && manager.GameConfigData.FishingDifficultyConfigs.Count > 0 && manager.ItemManager != null && manager.ItemManager.HasLoaded();
            if (!IsReady) return;
            try
            {
                ApplyConfig(manager.GameConfigData, settings);
                foreach (DeployableItemData item in manager.ItemManager.GetAllItemsOfType<DeployableItemData>())
                {
                    if (item == null) continue;
                    int level;
                    if (FishingRules.IsPot(item.id) && item.itemSubtype == ItemSubtype.POT) level = settings.PotYield;
                    else if (FishingRules.IsNet(item.id) && item.itemSubtype == ItemSubtype.NET) level = settings.NetYield;
                    else continue;
                    float original;
                    if (!intervals.TryGetValue(item, out original))
                    {
                        original = item.TimeBetweenCatchRolls;
                        intervals.Add(item, original);
                    }
                    float target = FishingRules.Interval(original, level);
                    if (item.TimeBetweenCatchRolls != target) CatchInterval.SetValue(item, target);
                }
                FishingHooks.Configure(settings);
            }
            catch
            {
                Restore();
                throw;
            }
        }

        private void ApplyConfig(GameConfigData config, VoyageSettings settings)
        {
            ChanceBaseline chance;
            if (!chances.TryGetValue(config, out chance))
            {
                chance = new ChanceBaseline { Day = config.BaseAberrationSpawnChance, Night = config.NightAberrationSpawnChance };
                chances.Add(config, chance);
            }
            float day = FishingRules.AberrationChance(chance.Day, config.MaxAberrationSpawnChance, settings.Aberration);
            float night = FishingRules.AberrationChance(chance.Night, config.MaxAberrationSpawnChance, settings.Aberration);
            if (day != config.BaseAberrationSpawnChance) DayChance.SetValue(config, day);
            if (night != config.NightAberrationSpawnChance) NightChance.SetValue(config, night);
            if (config.FishingDifficultyConfigs == null) return;
            foreach (HarvestDifficultyConfigData difficulty in config.FishingDifficultyConfigs.Values)
            {
                if (difficulty == null) continue;
                if (FishingRules.Level(settings.FishingAssist) != 0 && config.DredgingDifficultyConfigs != null
                    && config.DredgingDifficultyConfigs.ContainsValue(difficulty))
                    throw new InvalidOperationException("Fishing and dredging share a difficulty asset; fishing assistance was restored safely.");
                DifficultyBaseline original;
                if (!difficulties.TryGetValue(difficulty, out original))
                {
                    original = new DifficultyBaseline(difficulty);
                    difficulties.Add(difficulty, original);
                }
                original.Apply(difficulty, settings.FishingAssist);
            }
        }

        internal void Restore()
        {
            FishingHooks.Reset();
            foreach (KeyValuePair<HarvestDifficultyConfigData, DifficultyBaseline> pair in difficulties)
                if (pair.Key != null) pair.Value.Apply(pair.Key, 0);
            foreach (KeyValuePair<DeployableItemData, float> pair in intervals)
                if (pair.Key != null && pair.Key.TimeBetweenCatchRolls != pair.Value) CatchInterval.SetValue(pair.Key, pair.Value);
            foreach (KeyValuePair<GameConfigData, ChanceBaseline> pair in chances)
            {
                if (pair.Key == null) continue;
                if (pair.Key.BaseAberrationSpawnChance != pair.Value.Day) DayChance.SetValue(pair.Key, pair.Value.Day);
                if (pair.Key.NightAberrationSpawnChance != pair.Value.Night) NightChance.SetValue(pair.Key, pair.Value.Night);
            }
            difficulties.Clear();
            intervals.Clear();
            chances.Clear();
            IsReady = false;
        }

        private static FieldInfo RequireField(Type type, string name)
        {
            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field == null) throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private sealed class ChanceBaseline { internal float Day, Night; }

        private sealed class DifficultyBaseline
        {
            private readonly int minWidth, maxWidth;
            private readonly float ballSpeed, ballTrophyFactor, diamondTime, diamondTrophyFactor;

            internal DifficultyBaseline(HarvestDifficultyConfigData data)
            {
                minWidth = data.minTargetWidth;
                maxWidth = data.maxTargetWidth;
                ballSpeed = data.speedFactor;
                ballTrophyFactor = data.ballTrophySpeedFactor;
                diamondTime = data.diamondScaleUpTimeSec;
                diamondTrophyFactor = data.diamondTrophySpeedFactor;
            }

            internal void Apply(HarvestDifficultyConfigData data, int level)
            {
                level = FishingRules.Level(level);
                data.minTargetWidth = FishingRules.TargetWidth(minWidth, data.maxTargets, level);
                data.maxTargetWidth = FishingRules.TargetWidth(maxWidth, data.maxTargets, level);
                if (level == 0)
                {
                    data.speedFactor = ballSpeed;
                    data.ballTrophySpeedFactor = ballTrophyFactor;
                    data.diamondScaleUpTimeSec = diamondTime;
                    data.diamondTrophySpeedFactor = diamondTrophyFactor;
                    return;
                }
                float speed = FishingRules.NormalTargetSpeed(level);
                data.speedFactor = ballSpeed * speed;
                data.ballTrophySpeedFactor = ballTrophyFactor * speed;
                data.diamondScaleUpTimeSec = diamondTime / speed;
                data.diamondTrophySpeedFactor = diamondTrophyFactor * speed;
                // The ball's special speed is speedFactor / ballTrophySpeedFactor;
                // the diamond's is scaleUpTime * diamondTrophySpeedFactor. Both stay
                // native. Radial speed, gold width, passive progress, reward sizes,
                // target value and trophy appearance rolls are deliberately untouched.
            }
        }
    }
}
