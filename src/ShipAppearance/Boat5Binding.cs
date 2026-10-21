using System;
using System.Collections.Generic;
using UnityEngine;

namespace Jinke.DredgeShipAppearance
{
    // Owns Boat5 visual mesh references and two slot-1 texture overrides only.
    // No material object, Transform, collider, attachment or event is changed.
    internal sealed class Boat5Binding
    {
        private readonly BoatModelProxy proxy;
        private readonly MeshFilter filter;
        private readonly List<Mesh> meshList;
        private readonly List<Mesh> original;
        private readonly Dictionary<string, Boat5SkinResources> resources = new Dictionary<string, Boat5SkinResources>();
        private readonly Boat5WindowOverrides windows;
        private bool dirty;
        internal string ActiveId { get; private set; }
        internal bool Applied { get { return ActiveId != ShipAppearance.Original; } }

        internal Boat5Binding(BoatModelProxy model)
        {
            if (model == null || model.BoatMeshRenderer == null) throw new InvalidOperationException("Boat5 renderer is unavailable.");
            proxy = model;
            filter = model.BoatMeshRenderer.GetComponent<MeshFilter>();
            meshList = model.DamageStateMeshes;
            if (filter == null || meshList == null || meshList.Count != 4)
                throw new InvalidOperationException("Boat5 must have four native damage meshes.");
            foreach (Mesh mesh in meshList)
                if (mesh == null || mesh.subMeshCount != 2) throw new InvalidOperationException("Boat5 native mesh layout differs.");
            Material[] materials = model.BoatMeshRenderer.sharedMaterials;
            if (materials.Length != 2 || materials[0] == null || materials[1] == null ||
                !materials[0].HasProperty("_Hull_Color") || !materials[0].HasProperty("_Roof_Color") || !materials[1].HasProperty("_LightStrength"))
                throw new InvalidOperationException("Boat5 paint/window material slots differ.");
            // A real copy: Clear/AddRange below must never erase the backup.
            original = new List<Mesh>(meshList);
            windows = new Boat5WindowOverrides(model.BoatMeshRenderer);
            ActiveId = ShipAppearance.Original;
            Boat5SkinResources faceted = new Boat5SkinResources(Boat5MeshFactory.Create(original[0]));
            try
            {
                Validate(faceted);
                resources.Add(ShipAppearance.Faceted, faceted);
            }
            catch { faceted.Dispose(); throw; }
        }

        internal bool Matches(BoatModelProxy model) { return proxy != null && proxy == model; }

        internal void Apply(int damage, int threshold)
        { Apply(ShipAppearance.Faceted, damage, threshold); }

        internal void Apply(string id, int damage, int threshold)
        {
            RequireLive();
            Boat5SkinResources next;
            if (!resources.TryGetValue(id, out next))
            {
                next = Boat5AuthoredAssets.Create(id);
                try { Validate(next); resources.Add(id, next); }
                catch { if (next != null) next.Dispose(); throw; }
            }
            string previous = ActiveId;
            var previousMeshes = new List<Mesh>(meshList);
            try
            {
                dirty = true;
                SetTextures(next);
                meshList.Clear(); meshList.AddRange(next.Meshes);
                filter.sharedMesh = next.Meshes[DamageIndex(damage, threshold)];
                ActiveId = id;
                dirty = false;
            }
            catch
            {
                try
                {
                    meshList.Clear(); meshList.AddRange(previousMeshes);
                    filter.sharedMesh = previousMeshes[DamageIndex(damage, threshold)];
                }
                finally { SetTextures(previous == ShipAppearance.Original ? null : resources[previous]); }
                ActiveId = previous; dirty = false;
                throw;
            }
        }

        internal void Restore(int damage, int threshold)
        {
            if (!Applied && !dirty) return;
            if (proxy == null || filter == null)
            {
                windows.Restore();
                ActiveId = ShipAppearance.Original;
                dirty = false;
                return;
            }
            RequireLive();
            // Detach texture references before their owning resources can be
            // destroyed. If it fails Release retains everything for retry.
            windows.Restore();
            meshList.Clear(); meshList.AddRange(original);
            filter.sharedMesh = original[DamageIndex(damage, threshold)];
            ActiveId = ShipAppearance.Original;
            dirty = false;
        }

        internal void Release(int damage, int threshold)
        {
            // If restoring a still-live object fails, do not destroy meshes it
            // could still reference. The caller retains this binding for retry.
            Restore(damage, threshold);
            foreach (Boat5SkinResources value in resources.Values) value.Dispose();
            resources.Clear();
        }

        private void RequireLive()
        {
            if (proxy == null || filter == null || !ReferenceEquals(proxy.DamageStateMeshes, meshList))
                throw new InvalidOperationException("Boat5 visual binding changed.");
        }

        private void SetTextures(Boat5SkinResources value)
        {
            if (value == null || value.Albedo == null) windows.Restore();
            else windows.Apply(value.Albedo, value.Emission);
        }

        private static void Validate(Boat5SkinResources value)
        {
            if (value == null || value.Meshes == null || value.Meshes.Length != 4)
                throw new InvalidOperationException("Four generated damage meshes are required.");
            foreach (Mesh mesh in value.Meshes)
                if (mesh == null || mesh.subMeshCount != 2) throw new InvalidOperationException("Generated material slots differ.");
            if ((value.Albedo == null) != (value.Emission == null))
                throw new InvalidOperationException("Boat5 appearance atlas pair is incomplete.");
        }

        // Mirrors BoatSubModelToggler.OnPlayerDamageChanged without broadcasting
        // its global event (which also invokes the player's death checks).
        internal static int DamageIndex(int damage, int threshold)
        {
            if (damage == 0) return 0;
            if (damage == threshold) return 3;
            return Mathf.Clamp(Mathf.CeilToInt(damage / 2f), 0, 3);
        }
    }
}
