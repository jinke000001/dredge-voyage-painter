using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jinke.DredgeMerchantTravel
{
    internal sealed class LandingCandidate
    {
        public string id;
        public float x, z, heading, depth;
        public float footprintMinDepth;
        public float[] waterDepthSamples;
        public string zone;
        public bool safe;
        public int broadphaseMatches, narrowphaseTests, narrowphaseClear;
        public List<string> reasons = new List<string>();
        internal Vector3 Position { get { return new Vector3(x, 0, z); } }
    }

    internal sealed class TargetSnapshot
    {
        public string id, name, region, description, unavailable;
        public bool visited, hasDlc, eligible, dockActive, nativeDestination;
        public float clearance;
        public LandingGeometryDiagnostics geometry;
        public List<LandingCandidate> candidates = new List<LandingCandidate>();
        public LandingCandidate selected;
        internal Destination Definition;
    }

    internal sealed class LandingGeometryDiagnostics
    {
        public int hullColliderCount, solidColliders, hazardColliders, unknownTriggers, unboundedColliders;
        public LandingQueryDiagnostics query;
        public List<string> hullLayers = new List<string>();
        public List<string> unboundedLabels = new List<string>();
        public Dictionary<string, int> excluded = new Dictionary<string, int>();
    }

    internal sealed class LandingPlanner
    {
        private sealed class Obstacle
        {
            internal Collider Collider;
            internal Bounds Bounds;
            internal string Label;
            internal LandingColliderRole Role;
        }

        internal List<TargetSnapshot> Inspect(Player player)
        {
            GameManager game = GameManager.Instance;
            bool unlocked = game.SaveData.unlockedAbilities != null && game.SaveData.unlockedAbilities.Contains("manifest");
            DockPOI[] docks = Object.FindObjectsOfType<DockPOI>(true);
            var sourceScenes = new HashSet<int> { player.gameObject.scene.handle };
            foreach (DockPOI dock in docks)
                if (dock != null && dock.dock != null && dock.dock.Data != null && Destination.Find(dock.dock.Data.Id) != null)
                    sourceScenes.Add(dock.gameObject.scene.handle);
            var geometry = new LandingGeometryDiagnostics();
            List<Collider> hull = CollectHull(player, geometry);
            List<Obstacle> obstacles = CollectObstacles(player, hull, sourceScenes, geometry);
            float clearance = HullClearance(player, hull);
            var results = new List<TargetSnapshot>();
            using (var query = new LandingQueryWorld(clearance))
            {
                geometry.query = query.Diagnostics;
                foreach (Destination target in Destination.All)
                {
                    bool dlc = target.RequiredDlc == 0 || (game.EntitlementManager != null && game.EntitlementManager.GetHasEntitlement(
                        target.RequiredDlc == 1 ? Entitlement.DLC_1 : Entitlement.DLC_2));
                    bool visited = game.SaveData.GetBoolVariable("has-visited-dock-" + target.Id);
                    var snapshot = new TargetSnapshot { Definition = target, id = target.Id, name = target.Name, region = target.Region,
                        description = target.Description, visited = visited, hasDlc = !target.RequiresDlc || dlc,
                        clearance = clearance, geometry = geometry };
                    snapshot.unavailable = TravelRules.Eligibility(unlocked, visited, target.RequiresDlc, dlc);
                    if (!dlc && target.RequiredDlc == 2) snapshot.unavailable = "需要「铁钻」DLC";
                    snapshot.eligible = snapshot.unavailable == null;
                    DockPOI dock = null;
                    foreach (DockPOI candidate in docks)
                        if (candidate != null && candidate.gameObject.scene.IsValid() && candidate.dock != null && candidate.dock.Data != null && candidate.dock.Data.Id == target.Id)
                        { if (dock != null) { dock = null; break; } dock = candidate; }
                    if (dock == null || dock.dockSlots == null || dock.dockSlots.Length == 0)
                    { snapshot.unavailable = snapshot.unavailable ?? "商人场景尚未加载"; results.Add(snapshot); continue; }
                    snapshot.dockActive = dock.gameObject.activeInHierarchy;
                    for (int slotIndex = 0; slotIndex < dock.dockSlots.Length; slotIndex++)
                    {
                        Transform slot = dock.dockSlots[slotIndex];
                        if (slot == null) continue;
                        Vector3 outward = slot.position - dock.transform.position;
                        outward.y = 0;
                        if (outward.sqrMagnitude < .25f) outward = -slot.forward;
                        outward.Normalize();
                        foreach (float distance in new[] { 12f, 18f, 26f, 36f })
                        foreach (float angle in new[] { 0f, -35f, 35f })
                        {
                            Vector3 direction = Quaternion.Euler(0, angle, 0) * outward;
                            Vector3 point = slot.position + direction * distance;
                            point.y = 0;
                            var candidate = new LandingCandidate { id = "slot" + slotIndex + "/" + distance + "/" + angle,
                                x = point.x, z = point.z, heading = Quaternion.LookRotation(-direction, Vector3.up).eulerAngles.y };
                            Check(candidate, clearance, obstacles, query, geometry);
                            snapshot.candidates.Add(candidate);
                            if (snapshot.selected == null && candidate.safe) snapshot.selected = candidate;
                        }
                    }
                    if (snapshot.selected == null) snapshot.unavailable = snapshot.unavailable ?? "附近暂时没有足够安全的水面";
                    results.Add(snapshot);
                }
            }
            return results;
        }

        private static List<Collider> CollectHull(Player player, LandingGeometryDiagnostics diagnostics)
        {
            var hull = new List<Collider>();
            Rigidbody body = NativeState.Rigidbody(player);
            foreach (Collider collider in player.GetComponentsInChildren<Collider>(true))
            {
                if (collider == null || !collider.enabled || collider.isTrigger || !collider.gameObject.activeInHierarchy ||
                    body == null || collider.attachedRigidbody != body) continue;
                hull.Add(collider);
                string layer = collider.gameObject.layer + ":" + LayerMask.LayerToName(collider.gameObject.layer);
                if (!diagnostics.hullLayers.Contains(layer)) diagnostics.hullLayers.Add(layer);
            }
            diagnostics.hullColliderCount = hull.Count;
            return hull;
        }

        private static float HullClearance(Player player, List<Collider> hull)
        {
            float radius = 4f;
            foreach (Collider collider in hull)
            {
                Bounds bounds;
                if (!TryBounds(collider, out bounds)) continue;
                Vector3 offset = bounds.center - player.transform.position;
                radius = Mathf.Max(radius, Mathf.Max(Mathf.Abs(offset.x) + bounds.extents.x, Mathf.Abs(offset.z) + bounds.extents.z) + 2f);
            }
            return radius;
        }

        private static List<Obstacle> CollectObstacles(Player player, List<Collider> hull, HashSet<int> sourceScenes,
            LandingGeometryDiagnostics diagnostics)
        {
            var results = new List<Obstacle>();
            foreach (Collider collider in Object.FindObjectsOfType<Collider>(true))
            {
                if (collider == null || !collider.gameObject.scene.IsValid() || collider.transform.IsChildOf(player.transform)) continue;
                if (LandingQueryWorld.Owns(collider.gameObject.scene) || !sourceScenes.Contains(collider.gameObject.scene.handle))
                { Increment(diagnostics.excluded, "OtherScene"); continue; }
                bool collidesWithHull = false;
                foreach (Collider hullPart in hull)
                    if (!Physics.GetIgnoreLayerCollision(hullPart.gameObject.layer, collider.gameObject.layer))
                    { collidesWithHull = true; break; }
                LandingColliderRole role = LandingRules.Classify(collider.enabled, IsAutomaticHazard(collider), collider.isTrigger,
                    IsOrdinarySensor(collider), collidesWithHull);
                if (!LandingRules.BlocksLanding(role))
                { Increment(diagnostics.excluded, role.ToString() + ":" + LayerMask.LayerToName(collider.gameObject.layer)); continue; }
                string label = collider.gameObject.name + ":" + collider.GetType().Name + ":" + LayerMask.LayerToName(collider.gameObject.layer) +
                    (collider.gameObject.activeInHierarchy ? ":active" : ":inactive") + (collider.isTrigger ? ":trigger" : "");
                // CullingBrain disables distant GameObjects, not their enabled flag. Read their shape without activating them.
                Bounds bounds;
                if (!TryBounds(collider, out bounds))
                {
                    diagnostics.unboundedColliders++;
                    if (diagnostics.unboundedLabels.Count < 8) diagnostics.unboundedLabels.Add(label);
                    continue;
                }
                if (role == LandingColliderRole.Solid) diagnostics.solidColliders++;
                else if (role == LandingColliderRole.AutomaticHazard) diagnostics.hazardColliders++;
                else diagnostics.unknownTriggers++;
                results.Add(new Obstacle { Collider = collider, Bounds = bounds, Label = label, Role = role });
            }
            return results;
        }

        private static void Check(LandingCandidate candidate, float radius, List<Obstacle> obstacles, LandingQueryWorld query,
            LandingGeometryDiagnostics diagnostics)
        {
            Vector3 point = candidate.Position;
            candidate.zone = PlayerZoneDetector.GetZoneForPoint(point).ToString();
            candidate.waterDepthSamples = new float[LandingRules.WaterSampleCount];
            candidate.footprintMinDepth = 1f;
            int sampleIndex = 0;
            for (int x = -1; x <= 1; x++)
            for (int z = -1; z <= 1; z++)
            {
                float depth = GameManager.Instance.WaveController.SampleWaterDepthAtPosition(point + new Vector3(x * radius, 0, z * radius));
                candidate.waterDepthSamples[sampleIndex++] = depth;
                candidate.footprintMinDepth = Mathf.Min(candidate.footprintMinDepth, depth);
                if (x == 0 && z == 0) candidate.depth = depth;
            }
            // The mask is normalized, not metres. Footprint samples also guard against mesh back-face misses inside land.
            if (!LandingRules.HasWaterFootprint(candidate.waterDepthSamples)) candidate.reasons.Add("water-depth-footprint:" + candidate.footprintMinDepth);
            if (diagnostics.hullColliderCount == 0) candidate.reasons.Add("hull-collider-unavailable");
            if (diagnostics.unboundedColliders > 0) candidate.reasons.Add("collision-geometry-unavailable");
            if (!query.Ready)
            { candidate.reasons.Add("geometry-self-test-failed:" + query.Diagnostics.error); candidate.safe = false; return; }
            var envelope = new Bounds(point, new Vector3(radius * 2f, 5f, radius * 2f));
            foreach (Obstacle obstacle in obstacles)
            {
                if (!envelope.Intersects(obstacle.Bounds)) continue;
                candidate.broadphaseMatches++;
                try
                {
                    candidate.narrowphaseTests++;
                    if (query.Overlaps(obstacle.Collider, point))
                    {
                        if (candidate.reasons.Count < 16) candidate.reasons.Add("narrowphase:" + obstacle.Role + ":" + obstacle.Label);
                    }
                    else candidate.narrowphaseClear++;
                }
                catch (Exception exception)
                {
                    if (candidate.reasons.Count < 16) candidate.reasons.Add("narrowphase-error:" + obstacle.Label + ":" + exception.GetType().Name);
                }
            }
            candidate.safe = candidate.reasons.Count == 0;
        }

        private static void Increment(Dictionary<string, int> counts, string key)
        {
            int current;
            counts.TryGetValue(key, out current);
            counts[key] = current + 1;
        }

        private static bool IsAutomaticHazard(Collider collider)
        {
            return collider.GetComponentInParent<ConversationTrigger>(true) != null ||
                collider.GetComponentInParent<GCMonsterSpawnTrigger>(true) != null ||
                collider.GetComponentInParent<TSMonsterTriggerBox>(true) != null ||
                collider.GetComponentInParent<PlayerDetector>(true) != null ||
                collider.GetComponentInParent<OozePatch>(true) != null ||
                collider.gameObject.layer == LayerMask.NameToLayer("Ooze");
        }

        private static bool IsOrdinarySensor(Collider collider)
        {
            // These keep their ordinary native effects after arrival; no callback or scene state is changed here.
            return collider.GetComponent<ZoneCollider>() != null || collider.GetComponentInParent<HarvestPOI>(true) != null ||
                collider.GetComponentInParent<DockPOI>(true) != null || collider.GetComponentInParent<HarvestZone>(true) != null ||
                collider.GetComponentInParent<WeatherTrigger>(true) != null || collider.GetComponentInParent<SanityModifier>(true) != null;
        }

        internal static bool TryBounds(Collider collider, out Bounds world)
        {
            Bounds local;
            var box = collider as BoxCollider;
            var sphere = collider as SphereCollider;
            var capsule = collider as CapsuleCollider;
            var mesh = collider as MeshCollider;
            if (sphere != null || capsule != null)
            {
                // Unity spheres keep a circular cross-section under non-uniform scale. Scaling a local box per axis
                // can underestimate their radius. A maximum-scale enclosing sphere is conservative for both shapes.
                Vector3 scale = collider.transform.lossyScale;
                float maximumScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                float localRadius = sphere != null ? sphere.radius : Mathf.Max(capsule.radius, capsule.height * .5f);
                float radius = localRadius * maximumScale;
                Vector3 center = collider.transform.TransformPoint(sphere != null ? sphere.center : capsule.center);
                world = new Bounds(center, Vector3.one * radius * 2f);
                return radius > 0 && TravelRules.Finite(radius) && TravelRules.Finite(center.x) &&
                    TravelRules.Finite(center.y) && TravelRules.Finite(center.z);
            }
            if (box != null) local = new Bounds(box.center, box.size);
            else if (mesh != null && mesh.sharedMesh != null) local = mesh.sharedMesh.bounds;
            else { world = collider.bounds; return world.size.sqrMagnitude > 0; }
            Matrix4x4 matrix = collider.transform.localToWorldMatrix;
            Vector3 e = local.extents;
            Vector3 a = matrix.MultiplyVector(new Vector3(e.x, 0, 0));
            Vector3 b = matrix.MultiplyVector(new Vector3(0, e.y, 0));
            Vector3 c = matrix.MultiplyVector(new Vector3(0, 0, e.z));
            world = new Bounds(matrix.MultiplyPoint3x4(local.center), new Vector3(
                Mathf.Abs(a.x) + Mathf.Abs(b.x) + Mathf.Abs(c.x), Mathf.Abs(a.y) + Mathf.Abs(b.y) + Mathf.Abs(c.y),
                Mathf.Abs(a.z) + Mathf.Abs(b.z) + Mathf.Abs(c.z)) * 2);
            return true;
        }
    }
}
