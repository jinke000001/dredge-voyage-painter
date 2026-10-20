using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Jinke.DredgeMerchantTravel;
using Newtonsoft.Json;

internal static class Program
{
    private static int passed;
    private static int failed;
    private static readonly string FixtureRoot = Path.Combine(AppContext.BaseDirectory, "fixture-" + Guid.NewGuid().ToString("N"));
    private static readonly string HashA = ProfileContract.HashText("original saved bytes A");

    private static int Main()
    {
        Directory.CreateDirectory(FixtureRoot);
        try
        {
            Test("free-sailing accepted", () => Equal(TravelRules.Blockers(Safe()).Count, 0));
            string[] required = { "Ready", "Playing", "Alive", "BaseActionLayer", "MovementAllowed", "InputEnabled", "NormalTimeScale", "Focused" };
            foreach (string field in required) Test("required-state " + field, () =>
            {
                GateState state = Safe();
                typeof(GateState).GetField(field).SetValue(state, false);
                True(TravelRules.Blockers(state).Count > 0);
            });
            string[] forbidden = { "Docked", "AutoMoving", "Docking", "Fishing", "Harvesting", "Paused", "WindowOpen", "Dialogue", "Popup", "Cutscene",
                "Finale", "CastingForbidden", "ForbidSave", "NativeTeleport", "OwnTeleport", "Immune", "ForcedTime", "FrozenTime", "Binding", "AbilityCharging", "StorageError" };
            foreach (string field in forbidden) Test("blocked-state " + field, () =>
            {
                GateState state = Safe();
                typeof(GateState).GetField(field).SetValue(state, true);
                True(TravelRules.Blockers(state).Count > 0);
            });
            Test("progress is read as independent requirements", () =>
            {
                True(TravelRules.Eligibility(false, true, false, true) != null);
                True(TravelRules.Eligibility(true, false, false, true) != null);
                True(TravelRules.Eligibility(true, true, true, false) != null);
                Equal(TravelRules.Eligibility(true, true, true, true), null);
                Equal(TravelRules.Eligibility(true, true, false, false), null);
            });
            Test("six exact visited destinations plus original Blackstone", () =>
            {
                Equal(Destination.All.Length, 6);
                var unique = new HashSet<string>();
                foreach (Destination item in Destination.All) True(unique.Add(item.Id) && Destination.Find(item.Id) == item);
                Equal(Destination.Find("0,0,0"), null);
                Equal(Destination.Find("DOCK.PONTOON-GC"), null);
                Equal(Destination.Find(null), null);
                Equal(Destination.Find("dock.pontoon-tpr").RequiredDlc, 1);
                Equal(Destination.Find("dock.the-iron-rig").RequiredDlc, 2);
            });
            Test("disabled collision shapes do not block arrival", () =>
            {
                Equal(LandingRules.Classify(false, false, false, false, true), LandingColliderRole.Disabled);
                Equal(LandingRules.Classify(false, true, true, false, true), LandingColliderRole.Disabled);
            });
            Test("non-trigger fish and water surfaces follow the actual hull layer matrix", () =>
            {
                Equal(LandingRules.Classify(true, false, false, true, false), LandingColliderRole.LayerIgnored);
                Equal(LandingRules.Classify(true, false, false, false, false), LandingColliderRole.LayerIgnored);
                Equal(LandingRules.Classify(true, false, false, true, true), LandingColliderRole.Solid);
                Equal(LandingRules.Classify(true, false, false, false, true), LandingColliderRole.Solid);
            });
            Test("normal environmental triggers keep native effects without becoming walls", () =>
            {
                Equal(LandingRules.Classify(true, false, true, true, true), LandingColliderRole.OrdinarySensor);
                Equal(LandingRules.Classify(true, false, true, true, false), LandingColliderRole.OrdinarySensor);
            });
            Test("automatic hazards cannot be filtered by a benign component or a hull-only layer", () =>
            {
                Equal(LandingRules.Classify(true, true, true, true, false), LandingColliderRole.AutomaticHazard);
                Equal(LandingRules.Classify(true, true, false, true, false), LandingColliderRole.AutomaticHazard);
                True(LandingRules.BlocksLanding(LandingColliderRole.AutomaticHazard));
            });
            Test("unknown triggers on a colliding layer remain conservative", () =>
            {
                Equal(LandingRules.Classify(true, false, true, false, true), LandingColliderRole.UnknownTrigger);
                True(LandingRules.BlocksLanding(LandingColliderRole.UnknownTrigger));
                True(LandingRules.BlocksLanding(LandingColliderRole.Solid));
                True(!LandingRules.BlocksLanding(LandingColliderRole.Disabled));
                True(!LandingRules.BlocksLanding(LandingColliderRole.LayerIgnored));
                True(!LandingRules.BlocksLanding(LandingColliderRole.OrdinarySensor));
            });
            Test("water footprint checks every corner and edge, not only its centre", () =>
            {
                var water = new float[] { 1, 1, 1, 1, .1f, 1, 1, 1, 1 };
                True(LandingRules.HasWaterFootprint(water));
                for (int i = 0; i < water.Length; i++)
                {
                    float original = water[i]; water[i] = 0;
                    True(!LandingRules.HasWaterFootprint(water)); water[i] = original;
                }
                for (int i = 0; i < water.Length; i++) water[i] = LandingRules.MinimumWaterDepth;
                True(LandingRules.HasWaterFootprint(water));
            });
            Test("missing or invalid water data fails closed", () =>
            {
                True(!LandingRules.HasWaterFootprint(null));
                True(!LandingRules.HasWaterFootprint(new float[1]));
                var water = new float[] { 1, 1, 1, 1, 1, 1, 1, 1, 1 };
                foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -.1f, 1.01f, .019f })
                { water[4] = invalid; True(!LandingRules.HasWaterFootprint(water)); }
            });
            Test("geometry all-false results reproduce QA05 failure and are rejected", () =>
            {
                True(!LandingRules.GeometrySelfTestPassed(true, new bool[7], new bool[7]));
                True(!LandingRules.GeometrySelfTestPassed(false, new[] { true, true, true, true, true, true, true }, new bool[7]));
                True(!LandingRules.GeometrySelfTestPassed(true, null, new bool[7]));
                True(!LandingRules.GeometrySelfTestPassed(true, new bool[6], new bool[7]));
            });
            Test("every supported shape requires both overlap and separation evidence", () =>
            {
                var positive = new[] { true, true, true, true, true, true, true };
                var negative = new bool[7];
                True(LandingRules.GeometrySelfTestPassed(true, positive, negative));
                for (int i = 0; i < 7; i++)
                {
                    positive[i] = false; True(!LandingRules.GeometrySelfTestPassed(true, positive, negative)); positive[i] = true;
                    negative[i] = true; True(!LandingRules.GeometrySelfTestPassed(true, positive, negative)); negative[i] = false;
                }
            });
            Test("all seven radial directions map to their own wedge", () =>
            {
                Equal(RadialRules.Count, Destination.All.Length + 1);
                for (int i = 0; i < RadialRules.Count; i++)
                {
                    double radians = i * Math.PI * 2 / RadialRules.Count;
                    Equal(RadialRules.Selection(Math.Sin(radians) * 200, Math.Cos(radians) * 200, 96), i);
                }
                Equal(RadialRules.Selection(0, 0, 96), -1);
                Equal(RadialRules.Selection(0, 96, 96), -1);
                Equal(RadialRules.Selection(0, 97, 96), 0);
                Equal(RadialRules.Selection(double.NaN, 200, 96), -1);
                Equal(RadialRules.Selection(0, double.PositiveInfinity, 96), -1);
            });
            Test("first native manifest use has no cooldown without saved history", () =>
            {
                Equal(RadialRules.NativeCooldownRemaining(double.PositiveInfinity, .5), 0d);
                Equal(RadialRules.NativeCooldownRemaining(0, .5), .5);
                Equal(RadialRules.NativeCooldownRemaining(.25, .5), .25);
                Equal(RadialRules.NativeCooldownRemaining(.5, .5), 0d);
                Equal(RadialRules.NativeCooldownRemaining(10, .5), 0d);
                // A rolled-back game clock follows native history instead of granting a second cooldown pool.
                Equal(RadialRules.NativeCooldownRemaining(-.2, .5), .7);
                Throws(() => RadialRules.NativeCooldownRemaining(double.NegativeInfinity, .5));
                Throws(() => RadialRules.NativeCooldownRemaining(double.NaN, .5));
                Throws(() => RadialRules.NativeCooldownRemaining(1, double.PositiveInfinity));
            });
            Test("native commit ticket matches exact owner and ability only once", () =>
            {
                var owner = new object(); var ability = new object(); var gate = new SingleCommitTicket();
                True(!gate.Consume(owner, ability)); gate.Begin(owner, ability);
                True(!gate.Consume(new object(), ability)); True(!gate.Consume(owner, new object()));
                True(gate.Consume(owner, ability)); True(gate.Consumed); True(gate.InProgress);
                True(!gate.Consume(owner, ability)); Throws(() => gate.Begin(owner, ability));
                gate.End(); True(!gate.InProgress); True(!gate.Consume(owner, ability));
                gate.Begin(owner, ability); True(!gate.Consumed); gate.End();
                Throws(() => gate.Begin(null, ability));
            });
            Test("save hashing is read-only and bounded", () =>
            {
                string path = FilePath("save.bin"); File.WriteAllText(path, "original saved bytes A");
                string hash = ProfileContract.HashFile(path, 1024);
                Equal(hash, HashA);
                Throws(() => ProfileContract.HashFile(path, 2));
                Equal(File.ReadAllText(path), "original saved bytes A");
            });
            Test("wrong app identity rejected before file access", () =>
            {
                Throws(() => ProfileContract.Verify("com.BlackSaltGames.DREDGE", ProfileContract.Company, ProfileContract.Product,
                    "/wrong/Contents", "/wrong/profile", "/wrong/runtime", "/wrong/home", "/wrong/cwd", null, null));
            });
            Test("runtime outside isolated output is rejected", () =>
            {
                Throws(() => ProfileContract.Verify(ProfileContract.Identifier, ProfileContract.Company, ProfileContract.Product,
                    "/Applications/DREDGE.app/Contents", "/wrong/profile", "/wrong/runtime", "/wrong/home", "/wrong/cwd", null, null));
            });
            Test("fixed Voyage revisions require their matching product identities", () =>
            {
                foreach (var pair in new[] {
                    new[] { ProfileContract.VoyageIdentifier, "DREDGE Voyage QA 20261018" },
                    new[] { ProfileContract.VoyageRevisionIdentifier, "DREDGE Voyage QA 20261019" },
                    new[] { ProfileContract.PainterIdentifier, "DREDGE Voyage Painter QA 20261019" } })
                {
                    try
                    {
                        ProfileContract.Verify(pair[0], ProfileContract.Company, pair[1],
                            "/wrong", "/wrong/profile", "/wrong/runtime", "/wrong/home", "/wrong/cwd", null, null);
                        throw new Exception("Invalid data path unexpectedly accepted.");
                    }
                    catch (InvalidDataException error) { Equal(error.Message, "Unexpected macOS player data path."); }
                    try
                    {
                        ProfileContract.Verify(pair[0], ProfileContract.Company, ProfileContract.Product,
                            "/wrong", "/wrong/profile", "/wrong/runtime", "/wrong/home", "/wrong/cwd", null, null);
                        throw new Exception("Mixed product identity unexpectedly accepted.");
                    }
                    catch (InvalidDataException error) { Equal(error.Message, "Isolation mismatch: product"); }
                }
            });
            Test("launcher Resources cwd is accepted without changing process state", () =>
            {
                string app = ProfileContract.OutputRoot + "/runtime/DREDGE-MerchantTravel-QA.app";
                string resources = app + "/Contents/Resources";
                string before = Environment.CurrentDirectory;
                ProfileContract.RequireStartupWorkingDirectory(resources, resources, app);
                Equal(Environment.CurrentDirectory, before);
            });
            Test("Unity app-parent cwd is accepted without changing process state", () =>
            {
                string app = ProfileContract.OutputRoot + "/runtime/DREDGE-MerchantTravel-QA.app";
                string before = Environment.CurrentDirectory;
                ProfileContract.RequireStartupWorkingDirectory(Path.GetDirectoryName(app), app + "/Contents/Resources", app);
                Equal(Environment.CurrentDirectory, before);
            });
            Test("unrelated cwd and another app Resources are rejected", () =>
            {
                string app = ProfileContract.OutputRoot + "/runtime/DREDGE-MerchantTravel-QA.app";
                string resources = app + "/Contents/Resources";
                foreach (string cwd in new[] { "/tmp", ProfileContract.OutputRoot, app, app + "/Contents",
                    ProfileContract.OutputRoot + "/runtime/Other.app/Contents/Resources", "/Applications/DREDGE.app/Contents/Resources" })
                    Throws(() => ProfileContract.RequireStartupWorkingDirectory(cwd, resources, app));
            });
            Test("Resources must belong to the same verified app", () =>
            {
                string app = ProfileContract.OutputRoot + "/runtime/DREDGE-MerchantTravel-QA.app";
                Throws(() => ProfileContract.RequireStartupWorkingDirectory(Path.GetDirectoryName(app),
                    ProfileContract.OutputRoot + "/runtime/Other.app/Contents/Resources", app));
            });
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Test("Darwin lstat layout accepts ordinary profile files", () =>
                {
                    string folder = Path.Combine(FixtureRoot, "regular-profile"); Directory.CreateDirectory(folder);
                    File.WriteAllText(Path.Combine(folder, "one.bin"), "isolated");
                    InspectTree(folder);
                });
                Test("recursive profile symlinks are rejected without reading targets", () =>
                {
                    string folder = Path.Combine(FixtureRoot, "symlink-profile"); Directory.CreateDirectory(folder);
                    File.CreateSymbolicLink(Path.Combine(folder, "dredge-save0.bin"), Path.Combine(FixtureRoot, "missing-target.bin"));
                    Throws(() => InspectTree(folder));
                    File.Delete(Path.Combine(folder, "dredge-save0.bin"));
                });
                Test("recursive profile hard links are rejected", () =>
                {
                    // The workspace is ExFAT. Only this fixture uses the approved system APFS temp directory.
                    string folder = Path.Combine(Path.GetTempPath(), "jinke-merchanttravel-hardlink-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(folder);
                    try
                    {
                        string original = Path.Combine(folder, "one.bin"); File.WriteAllText(original, "isolated");
                        Equal(Link(original, Path.Combine(folder, "two.bin")), 0);
                        Throws(() => InspectTree(folder));
                    }
                    finally { Directory.Delete(folder, true); }
                });
            }
        }
        finally { Directory.Delete(FixtureRoot, true); }
        Console.WriteLine("MerchantTravel offline checks: " + passed + " passed, " + failed + " failed.");
        Console.WriteLine("These tests do not validate Unity scenes, actual teleport, UI, native input, or save callbacks.");
        return failed == 0 ? 0 : 1;
    }

    private static GateState Safe() { return new GateState { Ready = true, Playing = true, Alive = true, BaseActionLayer = true,
        MovementAllowed = true, InputEnabled = true, NormalTimeScale = true, Focused = true,
        Docked = false, AutoMoving = false, Docking = false, Fishing = false, Harvesting = false, Paused = false,
        WindowOpen = false, Dialogue = false, Popup = false, Cutscene = false, Finale = false, CastingForbidden = false,
        ForbidSave = false, NativeTeleport = false, OwnTeleport = false, Immune = false, ForcedTime = false, FrozenTime = false,
        Binding = false, AbilityCharging = false, StorageError = false }; }
    private static string FilePath(string name) { return Path.Combine(FixtureRoot, name); }
    private static void InspectTree(string path)
    {
        try { typeof(ProfileContract).GetMethod("InspectProfileTree", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { path }); }
        catch (TargetInvocationException error) { throw error.InnerException ?? error; }
    }
    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int Link(string existing, string alias);
    private static void Test(string name, Action action)
    { try { action(); passed++; Console.WriteLine("PASS " + name); } catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error); } }
    private static void True(bool value) { if (!value) throw new Exception("Assertion failed."); }
    private static void Equal<T>(T actual, T expected)
    { if (!EqualityComparer<T>.Default.Equals(actual, expected)) throw new Exception("Expected " + expected + "; got " + actual); }
    private static void Throws(Action action)
    { try { action(); } catch (Exception) { return; } throw new Exception("Expected rejection."); }
}
