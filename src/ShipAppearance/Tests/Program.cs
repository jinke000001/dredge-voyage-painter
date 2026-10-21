using System;
using System.Linq;
using Jinke.DredgeShipAppearance;
using Jinke.DredgeMerchantTravel;
using UnityEngine;

internal static class Program
{
    private static int passed;
    private static void Check(bool value, string name)
    { if (!value) throw new Exception("FAIL " + name); passed++; Console.WriteLine("PASS " + name); }
    private sealed class World
    {
        internal readonly SaveData Save = new SaveData();
        internal readonly GameManager Game;
        internal readonly Player Player;
        internal readonly BoatModelProxy Boat;
        internal readonly MeshFilter Filter;
        internal readonly Mesh[] Original = new Mesh[4];
        internal readonly Material[] Materials;
        internal World(int tier = 5, string id = null, bool resourceFailure = false, bool authoredFailure = false)
        {
            ShipAppearance.ResetSession();
            Bootstrap.PainterEnabled = true; Boat5MeshFactory.ThrowCreate = resourceFailure;
            Boat5MeshFactory.Creations = 0; Boat5MeshFactory.Created.Clear();
            Boat5AuthoredAssets.ThrowCreate = authoredFailure; Boat5AuthoredAssets.ThrowId = null; Boat5AuthoredAssets.Created.Clear();
            Save.HullTier = tier;
            if (id != null) Save.stringVariables[ShipAppearance.Key] = id;
            Player = new GameObject("Player").Add(new Player());
            var boatObject = new GameObject("Boat5");
            Boat = boatObject.Add(new BoatModelProxy());
            Boat.BoatMeshRenderer = boatObject.Add(new MeshRenderer());
            Filter = boatObject.Add(new MeshFilter()); Materials = Boat.BoatMeshRenderer.sharedMaterials;
            for (int i = 0; i < 4; i++) { Original[i] = new Mesh { name = "Original" + i }; Boat.DamageStateMeshes.Add(Original[i]); }
            Filter.sharedMesh = Original[0];
            Player.transform.Children["Boat5"] = boatObject.transform;
            Player.BoatModelProxy = Boat;
            if (tier != 5) SetOtherBoat(tier);
            Game = new GameManager { Player = Player, SaveData = Save, SaveManager = new SaveManager { ActiveSaveData = Save } };
            GameManager.Instance = Game; GameEvents.Instance = new GameEvents();
            ShipAppearance.OnSaveReady(Save);
        }
        internal void SetOtherBoat(int tier)
        {
            Save.HullTier = tier;
            Player.BoatModelProxy = new GameObject("Boat" + tier).Add(new BoatModelProxy());
        }
        internal void NativeDamage(int damage, int index)
        {
            Save.Damage = damage;
            // Emulate the native consumer, without calling our DamageIndex.
            Filter.sharedMesh = Boat.DamageStateMeshes[index];
        }
        internal bool Native { get { return Boat.DamageStateMeshes.SequenceEqual(Original); } }
    }
    private static void Main()
    {
        var world = new World(); string error;
        Check(ShipAppearance.Ready && ShipAppearance.SelectedId == ShipAppearance.Original, "default original ready");
        Check(world.Native && !world.Save.stringVariables.ContainsKey(ShipAppearance.Key), "missing key leaves native data unchanged");
        Check(ReferenceEquals(world.Materials, world.Boat.BoatMeshRenderer.sharedMaterials), "material array preserved");
        Check(GameEvents.Instance.Listeners == 1, "one model listener");
        for (int i = 0; i < 10; i++) ShipAppearance.Tick();
        Check(GameEvents.Instance.Listeners == 1 && Boat5MeshFactory.Creations == 1, "ticks reuse one binding and meshes");
        world.Save.Damage = 3;
        Check(ShipAppearance.Change(ShipAppearance.Faceted, out error), "select faceted");
        Check(world.Save.GetStringVariable(ShipAppearance.Key, "") == ShipAppearance.Faceted, "ID written after application");
        Check(world.Filter.sharedMesh == world.Boat.DamageStateMeshes[2] && !world.Native, "selection synchronizes current poor damage");
        var generated = world.Boat.DamageStateMeshes.ToArray();
        world.NativeDamage(1, 1); ShipAppearance.Tick();
        Check(world.Filter.sharedMesh == generated[1], "native damage selects custom ok");
        world.NativeDamage(8, 3); ShipAppearance.Tick();
        Check(world.Filter.sharedMesh == generated[3], "native critical selects custom critical");
        world.NativeDamage(0, 0); ShipAppearance.Tick();
        Check(world.Filter.sharedMesh == generated[0], "native repair selects custom sturdy");
        world.NativeDamage(3, 2);
        Check(ShipAppearance.Change(ShipAppearance.Original, out error), "restore original");
        Check(world.Native && world.Filter.sharedMesh == world.Original[2], "independent backup restores current damage");
        Check(!world.Save.stringVariables.ContainsKey(ShipAppearance.Key), "restore removes selection key");
        Check(world.Original.All(m => !m.Destroyed), "native meshes never destroyed");

        Check(Boat5Binding.DamageIndex(0, 8) == 0 && Boat5Binding.DamageIndex(1, 8) == 1 &&
            Boat5Binding.DamageIndex(2, 8) == 1 && Boat5Binding.DamageIndex(3, 8) == 2 &&
            Boat5Binding.DamageIndex(4, 8) == 2 && Boat5Binding.DamageIndex(5, 8) == 3 &&
            Boat5Binding.DamageIndex(8, 8) == 3, "native damage mapping boundaries");

        world = new World(id: ShipAppearance.Faceted);
        Check(ShipAppearance.SelectedId == ShipAppearance.Faceted, "first bind applies saved selection without an event");
        generated = world.Boat.DamageStateMeshes.ToArray();
        world.Filter.sharedMesh = world.Original[0]; world.Save.Damage = 4;
        GameEvents.Instance.ModelChanged();
        Check(world.Filter.sharedMesh == generated[2], "model event corrects native OnEnable ordering");
        var events = GameEvents.Instance;
        var nextSave = new SaveData();
        world.Game.SaveData = nextSave; world.Game.SaveManager.ActiveSaveData = nextSave;
        ShipAppearance.OnSaveReady(nextSave);
        Check(world.Native && world.Filter.sharedMesh == world.Original[2], "slot change restores old current damage");
        Check(ShipAppearance.SelectedId == ShipAppearance.Original && !nextSave.stringVariables.ContainsKey(ShipAppearance.Key), "new slot stays original");
        Check(generated.All(m => m.Destroyed) && events.Listeners == 1, "old generated resources released and subscription rebound");
        Check(world.Save.GetStringVariable(ShipAppearance.Key, "") == ShipAppearance.Faceted, "slot change preserves previous slot string");
        world.Game.SaveData = world.Save; world.Game.SaveManager.ActiveSaveData = world.Save;
        ShipAppearance.OnSaveReady(world.Save);
        Check(ShipAppearance.SelectedId == ShipAppearance.Faceted, "switch back reads previous slot");

        world = new World(id: "future-version-id");
        Check(world.Native && ShipAppearance.HasUnknownSelection, "unknown selection displays original");
        Check(world.Save.GetStringVariable(ShipAppearance.Key, "") == "future-version-id", "unknown string retained");
        Check(!ShipAppearance.Change("other", out error) && world.Native, "unapproved choice rejected");
        Check(ShipAppearance.Change(ShipAppearance.Original, out error) && !world.Save.stringVariables.ContainsKey(ShipAppearance.Key), "explicit original clears unknown selection");

        world = new World(id: ShipAppearance.Faceted, resourceFailure: true);
        Check(world.Native && !ShipAppearance.Ready && ShipAppearance.SelectedId == ShipAppearance.Original, "resource failure falls back to original");
        for (int i = 0; i < 10; i++) ShipAppearance.Tick();
        Check(Boat5MeshFactory.Creations == 1, "failed resources are not retried each frame");
        Check(!ShipAppearance.Change(ShipAppearance.Faceted, out error) && world.Save.GetStringVariable(ShipAppearance.Key, "") == ShipAppearance.Faceted,
            "failed selection preserves stored ID");
        Check(ShipAppearance.Change(ShipAppearance.Original, out error) && !world.Save.stringVariables.ContainsKey(ShipAppearance.Key), "restore works despite missing resources");
        Boat5MeshFactory.ThrowCreate = false;
        Check(ShipAppearance.Change(ShipAppearance.Faceted, out error), "explicit retry after resources recover");

        world = new World(); world.Filter.ThrowOnce = true;
        Check(!ShipAppearance.Change(ShipAppearance.Faceted, out error), "upload failure rejects choice");
        Check(world.Native && !world.Save.stringVariables.ContainsKey(ShipAppearance.Key), "upload failure rolls back mesh and leaves missing key");
        world.Save.ThrowWrite = true;
        Check(!ShipAppearance.Change(ShipAppearance.Faceted, out error) && world.Native, "write rejection restores previously original mesh");
        world.Save.ThrowWrite = false;
        Check(ShipAppearance.Change(ShipAppearance.Faceted, out error), "selection remains usable after rollback");
        world.Save.ThrowWrite = true;
        Check(!ShipAppearance.Change(ShipAppearance.Faceted, out error) && ShipAppearance.SelectedId == ShipAppearance.Faceted, "write failure preserves previous custom appearance");

        foreach (int tier in new[] { 1, 2, 3, 4, 6 })
        {
            world = new World(tier, ShipAppearance.Faceted);
            Check(!ShipAppearance.IsBoat5 && !ShipAppearance.Ready && world.Native && Boat5MeshFactory.Creations == 0, "unsupported tier " + tier + " untouched");
            Check(!ShipAppearance.Change(ShipAppearance.Original, out error) && world.Save.GetStringVariable(ShipAppearance.Key, "") == ShipAppearance.Faceted,
                "unsupported tier " + tier + " cannot change selection");
        }
        world = new World(4, ShipAppearance.Faceted);
        world.Save.HullTier = 5; world.Player.BoatModelProxy = world.Boat;
        GameEvents.Instance.ModelChanged();
        Check(ShipAppearance.SelectedId == ShipAppearance.Faceted, "upgrade to Boat5 applies retained ID");
        world.SetOtherBoat(4); GameEvents.Instance.ModelChanged();
        Check(world.Native && ShipAppearance.SelectedId == ShipAppearance.Original, "leaving Boat5 restores hidden native list");

        world = new World(id: ShipAppearance.Faceted);
        generated = world.Boat.DamageStateMeshes.ToArray();
        var replacement = new World();
        Check(generated.All(m => m.Destroyed) && world.Native, "explicit session reset releases old meshes");
        ShipAppearance.Change(ShipAppearance.Faceted, out error);
        generated = replacement.Boat.DamageStateMeshes.ToArray();
        replacement.Game.IsPlaying = false; ShipAppearance.Tick();
        Check(replacement.Native && generated.All(m => m.Destroyed) && GameEvents.Instance.Listeners == 0, "title return restores and unsubscribes");
        replacement.Game.IsPlaying = true; ShipAppearance.Tick();
        Check(ShipAppearance.SelectedId == ShipAppearance.Faceted && GameEvents.Instance.Listeners == 1, "world reload rebinds persisted selection");
        replacement.Player.IsDocked = false;
        Check(!ShipAppearance.Change(ShipAppearance.Original, out error), "at sea changes rejected");
        replacement.Player.IsDocked = true; replacement.Save.ForbidSave = true;
        Check(!ShipAppearance.Change(ShipAppearance.Original, out error), "forbidden save changes rejected");
        replacement.Save.ForbidSave = false; replacement.Game.SaveManager.ActiveSaveData = new SaveData();
        Check(!ShipAppearance.Change(ShipAppearance.Original, out error), "inactive slot changes rejected");
        Bootstrap.PainterEnabled = false; ShipAppearance.Tick();
        Check(replacement.Native && GameEvents.Instance.Listeners == 0, "disabled profile restores and releases");
        Check(!ShipAppearance.Change(ShipAppearance.Faceted, out error), "non-painter profile cannot select");
        Bootstrap.PainterEnabled = true; ShipAppearance.ResetSession();

        world = new World(id: ShipAppearance.Faceted);
        generated = world.Boat.DamageStateMeshes.ToArray();
        var freshPlayer = new GameObject("Player").Add(new Player());
        var freshObject = new GameObject("Boat5");
        var freshBoat = freshObject.Add(new BoatModelProxy());
        freshBoat.BoatMeshRenderer = freshObject.Add(new MeshRenderer());
        var freshFilter = freshObject.Add(new MeshFilter());
        freshBoat.DamageStateMeshes.AddRange(world.Original);
        freshFilter.sharedMesh = world.Original[0];
        freshPlayer.transform.Children["Boat5"] = freshObject.transform;
        freshPlayer.BoatModelProxy = freshBoat;
        world.Save.Damage = 3;
        world.Game.PlayerStats.DamageThreshold = 3;
        world.Game.Player = freshPlayer;
        var oldEvents = GameEvents.Instance;
        GameEvents.Instance = new GameEvents();
        ShipAppearance.Tick();
        Check(world.Native && generated.All(m => m.Destroyed), "same-slot player rebuild restores and frees old binding");
        Check(world.Filter.sharedMesh == world.Original[2], "old player restores using its previous damage threshold");
        Check(freshFilter.sharedMesh == freshBoat.DamageStateMeshes[3] && freshFilter.sharedMesh != world.Original[3], "same-slot player rebuild uses new threshold");
        Check(oldEvents.Listeners == 0 && GameEvents.Instance.Listeners == 1, "recreated event singleton detaches previous listener");
        int creations = Boat5MeshFactory.Creations;
        oldEvents = GameEvents.Instance; GameEvents.Instance = new GameEvents();
        ShipAppearance.Tick();
        Check(oldEvents.Listeners == 0 && GameEvents.Instance.Listeners == 1 && Boat5MeshFactory.Creations == creations,
            "event singleton replacement keeps the live mesh binding");
        ShipAppearance.ResetSession();
        Check(freshBoat.DamageStateMeshes.SequenceEqual(world.Original), "recreated player restores native snapshot");

        int albedoKey = Shader.PropertyToID(Boat5WindowOverrides.AlbedoName);
        int emissionKey = Shader.PropertyToID(Boat5WindowOverrides.EmissionName);
        int otherKey = Shader.PropertyToID("other-writer-float");
        int lightKey = Shader.PropertyToID("_LightStrength");
        foreach (string id in new[] { ShipAppearance.Cthulhu, ShipAppearance.Cyberpunk })
        {
            world = new World(id: id);
            var owned = Boat5AuthoredAssets.Created.Single();
            var renderer = world.Boat.BoatMeshRenderer;
            Check(ShipAppearance.SelectedId == id && !world.Native, "saved authored selection first-binds " + id);
            Check(renderer.Blocks[1].GetTexture(albedoKey) == owned.Albedo && renderer.Blocks[1].GetTexture(emissionKey) == owned.Emission,
                "paired authored atlas applied " + id);
            Check(!renderer.Blocks[1].Floats.ContainsKey(lightKey), "no light strength override " + id);
            for (int state = 0; state < 4; state++)
            {
                world.NativeDamage(new[] { 0, 1, 3, 8 }[state], state);
                ShipAppearance.Tick();
                Check(world.Filter.sharedMesh == owned.Meshes[state], "native authored state " + id + " " + state);
            }
            world.Save.Damage = 3; world.Filter.sharedMesh = world.Original[0];
            GameEvents.Instance.ModelChanged();
            Check(world.Filter.sharedMesh == owned.Meshes[2], "authored model event reselects current state " + id);
            Check(ShipAppearance.Change(ShipAppearance.Original, out error) && world.Native && world.Filter.sharedMesh == world.Original[2],
                "authored original restores native current state " + id);
            Check(renderer.Blocks[1].GetTexture(albedoKey) == renderer.sharedMaterials[1].GetTexture(albedoKey) &&
                renderer.Blocks[1].GetTexture(emissionKey) == renderer.sharedMaterials[1].GetTexture(emissionKey), "authored atlas visually restored " + id);
            Check(!world.Save.stringVariables.ContainsKey(ShipAppearance.Key), "authored restore removes saved key " + id);
            ShipAppearance.ResetSession();
            Check(owned.Meshes.All(m => m.Destroyed) && owned.Albedo.Destroyed && owned.Emission.Destroyed, "authored resource cleanup " + id);
        }

        world = new World();
        var slot0 = new MaterialPropertyBlock(); slot0.SetFloat(otherKey, 7f);
        var slot1 = new MaterialPropertyBlock(); slot1.SetFloat(otherKey, 9f);
        var windowRenderer = world.Boat.BoatMeshRenderer;
        windowRenderer.SetPropertyBlock(slot0, 0); windowRenderer.SetPropertyBlock(slot1, 1);
        Check(ShipAppearance.Change(ShipAppearance.Cthulhu, out error), "select Cthulhu from original");
        var cult = Boat5AuthoredAssets.Created.Single();
        Check(windowRenderer.Blocks[0].GetFloat(otherKey) == 7f && windowRenderer.Blocks[1].GetFloat(otherKey) == 9f,
            "slot0 painter and unrelated slot1 keys preserved");
        Check(ReferenceEquals(world.Materials, windowRenderer.sharedMaterials) && world.Materials[1].Textures.Values.All(t => t != cult.Albedo && t != cult.Emission),
            "native material objects and textures unmodified");
        windowRenderer.sharedMaterials[1].Values["_LightStrength"] = 4f;
        GameEvents.Instance.ModelChanged();
        Check(!windowRenderer.Blocks[1].Floats.ContainsKey(lightKey) && windowRenderer.sharedMaterials[1].Values["_LightStrength"] == 4f,
            "native light intensity remains material-owned after reapply");
        Check(ShipAppearance.Change(ShipAppearance.Cyberpunk, out error) && ShipAppearance.SelectedId == ShipAppearance.Cyberpunk,
            "switch Cthulhu to cyberpunk");
        var cyber = Boat5AuthoredAssets.Created.Last();
        Check(windowRenderer.Blocks[1].GetTexture(albedoKey) == cyber.Albedo, "switch replaces only owned texture pair");
        world.Save.ThrowWrite = true;
        Check(!ShipAppearance.Change(ShipAppearance.Cthulhu, out error) && ShipAppearance.SelectedId == ShipAppearance.Cyberpunk,
            "write rejection rolls back previous authored ID");
        Check(windowRenderer.Blocks[1].GetTexture(albedoKey) == cyber.Albedo && world.Boat.DamageStateMeshes.SequenceEqual(cyber.Meshes) &&
            world.Save.GetStringVariable(ShipAppearance.Key, "") == ShipAppearance.Cyberpunk, "write rejection restores previous mesh atlas and string");
        world.Save.ThrowWrite = false; world.Filter.ThrowOnce = true;
        Check(!ShipAppearance.Change(ShipAppearance.Cthulhu, out error) && windowRenderer.Blocks[1].GetTexture(emissionKey) == cyber.Emission &&
            world.Boat.DamageStateMeshes.SequenceEqual(cyber.Meshes), "mesh upload failure rolls back previous authored atlas");
        windowRenderer.ThrowBlockOnce = true;
        Check(!ShipAppearance.Change(ShipAppearance.Cthulhu, out error) && windowRenderer.Blocks[1].GetTexture(albedoKey) == cyber.Albedo &&
            ShipAppearance.SelectedId == ShipAppearance.Cyberpunk, "block upload failure preserves previous authored appearance");
        Check(ShipAppearance.Change(ShipAppearance.Faceted, out error) && windowRenderer.Blocks[1].GetTexture(albedoKey) == windowRenderer.sharedMaterials[1].GetTexture(albedoKey),
            "legacy faceted selection removes authored texture override");
        Check(ShipAppearance.Change(ShipAppearance.Cthulhu, out error), "cached authored resource can be reapplied");
        var foreign = new Texture { name = "OtherModTexture" };
        windowRenderer.GetPropertyBlock(slot1, 1); slot1.SetTexture(albedoKey, foreign); slot1.SetFloat(otherKey, 42f);
        windowRenderer.SetPropertyBlock(slot1, 1);
        Check(ShipAppearance.Change(ShipAppearance.Cyberpunk, out error) && ShipAppearance.Change(ShipAppearance.Original, out error),
            "switch and restore after another texture writer");
        Check(windowRenderer.Blocks[1].GetTexture(albedoKey) == foreign && windowRenderer.Blocks[1].GetFloat(otherKey) == 42f,
            "restore preserves later writer baseline and unrelated values");
        ShipAppearance.ResetSession();
        Check(!foreign.Destroyed && cult.Albedo.Destroyed && cyber.Albedo.Destroyed, "cleanup destroys only owned atlases");

        world = new World(); windowRenderer = world.Boat.BoatMeshRenderer;
        var foreignAlbedo = new Texture(); var foreignEmission = new Texture();
        slot1 = new MaterialPropertyBlock(); slot1.SetTexture(albedoKey, foreignAlbedo); slot1.SetTexture(emissionKey, foreignEmission);
        windowRenderer.SetPropertyBlock(slot1, 1);
        ShipAppearance.Change(ShipAppearance.Cthulhu, out error); ShipAppearance.Change(ShipAppearance.Original, out error);
        Check(windowRenderer.Blocks[1].GetTexture(albedoKey) == foreignAlbedo && windowRenderer.Blocks[1].GetTexture(emissionKey) == foreignEmission,
            "preexisting texture block restored exactly");
        ShipAppearance.Change(ShipAppearance.Cyberpunk, out error);
        windowRenderer.GetPropertyBlock(slot1, 1); slot1.SetTexture(emissionKey, foreignEmission); windowRenderer.SetPropertyBlock(slot1, 1);
        ShipAppearance.Change(ShipAppearance.Original, out error);
        Check(windowRenderer.Blocks[1].GetTexture(emissionKey) == foreignEmission, "restore does not clobber an active external texture takeover");

        world = new World(id: ShipAppearance.Cthulhu, authoredFailure: true);
        Check(world.Native && !ShipAppearance.Ready && world.Save.GetStringVariable(ShipAppearance.Key, "") == ShipAppearance.Cthulhu,
            "missing authored resource falls back and retains string");
        Check(ShipAppearance.Change(ShipAppearance.Original, out error) && !world.Save.stringVariables.ContainsKey(ShipAppearance.Key),
            "missing authored resource still permits explicit original");
        Boat5AuthoredAssets.ThrowCreate = false;
        Check(ShipAppearance.Change(ShipAppearance.Cyberpunk, out error), "explicit authored resource retry succeeds");
        var previousResources = Boat5AuthoredAssets.Created.Last();
        var replacementSave = new SaveData();
        world.Game.SaveData = replacementSave; world.Game.SaveManager.ActiveSaveData = replacementSave;
        ShipAppearance.OnSaveReady(replacementSave);
        Check(world.Native && previousResources.Meshes.All(m => m.Destroyed) && previousResources.Albedo.Destroyed && previousResources.Emission.Destroyed,
            "slot switch detaches and disposes authored mesh atlas pair");
        world = new World();
        Boat5AuthoredAssets.ThrowId = ShipAppearance.Cthulhu;
        world.Save.stringVariables[ShipAppearance.Key] = ShipAppearance.Cthulhu;
        ShipAppearance.Tick();
        Check(world.Native && !ShipAppearance.Ready, "unavailable saved ID releases binding before explicit recovery");
        Check(ShipAppearance.Change(ShipAppearance.Cyberpunk, out error) && ShipAppearance.SelectedId == ShipAppearance.Cyberpunk,
            "unavailable saved ID does not block choosing another available appearance");
        foreach (string id in new[] { ShipAppearance.Cthulhu, ShipAppearance.Cyberpunk })
        {
            world = new World(4, id);
            Check(world.Native && Boat5AuthoredAssets.Created.Count == 0 && world.Save.GetStringVariable(ShipAppearance.Key, "") == id,
                "unsupported tier never loads authored resources " + id);
        }
        ShipAppearance.ResetSession();
        Console.WriteLine("Ship appearance lifecycle: " + passed + " passed; substitutes only, no Unity or save-file execution.");
    }
}
