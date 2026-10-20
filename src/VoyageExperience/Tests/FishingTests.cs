using System;
using Jinke.DredgeVoyage;
using Newtonsoft.Json;
using UnityEngine.Localization.Components;

internal static class FishingTests
{
    private static int assertions;
    private static void Require(bool value, string label) { assertions++; if (!value) throw new Exception(label); }
    private static void Near(float expected, float actual, string label) { Require(Math.Abs(expected - actual) < 0.00001f, label + ": " + actual); }
    private static void Exact(float expected, float actual, string label) { Require(BitConverter.SingleToInt32Bits(expected) == BitConverter.SingleToInt32Bits(actual), label); }
    private static void Reject<T>(Action action, string label) where T : Exception
    {
        bool rejected = false;
        try { action(); } catch (T) { rejected = true; }
        Require(rejected, label);
    }
    private static void VerifyFormat(float current, float maximum, float expected, object owner, bool native, string label)
    {
        var language = new LanguageManager();
        var target = new LocalizeStringEvent();
        FishingHooks.FormatDeployableDurability(language, current, maximum, target, "label.deployable.durability-value", owner);
        Require(language.Calls == 1, label + ": exactly one native format operation");
        Require(ReferenceEquals(target, language.LastTarget) && language.LastKey == "label.deployable.durability-value", label + ": original localization destination");
        Exact(maximum, language.LastMaximum, label + ": original maximum");
        if (native) Exact(current, language.LastCurrent, label + ": native input unchanged");
        else Near(expected, language.LastCurrent * (1f + GameManager.Instance.PlayerStats.ResearchedEquipmentMaintenanceModifier), label + ": effective lifetime");
    }

    public static void Main()
    {
        VerifySettings();
        GameManager.Instance = new GameManager();
        GameManager manager = GameManager.Instance;
        var fishing = new HarvestDifficultyConfigData();
        var dredging = new HarvestDifficultyConfigData();
        manager.GameConfigData.FishingDifficultyConfigs.Add(HarvestDifficulty.EASY, fishing);
        manager.GameConfigData.FishingDifficultyConfigs.Add(HarvestDifficulty.HARD, fishing); // shared within fishing: no compounding
        manager.GameConfigData.DredgingDifficultyConfigs.Add(HarvestDifficulty.EASY, dredging);
        var pot = new DeployableItemData("pot1", ItemSubtype.POT, 0.66f, 3f);
        var net = new DeployableItemData("net1", ItemSubtype.NET, 0.125f, 1f);
        var material = new DeployableItemData("tir-pot1", ItemSubtype.POT, 0.66f, 7f);
        var oil = new DeployableItemData("tir-net1", ItemSubtype.NET, 0.125f, 1f);
        var materialNet = new DeployableItemData("tir-net2", ItemSubtype.NET, 0.125f, 1f);
        manager.ItemManager.Items.AddRange(new[] { pot, net, material, oil, materialNet });
        var tuning = new FishingTuning();
        tuning.Apply(new VoyageSettings());
        Require(tuning.IsReady, "ready only after item load");
        Exact(0.66f, pot.TimeBetweenCatchRolls, "default pot interval");
        Exact(0.125f, net.TimeBetweenCatchRolls, "default net interval");
        Exact(0.01f, manager.GameConfigData.BaseAberrationSpawnChance, "default chance");

        var strong = new VoyageSettings { FishingAssist = 2, Aberration = 2, PotYield = 2, NetYield = 2, PotDurability = 2, NetDurability = 2 };
        tuning.Apply(strong);
        tuning.Apply(strong);
        Near(0.44f, pot.TimeBetweenCatchRolls, "pot frequency 1.5x without compounding");
        Near(1f / 12f, net.TimeBetweenCatchRolls, "net frequency 1.5x without compounding");
        Near(0.04f, manager.GameConfigData.BaseAberrationSpawnChance, "day +3 points");
        Near(0.06f, manager.GameConfigData.NightAberrationSpawnChance, "night +3 points");
        Require(fishing.minTargetWidth == 28 && fishing.maxTargetWidth == 42, "normal radial widths");
        Exact(6f, fishing.specialTargetWidth, "gold width untouched");
        Exact(40f, fishing.rotationSpeed, "radial gold speed untouched");
        Exact(15f, fishing.secondsToPassivelyCatch, "passive progress untouched");
        Exact(0.2f, fishing.targetValue, "progress reward unchanged");
        Near(2f / 1.5f, fishing.speedFactor / fishing.ballTrophySpeedFactor, "gold ball velocity preserved");
        Near(1.5f, fishing.diamondScaleUpTimeSec * fishing.diamondTrophySpeedFactor, "gold diamond timing preserved");
        Exact(20f, dredging.minTargetWidth, "dredging unchanged");
        Exact(0.66f, material.TimeBetweenCatchRolls, "material pot excluded");
        Exact(0.125f, oil.TimeBetweenCatchRolls, "oil net excluded");
        Exact(0.125f, materialNet.TimeBetweenCatchRolls, "material net excluded");
        Exact(3f, pot.MaxDurabilityDays, "saved durability maximum unchanged");
        Exact(1f, pot.CatchRate, "probability unchanged");

        Near(1f - 0.8f / 1.5f, FishingHooks.AdjustPotMaintenance(0.2f, new SerializedCrabPotPOIData { deployableItemId = "pot1" }), "pot wear and book combine");
        Near(1f - 0.8f / 1.5f, FishingHooks.AdjustNetMaintenance(0.2f, new TrawlNetAbility(net)), "net wear and book combine");
        Exact(0.2f, FishingHooks.AdjustPotMaintenance(0.2f, new SerializedCrabPotPOIData { deployableItemId = "tir-pot1" }), "material wear excluded");
        Exact(0.2f, FishingHooks.AdjustNetMaintenance(0.2f, new TrawlNetAbility(oil)), "oil wear excluded");
        var spiral = new SpiralMinigame();
        var originalGates = spiral.Gates.ToArray();
        float goldEnd = spiral.Material.GetFloat("_notch2_end");
        int originalShaderWrites = spiral.Material.Writes;
        FishingHooks.AfterSpiralTargets(spiral);
        Require(spiral.Gates.Count == originalGates.Length, "spiral gate count must remain 3, actual " + spiral.Gates.Count);
        Require(spiral.Draws == 3, "postfix never calls allocating native CreateNotch");
        Near(0.056f, spiral.OriginalNotches[0].y, "spiral ordinary width");
        Exact(0.04f, spiral.OriginalNotches[1].y, "spiral gold width");
        Exact(0.40f, spiral.OriginalNotches[1].x, "spiral gold position");
        for (int i = 0; i < originalGates.Length; i++)
        {
            Require(ReferenceEquals(originalGates[i], spiral.Gates[i]), "gate identity and index unchanged: " + i);
            var notch = spiral.OriginalNotches[i];
            var gate = spiral.Gates[i].Component;
            Exact(notch.x, spiral.Material.GetFloat("_notch" + (i + 1) + "_start"), "shader start matches hit window: " + i);
            Exact(notch.x + notch.y, spiral.Material.GetFloat("_notch" + (i + 1) + "_end"), "shader end matches hit window: " + i);
            Exact(notch.x + notch.y, gate.Position, "existing gate position matches hit window: " + i);
            Exact(notch.x + notch.y, gate.Rotation, "existing gate rotation matches hit window: " + i);
            Exact(17f, gate.ConfigMarker, "existing native spiral configuration reused: " + i);
            Require(gate.PositionCalls == (i == spiral.Trophy ? 1 : 2), "only ordinary gates repositioned: " + i);
            Require(gate.RotationCalls == (i == spiral.Trophy ? 1 : 2), "only ordinary gates rotated: " + i);
            Exact(1f, spiral.Material.GetFloat("_notch" + (i + 1) + "_opacity"), "native opacity preserved: " + i);
        }
        Exact(goldEnd, spiral.Material.GetFloat("_notch2_end"), "gold shader unchanged");
        Require(spiral.Material.Writes == originalShaderWrites + 4, "only two ordinary shader boundaries updated");

        manager.PlayerStats.ResearchedEquipmentMaintenanceModifier = 0.2f;
        var language = new LanguageManager();
        FishingHooks.FormatDeployableDurability(language, 3f, 3f, new LocalizeStringEvent(), "key", new TooltipSectionDeployableDetails(pot));
        Require(language.Calls == 1, "one native async formatter invocation");
        Near(3f * 1.5f / 0.8f, language.LastCurrent * 1.2f, "display equals effective wear duration");
        Exact(3f, language.LastMaximum, "formatter maximum unchanged");

        manager.PlayerStats.ResearchedEquipmentMaintenanceModifier = 0.1f;
        manager.SaveData.Net = new SpatialItemInstance(net);
        VerifyFormat(2.69802237f, 3f, 2.69802237f / 0.6f, new OccasionalGridPanel(pot), false, "deployed pot remaining time");
        VerifyFormat(3f, 3f, 5f, new OccasionalGridPanel(pot), false, "fresh pot remaining time");
        VerifyFormat(0.6f, 1f, 1f, new NetPanel(), false, "equipped net remaining time");
        FishingHooks.Configure(new VoyageSettings { PotDurability = 1, NetDurability = 1 });
        VerifyFormat(2f, 3f, 2f * 1.25f / 0.9f, new OccasionalGridPanel(pot), false, "moderate pot remaining time");
        VerifyFormat(0.5f, 1f, 0.5f * 1.25f / 0.9f, new NetPanel(), false, "moderate net remaining time");
        FishingHooks.Configure(new VoyageSettings { PotDurability = 2 });
        VerifyFormat(0.6f, 1f, 0f, new NetPanel(), true, "independent default net remains native");
        FishingHooks.Configure(strong);
        VerifyFormat(0f, 3f, 0f, new OccasionalGridPanel(pot), true, "broken pot keeps native broken label");
        VerifyFormat(2f, 7f, 0f, new OccasionalGridPanel(material), true, "material pot panel excluded");
        manager.SaveData.Net = new SpatialItemInstance(oil);
        VerifyFormat(0.8f, 1f, 0f, new NetPanel(), true, "oil net panel excluded");
        manager.SaveData.Net = null;
        VerifyFormat(0.8f, 1f, 0f, new NetPanel(), true, "missing equipped net falls back to native");
        VerifyFormat(0.8f, 1f, 0f, new object(), true, "unrecognized formatter owner unchanged");

        tuning.Apply(new VoyageSettings { PotYield = 1 });
        Near(0.528f, pot.TimeBetweenCatchRolls, "changing 2 to 1 uses native baseline");
        Exact(0.125f, net.TimeBetweenCatchRolls, "independent net setting");
        Exact(0.01f, manager.GameConfigData.BaseAberrationSpawnChance, "chance reset by default setting");
        Require(fishing.minTargetWidth == 20 && fishing.maxTargetWidth == 30, "assistance reset");
        var nativeSpiral = new SpiralMinigame();
        FishingHooks.AfterSpiralTargets(nativeSpiral);
        Require(nativeSpiral.Gates.Count == 3 && nativeSpiral.Draws == 3, "default spiral gate count unchanged");
        Require(nativeSpiral.Material.Writes == 9, "default spiral shader untouched");
        Exact(0.04f, nativeSpiral.OriginalNotches[0].y, "default spiral ordinary width untouched");
        Require(nativeSpiral.Gates[0].Component.PositionCalls == 1 && nativeSpiral.Gates[0].Component.RotationCalls == 1, "default spiral transforms untouched");
        Exact(0.2f, FishingHooks.AdjustNetMaintenance(0.2f, null), "default hook returns original without owner access");
        FishingHooks.FormatDeployableDurability(language, 2.25f, 3f, null, "key", null);
        Require(language.Calls == 2, "default formatter exactly once");
        Exact(2.25f, language.LastCurrent, "default formatter input exactly native");
        GameManager.Instance = null;
        VerifyFormat(2.25f, 3f, 0f, new OccasionalGridPanel(pot), true, "default pot panel needs no game manager");
        VerifyFormat(0.75f, 1f, 0f, new NetPanel(), true, "default net panel needs no game manager");
        GameManager.Instance = manager;

        tuning.Restore();
        tuning.Restore();
        Exact(0.66f, pot.TimeBetweenCatchRolls, "restore is complete and idempotent");
        Exact(2f, fishing.speedFactor, "restore ball speed bits");
        Exact(1.5f, fishing.ballTrophySpeedFactor, "restore trophy bits");
        Require(!tuning.IsReady, "restore resets readiness");
        tuning.Apply(strong);
        Near(0.44f, pot.TimeBetweenCatchRolls, "apply after restore recaptures original");
        tuning.Restore();
        VerifyHighBoost(tuning, manager, pot, net, material, oil);
        manager.ItemManager.Loaded = false;
        tuning.Apply(strong);
        Require(!tuning.IsReady, "unloaded manager is not ready");
        Exact(0.66f, pot.TimeBetweenCatchRolls, "unloaded apply cannot mutate");
        manager.ItemManager.Loaded = true;
        manager.GameConfigData.DredgingDifficultyConfigs[HarvestDifficulty.HARD] = fishing;
        bool rejected = false;
        try { tuning.Apply(strong); } catch (InvalidOperationException) { rejected = true; }
        Require(rejected, "shared dredge assets rejected safely");
        Exact(0.01f, manager.GameConfigData.BaseAberrationSpawnChance, "failed apply rolls back earlier changes");
        Exact(0.66f, pot.TimeBetweenCatchRolls, "failed apply preserves tools");

        Require(!FishingRules.IsPot("pot99") && !FishingRules.IsNet("tir-net2") && !FishingRules.IsNet(null), "unsupported tool IDs excluded");
        Exact(0.1f, FishingRules.AberrationChance(0.1f, 0.1f, 2), "chance ceiling remains native");
        Exact(0.66f, FishingRules.Interval(0.66f, 99), "invalid setting falls back to original");
        Require(FishingRules.Interval(0.000001f, 2) > 0f, "tiny interval stays positive");
        Exact(0f, FishingRules.Interval(0f, 2), "invalid original interval is not invented");
        Console.WriteLine("Fishing tuning: " + assertions + " assertions passed (managed doubles; no Unity/game execution).");
    }

    private static void VerifySettings()
    {
        var settings = new VoyageSettings();
        Require(settings.IsDefault, "new preferences remain native by default");
        for (int row = 0; row < 7; row++)
        {
            int maximum = row == 0 || row == 6 ? 2 : 3;
            Require(VoyageSettings.MaximumLevel(row) == maximum, "only intended rows accept the fourth option: " + row);
            for (int level = 0; level <= maximum; level++)
            {
                settings.Set(row, level);
                settings.Validate();
                VoyageSettings restored = JsonConvert.DeserializeObject<VoyageSettings>(JsonConvert.SerializeObject(settings));
                restored.Validate();
                Require(restored.Get(row) == level, "preference JSON round trip: " + row + "/" + level);
            }
            int selected = settings.Get(row);
            Reject<ArgumentOutOfRangeException>(() => settings.Set(row, -1), "negative UI level rejected: " + row);
            Reject<ArgumentOutOfRangeException>(() => settings.Set(row, maximum + 1), "unsupported UI level rejected: " + row);
            Require(settings.Get(row) == selected, "rejected setting leaves previous value intact: " + row);
        }
        VoyageSettings copy = settings.Copy();
        copy.Set(1, 0);
        Require(settings.Aberration == 3 && copy.Aberration == 0, "copied high preferences are independent");
        string[] fields = { "FishingAssist", "Aberration", "PotYield", "PotDurability", "NetYield", "NetDurability", "Pace" };
        for (int row = 0; row < fields.Length; row++)
        {
            foreach (int invalid in new[] { -1, VoyageSettings.MaximumLevel(row) + 1, int.MaxValue })
            {
                var loaded = JsonConvert.DeserializeObject<VoyageSettings>("{\"" + fields[row] + "\":" + invalid + "}");
                Reject<InvalidOperationException>(() => loaded.Validate(), "invalid persisted level rejected: " + fields[row] + "/" + invalid);
            }
        }
        VoyageSettings old = JsonConvert.DeserializeObject<VoyageSettings>("{\"FishingAssist\":2,\"Aberration\":2,\"PotYield\":2,\"PotDurability\":2,\"NetYield\":2,\"NetDurability\":2,\"Pace\":2}");
        old.Validate();
        for (int row = 0; row < 7; row++) Require(old.Get(row) == 2, "previous preferences retain the same level: " + row);
        Require(JsonConvert.DeserializeObject<VoyageSettings>("{}").IsDefault, "missing persisted fields stay native");
        Reject<ArgumentOutOfRangeException>(() => settings.Set(7, 0), "unknown preference row rejected");
    }

    private static void VerifyHighBoost(FishingTuning tuning, GameManager manager, DeployableItemData pot,
        DeployableItemData net, DeployableItemData material, DeployableItemData oil)
    {
        var high = new VoyageSettings { Aberration = 3, PotYield = 3, NetYield = 3, PotDurability = 3, NetDurability = 3 };
        manager.PlayerStats.ResearchedEquipmentMaintenanceModifier = 0.1f;
        manager.SaveData.Net = new SpatialItemInstance(net);
        var placedPot = new SerializedCrabPotPOIData { deployableItemId = "pot1" };
        var activeNet = new TrawlNetAbility(net);
        tuning.Apply(high);
        tuning.Apply(high);
        Near(0.22f, pot.TimeBetweenCatchRolls, "high pot catch checks are 3x without compounding");
        Near(1f / 24f, net.TimeBetweenCatchRolls, "high net catch checks are 3x without compounding");
        Near(0.31f, manager.GameConfigData.BaseAberrationSpawnChance, "high daytime base is 31 percent");
        Near(0.33f, manager.GameConfigData.NightAberrationSpawnChance, "high nighttime base is 33 percent");
        Exact(0.35f, manager.GameConfigData.MaxAberrationSpawnChance, "native 35 percent ordinary cap unchanged");
        Near(0.3f, 1f - FishingHooks.AdjustPotMaintenance(0.1f, placedPot), "high pot wear is one third with research");
        Near(0.3f, 1f - FishingHooks.AdjustNetMaintenance(0.1f, activeNet), "high net wear is one third with research");
        VerifyFormat(3f, 3f, 10f, new TooltipSectionDeployableDetails(pot), false, "high pot tooltip matches wear");
        VerifyFormat(3f, 3f, 10f, new OccasionalGridPanel(pot), false, "high deployed pot duration matches wear");
        VerifyFormat(1f, 1f, 1f / 0.3f, new NetPanel(), false, "high net panel duration matches wear");
        Exact(3f, pot.MaxDurabilityDays, "high boost does not enlarge saved pot durability range");
        Exact(1f, net.MaxDurabilityDays, "high boost does not enlarge saved net durability range");
        Exact(1f, pot.CatchRate, "high boost changes checks, not per-check catch probability");
        Exact(0.66f, material.TimeBetweenCatchRolls, "high boost excludes material pot catch interval");
        Exact(0.125f, oil.TimeBetweenCatchRolls, "high boost excludes oil net catch interval");
        Exact(0.1f, FishingHooks.AdjustPotMaintenance(0.1f, new SerializedCrabPotPOIData { deployableItemId = "tir-pot1" }), "high boost excludes material pot wear");
        Exact(0.1f, FishingHooks.AdjustNetMaintenance(0.1f, new TrawlNetAbility(oil)), "high boost excludes oil net wear");
        VerifyFormat(2f, 7f, 0f, new OccasionalGridPanel(material), true, "high boost excludes material pot display");
        manager.SaveData.Net = new SpatialItemInstance(oil);
        VerifyFormat(0.8f, 1f, 0f, new NetPanel(), true, "high boost excludes oil net display");
        manager.SaveData.Net = new SpatialItemInstance(net);

        tuning.Apply(new VoyageSettings { Aberration = 1, PotYield = 1, NetYield = 1, PotDurability = 1, NetDurability = 1 });
        Near(0.528f, pot.TimeBetweenCatchRolls, "pot 3 to 1 uses original interval");
        Near(0.1f, net.TimeBetweenCatchRolls, "net 3 to 1 uses original interval");
        Near(0.025f, manager.GameConfigData.BaseAberrationSpawnChance, "day 3 to 1 uses original chance");
        Near(0.045f, manager.GameConfigData.NightAberrationSpawnChance, "night 3 to 1 uses original chance");
        Near(0.72f, 1f - FishingHooks.AdjustPotMaintenance(0.1f, placedPot), "pot 3 to 1 uses original wear");
        Near(0.72f, 1f - FishingHooks.AdjustNetMaintenance(0.1f, activeNet), "net 3 to 1 uses original wear");
        VerifyFormat(3f, 3f, 3f / 0.72f, new OccasionalGridPanel(pot), false, "pot 3 to 1 display follows wear");
        VerifyFormat(1f, 1f, 1f / 0.72f, new NetPanel(), false, "net 3 to 1 display follows wear");
        tuning.Apply(new VoyageSettings());
        Exact(0.66f, pot.TimeBetweenCatchRolls, "pot 3 to 1 to 0 restores original bits");
        Exact(0.125f, net.TimeBetweenCatchRolls, "net 3 to 1 to 0 restores original bits");
        Exact(0.01f, manager.GameConfigData.BaseAberrationSpawnChance, "day 3 to 1 to 0 restores original bits");
        Exact(0.03f, manager.GameConfigData.NightAberrationSpawnChance, "night 3 to 1 to 0 restores original bits");
        Exact(0.1f, FishingHooks.AdjustPotMaintenance(0.1f, placedPot), "pot 3 to 1 to 0 restores native wear input");
        Exact(0.1f, FishingHooks.AdjustNetMaintenance(0.1f, activeNet), "net 3 to 1 to 0 restores native wear input");
        VerifyFormat(3f, 3f, 0f, new OccasionalGridPanel(pot), true, "pot 3 to 1 to 0 restores native display input");
        VerifyFormat(1f, 1f, 0f, new NetPanel(), true, "net 3 to 1 to 0 restores native display input");
        tuning.Apply(high);
        tuning.Restore();
        Exact(0.66f, pot.TimeBetweenCatchRolls, "Restore clears high pot frequency");
        Exact(0.125f, net.TimeBetweenCatchRolls, "Restore clears high net frequency");
        Exact(0.01f, manager.GameConfigData.BaseAberrationSpawnChance, "Restore clears high day chance");
        Exact(0.03f, manager.GameConfigData.NightAberrationSpawnChance, "Restore clears high night chance");
        Exact(0.1f, FishingHooks.AdjustPotMaintenance(0.1f, placedPot), "Restore clears high pot wear hook");
        Exact(0.1f, FishingHooks.AdjustNetMaintenance(0.1f, activeNet), "Restore clears high net wear hook");

        Exact(0.35f, FishingRules.AberrationChance(0.2f, 0.35f, 3), "high bonus still clamps at native cap");
        Exact(0.4f, FishingRules.AberrationChance(0.4f, 0.35f, 3), "unusual baseline above cap is not lowered");
        Require(FishingRules.Interval(0.000001f, 3) > 0f, "high interval retains a positive floor");
        Exact(1f, FishingRules.WindowMultiplier(3), "fourth boost never expands fishing assistance");
        Exact(1f, FishingRules.NormalTargetSpeed(3), "fourth boost never changes target speed");
        foreach (int invalid in new[] { -1, 4, int.MaxValue })
        {
            Exact(0.66f, FishingRules.Interval(0.66f, invalid), "invalid boost interval is native: " + invalid);
            Exact(0.1f, FishingRules.Maintenance(0.1f, invalid), "invalid boost wear is native: " + invalid);
            Exact(0.01f, FishingRules.AberrationChance(0.01f, 0.35f, invalid), "invalid boost chance is native: " + invalid);
            FishingHooks.Configure(new VoyageSettings { PotDurability = invalid, NetDurability = invalid });
            Exact(0.1f, FishingHooks.AdjustPotMaintenance(0.1f, placedPot), "invalid pot hook is native: " + invalid);
            Exact(0.1f, FishingHooks.AdjustNetMaintenance(0.1f, activeNet), "invalid net hook is native: " + invalid);
        }
        FishingHooks.Reset();
    }
}
