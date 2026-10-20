using System;
using UnityEngine;

namespace Jinke.DredgeSkins
{
    internal static class ShaderFields
    {
        internal static readonly int Hull = Shader.PropertyToID("_Hull_Color");
        internal static readonly int Roof = Shader.PropertyToID("_Roof_Color");
        internal static readonly int Flag = Shader.PropertyToID("Texture2D_9aa7ba2263944b48bbf43c218dc48459");
    }

    /// <summary>Owns only the relevant properties on material slot 0; never clears or restores a whole block.</summary>
    internal sealed class RendererOverrides
    {
        internal readonly Renderer Renderer;
        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        private bool hasHull;
        private bool hasRoof;
        private bool hasFlag;
        private Color baselineHull;
        private Color baselineRoof;
        private Texture baselineFlag;
        private Color lastHull;
        private Color lastRoof;
        private Texture lastFlag;
        private bool colorsWritten;
        private bool flagWritten;

        internal RendererOverrides(Renderer renderer)
        {
            Renderer = renderer;
            ReadBlock();
            hasHull = block.HasColor(ShaderFields.Hull);
            hasRoof = block.HasColor(ShaderFields.Roof);
            hasFlag = block.HasTexture(ShaderFields.Flag);
            if (hasHull) baselineHull = block.GetColor(ShaderFields.Hull);
            if (hasRoof) baselineRoof = block.GetColor(ShaderFields.Roof);
            if (hasFlag) baselineFlag = block.GetTexture(ShaderFields.Flag);
        }

        private void ReadBlock() { Renderer.GetPropertyBlock(block, 0); }
        private void WriteBlock() { Renderer.SetPropertyBlock(block, 0); }

        private Material Material
        {
            get
            {
                if (Renderer == null) return null;
                Material[] materials = Renderer.sharedMaterials;
                return materials.Length > 0 ? materials[0] : null;
            }
        }

        internal void ApplyColors(LoadedSkin skin)
        {
            ApplyPalette(skin, skin);
        }

        internal void ApplyPalette(LoadedSkin hull, LoadedSkin roof)
        {
            Material material = Material;
            if (material == null || !material.HasProperty(ShaderFields.Hull) || !material.HasProperty(ShaderFields.Roof)) return;
            if (hull == null && roof == null && !colorsWritten) return;
            ReadBlock();
            // A later independent writer's values become the fallback rather than being discarded.
            if (colorsWritten && block.HasColor(ShaderFields.Hull) && block.GetColor(ShaderFields.Hull) != lastHull)
            { hasHull = true; baselineHull = block.GetColor(ShaderFields.Hull); }
            if (colorsWritten && block.HasColor(ShaderFields.Roof) && block.GetColor(ShaderFields.Roof) != lastRoof)
            { hasRoof = true; baselineRoof = block.GetColor(ShaderFields.Roof); }
            lastHull = hull != null ? hull.Hull : hasHull ? baselineHull : material.GetColor(ShaderFields.Hull);
            lastRoof = roof != null ? roof.Roof : hasRoof ? baselineRoof : material.GetColor(ShaderFields.Roof);
            block.SetColor(ShaderFields.Hull, lastHull);
            block.SetColor(ShaderFields.Roof, lastRoof);
            WriteBlock();
            colorsWritten = true;
        }

        internal void ApplyFlag(LoadedSkin skin, SkinAssets assets)
        {
            Material material = Material;
            if (material == null || !material.HasProperty(ShaderFields.Flag)) return;
            if (skin == null && !flagWritten) return;
            ReadBlock();
            if (flagWritten && block.HasTexture(ShaderFields.Flag) && block.GetTexture(ShaderFields.Flag) != lastFlag)
            { hasFlag = true; baselineFlag = block.GetTexture(ShaderFields.Flag); }
            Texture native = hasFlag && baselineFlag != null ? baselineFlag : material.GetTexture(ShaderFields.Flag);
            if (native == null) return;
            lastFlag = skin == null ? native : assets.GetAtlas(native, skin);
            block.SetTexture(ShaderFields.Flag, lastFlag);
            WriteBlock();
            flagWritten = true;
        }

        internal void Restore()
        {
            if (Renderer == null) return;
            ApplyColors(null);
            ApplyFlag(null, null);
        }
    }
}
