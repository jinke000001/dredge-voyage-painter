using UnityEngine;

namespace Jinke.DredgeShipAppearance
{
    // Every object here is authored by this extension. Native meshes/materials
    // never enter this owner and must be detached before Dispose is called.
    internal sealed class Boat5SkinResources
    {
        internal readonly Mesh[] Meshes;
        internal readonly Texture2D Albedo;
        internal readonly Texture2D Emission;

        internal Boat5SkinResources(Mesh[] meshes, Texture2D albedo = null, Texture2D emission = null)
        { Meshes = meshes; Albedo = albedo; Emission = emission; }

        internal void Dispose()
        {
            Boat5MeshFactory.Dispose(Meshes);
            if (Albedo != null) Object.Destroy(Albedo);
            if (Emission != null) Object.Destroy(Emission);
        }
    }
}
