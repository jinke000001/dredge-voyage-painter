using System.Collections.ObjectModel;

namespace Jinke.DredgeSkins.Weaver;

public enum HookPlacement { BeforeReturn, BeforeBody, ConditionalBeforeBody, AfterCallTransformSingle, ReplaceCallWithOwner, BeforeBodyWithOwner }

public record HookSpec(string Method, string Hook, HookPlacement Placement, string? Callee = null, int ExpectedCalls = 0, string? CalleeAssembly = null,
    bool AllowUnprotectedReturn = false);

// These are fixed, audited products, not a caller-supplied patch definition.
public sealed class PatchProfile
{
    public static PatchProfile Skins { get; } = new("skins", "Jinke.DredgeSkins",
        new("System.Void PlayerColorCustomizer::RefreshBoatColors()", "OnColors", HookPlacement.BeforeReturn),
        new("System.Void PlayerColorCustomizer::RefreshBoatFlag()", "OnFlag", HookPlacement.BeforeReturn));

    public static PatchProfile MerchantTravel { get; } = new("merchant-travel", "Jinke.DredgeMerchantTravel",
        new("System.Void SteamManager::Awake()", "EnsureIsolatedProfile", HookPlacement.BeforeBody),
        new("System.Void SaveManager::Init()", "EnsureIsolatedProfile", HookPlacement.BeforeBody),
        new("System.Void PlayerColorCustomizer::RefreshBoatColors()", "OnPlayer", HookPlacement.BeforeReturn),
        new("System.Void AbilityBarUI::OnCurrentPressComplete()", "BeforeAbilityComplete", HookPlacement.ConditionalBeforeBody));

    public static PatchProfile VoyageExperience { get; } = new("voyage-experience", "Jinke.DredgeMerchantTravel",
        MerchantTravel.Hooks.Values.Concat(new HookSpec[]
        {
            new("System.Boolean SerializedCrabPotPOIData::AdjustDurability(System.Single)", "AdjustPotMaintenance", HookPlacement.AfterCallTransformSingle,
                "System.Single PlayerStats::get_ResearchedEquipmentMaintenanceModifier()", 1, "Assembly-CSharp"),
            new("System.Void TrawlNetAbility::Update()", "AdjustNetMaintenance", HookPlacement.AfterCallTransformSingle,
                "System.Single PlayerStats::get_ResearchedEquipmentMaintenanceModifier()", 1, "Assembly-CSharp"),
            new("System.Void TooltipSectionDeployableDetails::RefreshUI()", "FormatDeployableDurability", HookPlacement.ReplaceCallWithOwner,
                "System.Void LanguageManager::FormatTimeStringForDurability(System.Single,System.Single,UnityEngine.Localization.Components.LocalizeStringEvent,System.String)", 2, "Assembly-CSharp"),
            new("System.Void OccasionalGridPanel::RefreshDurabilityUI()", "FormatDeployableDurability", HookPlacement.ReplaceCallWithOwner,
                "System.Void LanguageManager::FormatTimeStringForDurability(System.Single,System.Single,UnityEngine.Localization.Components.LocalizeStringEvent,System.String)", 1, "Assembly-CSharp"),
            new("System.Void NetPanel::RefreshUI()", "FormatDeployableDurability", HookPlacement.ReplaceCallWithOwner,
                "System.Void LanguageManager::FormatTimeStringForDurability(System.Single,System.Single,UnityEngine.Localization.Components.LocalizeStringEvent,System.String)", 1, "Assembly-CSharp"),
            new("System.Void SpiralMinigame::GenerateTargets()", "AfterSpiralTargets", HookPlacement.BeforeReturn),
            new("System.Void PlayerSanity::Update()", "AdjustSanityDeltaTime", HookPlacement.AfterCallTransformSingle,
                "System.Single UnityEngine.Time::get_deltaTime()", 1, "UnityEngine.CoreModule"),
            new("System.Void SerializableGrid::Init(GridConfiguration,System.Boolean)", "AfterGridInitialized", HookPlacement.BeforeReturn),
            new("System.Void SaveData::Init()", "AfterSaveDataInit", HookPlacement.BeforeReturn, AllowUnprotectedReturn: true),
            new("System.Void UpgradeManager::OnUpgradesChanged(UpgradeData)", "AfterUpgradesChanged", HookPlacement.BeforeReturn),
        }).ToArray());

    public static PatchProfile VoyagePainter { get; } = new("voyage-painter", "Jinke.DredgeMerchantTravel",
        VoyageExperience.Hooks.Values.Concat(new HookSpec[]
        {
            new("System.Void PlayerColorCustomizer::RefreshBoatFlag()", "OnPainterFlag", HookPlacement.BeforeReturn),
            new("System.Void DredgeDialogueRunner::Awake()", "OnPainterRunnerAwake", HookPlacement.BeforeReturn),
            new("System.Void DredgeDialogueRunner::StartDialogue(System.String)", "BeforePainterDialogue", HookPlacement.BeforeBodyWithOwner),
        }).ToArray());

    private PatchProfile(string id, string runtimeName, params HookSpec[] hooks)
    {
        Id = id;
        RuntimeName = runtimeName;
        HookType = runtimeName + ".Bootstrap";
        Hooks = new ReadOnlyDictionary<string, HookSpec>(hooks.ToDictionary(h => h.Method, StringComparer.Ordinal));
    }

    public string Id { get; }
    public string RuntimeName { get; }
    public string HookType { get; }
    public IReadOnlyDictionary<string, HookSpec> Hooks { get; }
}
