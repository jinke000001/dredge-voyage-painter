using System;
using UnityEngine;

namespace Jinke.DredgeShipAppearance
{
    // Owns two texture keys on material slot 1 only. In particular this block
    // never writes _LightStrength, and never reads/writes the painter's slot 0.
    internal sealed class Boat5WindowOverrides
    {
        internal const string AlbedoName = "Texture2D_9aa7ba2263944b48bbf43c218dc48459";
        internal const string EmissionName = "Texture2D_c7b8c5c57d6443a5a9f86b68269754f3";
        private static readonly int AlbedoKey = Shader.PropertyToID(AlbedoName);
        private static readonly int EmissionKey = Shader.PropertyToID(EmissionName);
        private readonly Renderer renderer;
        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        private Texture baselineAlbedo, baselineEmission;
        private Texture lastAlbedo, lastEmission;
        private bool baselineHasAlbedo, baselineHasEmission;
        private bool written;

        internal Boat5WindowOverrides(Renderer renderer) { this.renderer = renderer; }

        internal void Apply(Texture2D albedo, Texture2D emission)
        {
            if (albedo == null || emission == null) throw new InvalidOperationException("Boat5 appearance textures are unavailable.");
            Material material = Material;
            if (material == null || !material.HasProperty(AlbedoKey) || !material.HasProperty(EmissionKey))
                throw new InvalidOperationException("Boat5 window shader texture properties differ.");
            renderer.GetPropertyBlock(block, 1);
            // Preserve a later writer's replacement as the new restoration
            // baseline; never overwrite its other keys or clear its block.
            CaptureBaseline(AlbedoKey, lastAlbedo, ref baselineHasAlbedo, ref baselineAlbedo);
            CaptureBaseline(EmissionKey, lastEmission, ref baselineHasEmission, ref baselineEmission);
            block.SetTexture(AlbedoKey, albedo);
            block.SetTexture(EmissionKey, emission);
            renderer.SetPropertyBlock(block, 1);
            lastAlbedo = albedo; lastEmission = emission; written = true;
        }

        private void CaptureBaseline(int key, Texture last, ref bool has, ref Texture value)
        {
            bool present = block.HasTexture(key);
            Texture current = present ? block.GetTexture(key) : null;
            if (!written || current != last)
            { has = present; value = current; }
        }

        internal void Restore()
        {
            if (!written) return;
            if (renderer == null) { written = false; return; }
            Material material = Material;
            if (material == null) throw new InvalidOperationException("Boat5 window material disappeared during restoration.");
            renderer.GetPropertyBlock(block, 1);
            // Unity exposes no per-property removal. An originally absent key
            // is returned to the current material texture, preserving visual
            // state and every unrelated block entry (not byte identity).
            if (block.HasTexture(AlbedoKey) && block.GetTexture(AlbedoKey) == lastAlbedo)
                block.SetTexture(AlbedoKey, baselineHasAlbedo ? baselineAlbedo : material.GetTexture(AlbedoKey));
            if (block.HasTexture(EmissionKey) && block.GetTexture(EmissionKey) == lastEmission)
                block.SetTexture(EmissionKey, baselineHasEmission ? baselineEmission : material.GetTexture(EmissionKey));
            renderer.SetPropertyBlock(block, 1);
            written = false;
            lastAlbedo = lastEmission = null;
        }

        private Material Material
        {
            get
            {
                if (renderer == null) return null;
                Material[] slots = renderer.sharedMaterials;
                return slots != null && slots.Length == 2 ? slots[1] : null;
            }
        }
    }
}
