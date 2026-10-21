using System;
using UnityEngine;

namespace Jinke.DredgeShipAppearance
{
    internal static class Boat5MeshFactory
    {
        internal static Mesh[] Create(Mesh nativeSturdy)
        {
            if (nativeSturdy == null || nativeSturdy.subMeshCount != 2)
                throw new InvalidOperationException("原版五级船网格不可用。");
            // The shipped mesh is not readable. Bounds are sufficient to check
            // this build's geometry contract; never request its CPU vertices.
            Bounds bounds = nativeSturdy.bounds;
            if (!Near(bounds.center.x, 0, .02f) || !Near(bounds.center.y, 1.2232823f, .03f) ||
                !Near(bounds.center.z, .2452915f, .03f) || !Near(bounds.extents.x, .7112064f, .03f) ||
                !Near(bounds.extents.y, 1.6060355f, .03f) || !Near(bounds.extents.z, 1.7017434f, .03f))
                throw new InvalidOperationException("原版五级船尺寸不匹配，保留原版外观。");

            var meshes = new Mesh[4];
            string[] states = { "Sturdy", "Ok", "Poor", "Critical" };
            try
            {
                for (int state = 0; state < meshes.Length; state++)
                {
                    Boat5Geometry geometry = Boat5Geometry.Build(state);
                    var vertices = new Vector3[geometry.Vertices.Count];
                    var uv = new Vector2[vertices.Length];
                    var colors = new Color32[vertices.Length];
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        Boat5Geometry.Point p = geometry.Vertices[i];
                        Boat5Geometry.UV t = geometry.TexCoords[i];
                        vertices[i] = new Vector3(p.X, p.Y, p.Z);
                        uv[i] = new Vector2(t.U, t.V);
                        colors[i] = new Color32(255, 255, 255, 255);
                    }
                    Mesh mesh = new Mesh();
                    meshes[state] = mesh;
                    mesh.name = "JinkeBoat5_Faceted_" + states[state];
                    mesh.hideFlags = HideFlags.DontSave;
                    mesh.vertices = vertices;
                    mesh.uv = uv;
                    mesh.colors32 = colors;
                    mesh.subMeshCount = 2;
                    mesh.SetTriangles(geometry.Hull, 0);
                    mesh.SetTriangles(geometry.Windows, 1);
                    mesh.RecalculateNormals();
                    mesh.RecalculateBounds();
                    if (mesh.subMeshCount != 2 || mesh.vertexCount != vertices.Length)
                        throw new InvalidOperationException("程序船型创建失败。");
                }
                return meshes;
            }
            catch
            {
                Dispose(meshes);
                throw;
            }
        }

        internal static void Dispose(Mesh[] meshes)
        {
            if (meshes == null) return;
            foreach (Mesh mesh in meshes) if (mesh != null) UnityEngine.Object.Destroy(mesh);
        }

        private static bool Near(float value, float expected, float tolerance)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && Math.Abs(value - expected) <= tolerance;
        }
    }
}
