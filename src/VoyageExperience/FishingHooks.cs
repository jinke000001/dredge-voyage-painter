using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using UnityEngine.Localization.Components;

namespace Jinke.DredgeVoyage
{
    // Called only from the exact native call sites patched by the isolated weaver.
    public static class FishingHooks
    {
        private static int assist, potDurability, netDurability;
        private static readonly HashSet<string> reportedErrors = new HashSet<string>();
        private static readonly FieldInfo NetItem = typeof(TrawlNetAbility).GetField("trawlNetItemData", BindingFlags.Instance | BindingFlags.NonPublic);

        internal static void Configure(VoyageSettings settings)
        {
            assist = FishingRules.Level(settings.FishingAssist);
            potDurability = FishingRules.BoostLevel(settings.PotDurability);
            netDurability = FishingRules.BoostLevel(settings.NetDurability);
        }

        internal static void Reset() { assist = potDurability = netDurability = 0; }

        public static float AdjustPotMaintenance(float original, object owner)
        {
            if (potDurability == 0) return original;
            var pot = owner as SerializedCrabPotPOIData;
            if (pot == null || !FishingRules.IsPot(pot.deployableItemId)) return original;
            return FishingRules.Maintenance(original, potDurability);
        }

        public static float AdjustNetMaintenance(float original, object owner)
        {
            if (netDurability == 0 || !(owner is TrawlNetAbility)) return original;
            try
            {
                if (NetItem == null) throw new MissingFieldException(typeof(TrawlNetAbility).FullName, "trawlNetItemData");
                var item = (DeployableItemData)NetItem.GetValue(owner);
                return item != null && FishingRules.IsNet(item.id) ? FishingRules.Maintenance(original, netDurability) : original;
            }
            catch (Exception error) { Report("net-wear", error); return original; }
        }

        // Replaces the fixed formatter calls in deployable tooltips, pot and net panels.
        // Exactly one native asynchronous formatting operation is issued, avoiding
        // a late original callback overwriting the updated duration.
        public static void FormatDeployableDurability(LanguageManager language, float current, float maximum,
            LocalizeStringEvent target, string key, object owner)
        {
            float adjusted = current;
            if (potDurability != 0 || netDurability != 0)
            {
                try
                {
                    GameManager manager = GameManager.Instance;
                    DeployableItemData item = GetDisplayedDeployable(owner, manager);
                    int level = GetDurabilityLevel(item);
                    if (level != 0 && manager != null && manager.PlayerStats != null)
                        adjusted = FishingRules.DurabilityFormatInput(current, manager.PlayerStats.ResearchedEquipmentMaintenanceModifier, level);
                }
                catch (Exception error) { Report("duration-label", error); }
            }
            language.FormatTimeStringForDurability(adjusted, maximum, target, key);
        }

        private static DeployableItemData GetDisplayedDeployable(object owner, GameManager manager)
        {
            if (owner is TooltipSectionDeployableDetails || owner is OccasionalGridPanel)
                return Read<DeployableItemData>(owner, "deployableItemData");
            if (owner is NetPanel && manager != null && manager.SaveData != null)
            {
                SpatialItemInstance item = manager.SaveData.EquippedTrawlNetInstance();
                return item == null ? null : item.GetItemData<DeployableItemData>();
            }
            return null;
        }

        public static void AfterSpiralTargets(object owner)
        {
            if (assist == 0 || !(owner is SpiralMinigame)) return;
            try
            {
                List<Vector2> notches = Read<List<Vector2>>(owner, "notches");
                if (notches == null || notches.Count == 0) return;
                int trophy = Read<int>(owner, "trophyNotchIndex");
                float start = Read<float>(owner, "startDeadzone"), end = Read<float>(owner, "endDeadzone");
                var image = Read<UnityEngine.UI.Image>(owner, "spiralImage");
                var animators = Read<List<Animator>>(owner, "gateAnimators");
                var config = Read<SpiralMinigame.SpiralConfig>(owner, "spiralConfig");
                if (image == null || image.material == null || animators == null || animators.Count != notches.Count)
                    throw new InvalidOperationException("Spiral target and gate counts do not match.");
                Material material = image.material;
                var gates = new SpiralComponent[notches.Count];
                for (int index = 0; index < gates.Length; index++)
                {
                    gates[index] = animators[index] == null ? null : animators[index].GetComponent<SpiralComponent>();
                    if (gates[index] == null) throw new InvalidOperationException("Spiral gate component is missing.");
                }
                float segment = 1f / notches.Count;
                for (int index = 0; index < notches.Count; index++)
                {
                    if (index == trophy) continue;
                    Vector2 original = notches[index];
                    float lower = segment * index + (index == 0 ? start : 0f);
                    float upper = segment * (index + 1) - (index == notches.Count - 1 ? end : 0f);
                    float center = original.x + original.y * 0.5f;
                    float width = Math.Max(original.y, Math.Min(original.y * FishingRules.WindowMultiplier(assist), upper - lower));
                    float left = Math.Max(lower, Math.Min(center - width * 0.5f, upper - width));
                    Vector2 widened = new Vector2(left, width);
                    notches[index] = widened;
                    // CreateNotch allocates another gate and changes animator indexing.
                    // Keep the original gate and its native color, opacity and animation state.
                    string prefix = "_notch" + (index + 1).ToString(CultureInfo.InvariantCulture);
                    float gatePosition = left + width;
                    material.SetFloat(prefix + "_start", left);
                    material.SetFloat(prefix + "_end", gatePosition);
                    gates[index].SetPosition(gatePosition, config);
                    gates[index].SetRotation(gatePosition, config);
                }
            }
            catch (Exception error) { Report("spiral-window", error); }
        }

        private static int GetDurabilityLevel(DeployableItemData item)
        {
            if (item == null) return 0;
            if (FishingRules.IsPot(item.id)) return potDurability;
            return FishingRules.IsNet(item.id) ? netDurability : 0;
        }

        private static T Read<T>(object owner, string name)
        {
            FieldInfo field = owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field == null) throw new MissingFieldException(owner.GetType().FullName, name);
            return (T)field.GetValue(owner);
        }

        private static void Report(string operation, Exception error)
        {
            if (reportedErrors.Add(operation)) Debug.LogWarning("[VoyageExperience] " + operation + ": " + error.Message);
        }
    }
}
