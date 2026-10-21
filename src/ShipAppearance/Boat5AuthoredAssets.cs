using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Jinke.DredgeShipAppearance
{
    // Controlled Blender export, not an arbitrary model loader. The complete
    // embedded payload is SHA-256 locked before any Unity object is allocated.
    internal static class Boat5AuthoredAssets
    {
        internal const string CthulhuDigest = "02e970fd262ff6541e7460c85bf13f3d3c0d6919a7f7bc9e925c91d20b65a3db";
        internal const string CyberpunkDigest = "a6f46ce42e4cad9a74a5a152a4131eb177596e60e9591aea7684ef06149f8eef";
        private const int MaxBytes = 16 * 1024 * 1024;

        internal static Boat5SkinResources Create(string id)
        {
            string name, digest;
            if (id == ShipAppearance.Cthulhu) { name = "cthulhu-v1"; digest = CthulhuDigest; }
            else if (id == ShipAppearance.Cyberpunk) { name = "cyberpunk-v1"; digest = CyberpunkDigest; }
            else throw new InvalidOperationException("Unknown authored Boat5 appearance.");
            byte[] bytes;
            using (Stream source = typeof(Boat5AuthoredAssets).Assembly.GetManifestResourceStream("Jinke.ShipAppearance." + name + ".bin"))
            {
                if (source == null || source.Length <= 0 || source.Length > MaxBytes)
                    throw new InvalidOperationException("Embedded Boat5 appearance is unavailable.");
                bytes = new byte[(int)source.Length];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int count = source.Read(bytes, offset, bytes.Length - offset);
                    if (count == 0) throw new InvalidOperationException("Embedded Boat5 appearance was truncated.");
                    offset += count;
                }
            }
            return Read(bytes, digest, name);
        }

        internal static Boat5SkinResources Read(byte[] bytes, string expectedDigest, string label)
        {
            if (bytes == null || bytes.Length > MaxBytes) throw new InvalidOperationException("Invalid Boat5 resource size.");
            using (SHA256 sha = SHA256.Create())
            {
                string actual = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                if (actual != expectedDigest) throw new InvalidOperationException("Boat5 appearance digest differs.");
            }
            var meshes = new Mesh[4];
            Texture2D albedo = null, emission = null;
            try
            {
                using (var input = new BinaryReader(new MemoryStream(bytes, false), Encoding.UTF8))
                {
                    if (Encoding.ASCII.GetString(Exact(input, 8)) != "JBSKIN01") throw new InvalidOperationException("Boat5 resource version differs.");
                    int width = input.ReadInt32(), height = input.ReadInt32();
                    if (width != 64 || height != 64) throw new InvalidOperationException("Boat5 atlas dimensions differ.");
                    byte[] basePixels = Exact(input, width * height * 4), lightPixels = Exact(input, width * height * 4);
                    if (input.ReadInt32() != 4) throw new InvalidOperationException("Boat5 requires four authored states.");
                    // Validate every state before allocating/uploading textures.
                    for (int state = 0; state < 4; state++) meshes[state] = ReadMesh(input, label + "_" + state);
                    if (input.BaseStream.Position != input.BaseStream.Length) throw new InvalidOperationException("Unexpected Boat5 resource suffix.");
                    albedo = Texture(basePixels, width, height, label + "_Albedo");
                    emission = Texture(lightPixels, width, height, label + "_Emission");
                }
                return new Boat5SkinResources(meshes, albedo, emission);
            }
            catch
            {
                Boat5MeshFactory.Dispose(meshes);
                if (albedo != null) UnityEngine.Object.Destroy(albedo);
                if (emission != null) UnityEngine.Object.Destroy(emission);
                throw;
            }
        }

        private static Mesh ReadMesh(BinaryReader input, string label)
        {
            int count = input.ReadInt32();
            if (count < 3 || count > 65532) throw new InvalidOperationException("Boat5 vertex budget differs.");
            var vertices = new Vector3[count]; var normals = new Vector3[count]; var uv = new Vector2[count];
            var colors = new Color32[count];
            for (int i = 0; i < count; i++)
            {
                float x = Number(input), y = Number(input), z = Number(input);
                if (Math.Abs(x) > 10 || Math.Abs(y) > 10 || Math.Abs(z) > 10) throw new InvalidOperationException("Boat5 geometry is out of bounds.");
                vertices[i] = new Vector3(x, y, z);
                float nx = Number(input), ny = Number(input), nz = Number(input);
                float length = nx * nx + ny * ny + nz * nz;
                if (length < .98f || length > 1.02f) throw new InvalidOperationException("Boat5 authored normal is invalid.");
                normals[i] = new Vector3(nx, ny, nz);
                float u = Number(input), v = Number(input);
                if (u < 0 || u > 1 || v < 0 || v > 1) throw new InvalidOperationException("Boat5 authored UV is invalid.");
                uv[i] = new Vector2(u, v);
                colors[i] = new Color32(255, 255, 255, 255);
            }
            List<int> hull = Indices(input, vertices), detail = Indices(input, vertices);
            Mesh mesh = null;
            try
            {
                mesh = new Mesh { name = "JinkeBoat5_" + label, hideFlags = HideFlags.DontSave };
                mesh.vertices = vertices; mesh.normals = normals; mesh.uv = uv; mesh.colors32 = colors;
                mesh.subMeshCount = 2; mesh.SetTriangles(hull, 0); mesh.SetTriangles(detail, 1);
                mesh.RecalculateBounds();
                return mesh;
            }
            catch { if (mesh != null) UnityEngine.Object.Destroy(mesh); throw; }
        }

        private static List<int> Indices(BinaryReader input, Vector3[] vertices)
        {
            int count = input.ReadInt32();
            if (count < 3 || count > 300000 || count % 3 != 0) throw new InvalidOperationException("Boat5 submesh indices are invalid.");
            var indices = new List<int>(count);
            for (int i = 0; i < count; i++)
            {
                int index = input.ReadInt32();
                if (index < 0 || index >= vertices.Length) throw new InvalidOperationException("Boat5 submesh index is out of range.");
                indices.Add(index);
            }
            for (int i = 0; i < count; i += 3)
            {
                Vector3 a = vertices[indices[i]], b = vertices[indices[i + 1]], c = vertices[indices[i + 2]];
                double ax = b.x - a.x, ay = b.y - a.y, az = b.z - a.z;
                double bx = c.x - a.x, by = c.y - a.y, bz = c.z - a.z;
                double nx = ay * bz - az * by, ny = az * bx - ax * bz, nz = ax * by - ay * bx;
                if (nx * nx + ny * ny + nz * nz < 1e-20) throw new InvalidOperationException("Boat5 has a degenerate authored triangle.");
            }
            return indices;
        }

        private static float Number(BinaryReader input)
        {
            float value = input.ReadSingle();
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidOperationException("Nonfinite Boat5 resource number.");
            return value;
        }
        private static byte[] Exact(BinaryReader input, int count)
        {
            byte[] bytes = input.ReadBytes(count);
            if (bytes.Length != count) throw new InvalidOperationException("Truncated Boat5 resource.");
            return bytes;
        }
        private static Texture2D Texture(byte[] rgba, int width, int height, string label)
        {
            Texture2D texture = null;
            try
            {
                texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
                texture.name = "JinkeBoat5_" + label; texture.hideFlags = HideFlags.DontSave;
                texture.filterMode = FilterMode.Point; texture.wrapMode = TextureWrapMode.Clamp;
                texture.LoadRawTextureData(rgba); texture.Apply(false, true);
                return texture;
            }
            catch { if (texture != null) UnityEngine.Object.Destroy(texture); throw; }
        }
    }
}
