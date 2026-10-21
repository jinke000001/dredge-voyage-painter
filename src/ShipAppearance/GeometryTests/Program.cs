using System;
using System.Linq;
using System.IO;
using System.Security.Cryptography;
using Jinke.DredgeShipAppearance;
using UnityEngine;

internal static class Program
{
    private static int checks;
    private static void Check(bool condition, string description)
    {
        checks++;
        if (!condition) throw new Exception(description);
    }
    private static void Throws(Action action, string description)
    {
        bool threw = false;
        try { action(); } catch (InvalidOperationException) { threw = true; }
        Check(threw, description);
    }

    private static int Main()
    {
        var states = Enumerable.Range(0, 4).Select(Boat5Geometry.Build).ToArray();
        foreach (Boat5Geometry geometry in states)
        {
            geometry.Validate();
            Check(geometry.Vertices.Count < 1200, "Low polygon budget");
            Check(geometry.Hull.Count > 0 && geometry.Windows.Count >= 36, "Both material slots contain triangles");
            Check(geometry.Vertices.All(p => Math.Abs(p.X) <= .712f && p.Y >= -.383f && p.Y <= 2.15f && p.Z >= -1.457f && p.Z <= 1.948f), "All points stay inside native Boat5 envelope");
            Check(geometry.Vertices.Any(p => p.Y < 0) && geometry.Vertices.Any(p => p.Y > 1.85f), "Waterline and wheelhouse preserved");
            Check(geometry.Vertices.Where(p => p.Z > 1.8f).All(p => Math.Abs(p.X) < .1f), "Bow tapers instead of ending in a box");
            Check(geometry.TexCoords.Any(uv => uv.V < .1f) && geometry.TexCoords.Any(uv => uv.V > .9f), "Native hull and roof mask regions used");
            CheckWindowNormals(geometry);
        }
        for (int state = 1; state < states.Length; state++)
        {
            Check(states[state].Vertices.Count == states[0].Vertices.Count, "Damage topology stable");
            Check(states[state].Vertices.Where((v, i) => v.X != states[state - 1].Vertices[i].X || v.Y != states[state - 1].Vertices[i].Y).Any(), "Each damage state has its own dent");
            Check(states[state].Vertices.Where((v, i) => v.Y > .8f).All(v => states[0].Vertices.Any(o => o.X == v.X && o.Y == v.Y && o.Z == v.Z)), "Upper attachments do not move with dents");
        }

        Boat5Geometry invalid = Boat5Geometry.Build(0);
        invalid.Hull[0] = invalid.Vertices.Count;
        Throws(invalid.Validate, "Reject invalid index");
        invalid = Boat5Geometry.Build(0);
        invalid.Hull[1] = invalid.Hull[0];
        Throws(invalid.Validate, "Reject zero-area triangle");
        invalid = Boat5Geometry.Build(0);
        invalid.Vertices[0] = new Boat5Geometry.Point(float.NaN, 0, 0);
        Throws(invalid.Validate, "Reject nonfinite vertex");
        invalid = Boat5Geometry.Build(0);
        invalid.Windows.Clear();
        Throws(invalid.Validate, "Reject missing window submesh");

        Mesh.Reset();
        Mesh native = Native();
        Mesh[] meshes = Boat5MeshFactory.Create(native);
        Check(meshes.Length == 4 && meshes.Distinct().Count() == 4, "Four independent mesh objects");
        Check(meshes.All(m => m.subMeshCount == 2 && m.Triangles[1].Count >= 36), "Factory uploads both submeshes");
        Check(meshes.All(m => m.uv.Length == m.vertexCount), "Factory uploads full UVs");
        Check(meshes.All(m => m.colors32.Length == m.vertexCount && m.colors32.All(c => c.r == 255 && c.g == 255 && c.b == 255 && c.a == 255)), "Explicit native white vertex colors");
        Check(meshes.All(m => !m.Destroyed) && !native.Destroyed && native.Unreadable, "Native mesh untouched and generated meshes retained");
        Boat5MeshFactory.Dispose(meshes);
        Check(meshes.All(m => m.Destroyed) && !native.Destroyed, "Dispose owns only new meshes");
        Boat5MeshFactory.Dispose(null);

        Mesh.Reset();
        native = Native();
        Mesh.ThrowOnTriangleCall = 5;
        Throws(() => Boat5MeshFactory.Create(native), "Upload failure propagates");
        Check(Mesh.Created.Skip(1).Count() == 3 && Mesh.Created.Skip(1).All(m => m.Destroyed), "Partial creation releases every generated mesh");
        Check(!native.Destroyed, "Failure never destroys native mesh");
        native.bounds.center.x = float.NaN;
        Throws(() => Boat5MeshFactory.Create(native), "Reject mismatched native bounds");
        native = Native(); native.subMeshCount = 1;
        Throws(() => Boat5MeshFactory.Create(native), "Reject missing native material submesh");

        CheckAuthored(ShipAppearance.Cthulhu, "cthulhu-v1", Boat5AuthoredAssets.CthulhuDigest);
        CheckAuthored(ShipAppearance.Cyberpunk, "cyberpunk-v1", Boat5AuthoredAssets.CyberpunkDigest);

        Console.WriteLine("Boat5 geometry: " + checks + " checks passed (offline doubles; no Unity/GPU execution).");
        Console.WriteLine("Per state: " + states[0].Vertices.Count + " vertices, " + states[0].Hull.Count / 3 + " hull triangles, " + states[0].Windows.Count / 3 + " window triangles.");
        return 0;
    }

    private static void CheckAuthored(string id, string resourceName, string digest)
    {
        Mesh.Reset(); Texture2D.Created.Clear();
        Boat5SkinResources skin = Boat5AuthoredAssets.Create(id);
        Check(skin.Meshes.Length == 4 && skin.Meshes.Distinct().Count() == 4, "Authored four independent states " + id);
        foreach (Mesh mesh in skin.Meshes)
        {
            Check(mesh.vertexCount > 3000 && mesh.vertexCount < 65533, "Authored vertex budget " + id);
            Check(mesh.subMeshCount == 2 && mesh.Triangles[0].Count > 0 && mesh.Triangles[1].Count > 0, "Authored two populated slots " + id);
            Check(mesh.normals.Length == mesh.vertexCount && mesh.uv.Length == mesh.vertexCount, "Authored normals and UVs uploaded " + id);
            Check(mesh.colors32.Length == mesh.vertexCount && mesh.colors32.All(c => c.r == 255 && c.g == 255 && c.b == 255 && c.a == 255),
                "Authored explicit white vertex colors " + id);
            Check(mesh.vertices.Min(v => v.y) < 0 && mesh.vertices.Max(v => v.y) > 1.5f, "Authored waterline and cabin " + id);
        }
        Check(skin.Meshes.Skip(1).All(mesh => mesh.vertices.Where((v, i) => i < skin.Meshes[0].vertexCount &&
            (v.x != skin.Meshes[0].vertices[i].x || v.y != skin.Meshes[0].vertices[i].y || v.z != skin.Meshes[0].vertices[i].z)).Any()),
            "Authored damage states have distinct geometry " + id);
        Check(skin.Albedo.Width == 64 && skin.Emission.Height == 64 && skin.Albedo.Pixels.Length == 16384 && skin.Emission.Pixels.Length == 16384,
            "Authored paired 64x64 RGBA atlas " + id);
        Check(!skin.Albedo.Mipmaps && !skin.Emission.Mipmaps && !skin.Albedo.Linear && !skin.Emission.Linear && skin.Albedo.Applied && skin.Emission.NotReadable,
            "Authored sRGB point atlas no mips and uploaded " + id);
        skin.Dispose();
        Check(skin.Meshes.All(m => m.Destroyed) && skin.Albedo.Destroyed && skin.Emission.Destroyed, "Authored resource owner cleanup " + id);

        byte[] bytes;
        using (Stream source = typeof(Boat5AuthoredAssets).Assembly.GetManifestResourceStream("Jinke.ShipAppearance." + resourceName + ".bin"))
        using (var copy = new MemoryStream()) { source.CopyTo(copy); bytes = copy.ToArray(); }
        byte[] tampered = (byte[])bytes.Clone(); tampered[tampered.Length - 1] ^= 1;
        int meshCountBeforeRejection = Mesh.Created.Count;
        Throws(() => Boat5AuthoredAssets.Read(tampered, digest, "tampered"), "Authored digest rejects single-byte modification " + id);
        Throws(() => Boat5AuthoredAssets.Read(bytes.Take(bytes.Length - 1).ToArray(), digest, "truncated"), "Authored digest rejects truncation " + id);
        Check(Mesh.Created.Count == meshCountBeforeRejection, "Authored integrity rejection allocates no Unity meshes " + id);
        Mesh.Reset(); Texture2D.Created.Clear(); Mesh.ThrowOnTriangleCall = 5;
        Throws(() => Boat5AuthoredAssets.Read(bytes, digest, "upload-failure"), "Authored upload failure propagates " + id);
        Check(Mesh.Created.Count == 3 && Mesh.Created.All(m => m.Destroyed) && Texture2D.Created.Count == 0,
            "Authored partial mesh upload failure cleans up before atlas allocation " + id);

        // Rehash intentionally malformed fixtures to exercise the parser after
        // the integrity gate as well as the stronger shipped-resource hashes.
        int header = 8 + 8 + 2 * 64 * 64 * 4 + 4;
        foreach (int offset in new[] { 0, 8, header, header + 4, header + 4 + 12, header + 4 + 24 })
        {
            byte[] malformed = (byte[])bytes.Clone();
            if (offset == 0) malformed[offset] = 0;
            else if (offset == 8 || offset == header) Array.Copy(BitConverter.GetBytes(-1), 0, malformed, offset, 4);
            else Array.Copy(BitConverter.GetBytes(float.NaN), 0, malformed, offset, 4);
            Throws(() => Boat5AuthoredAssets.Read(malformed, Hash(malformed), "malformed"), "Authored parser rejects malformed field " + offset + " " + id);
        }
        Mesh.ThrowOnTriangleCall = -1;
        int firstCount = BitConverter.ToInt32(bytes, header);
        int indices = header + 4 + firstCount * 32;
        foreach (int offset in new[] { header - 4, indices, indices + 4 })
        {
            byte[] malformed = (byte[])bytes.Clone();
            Array.Copy(BitConverter.GetBytes(-1), 0, malformed, offset, 4);
            Throws(() => Boat5AuthoredAssets.Read(malformed, Hash(malformed), "malformed-index"), "Authored parser rejects state/index field " + offset + " " + id);
        }
        byte[] degenerate = (byte[])bytes.Clone();
        Array.Copy(degenerate, indices + 4, degenerate, indices + 8, 4);
        Throws(() => Boat5AuthoredAssets.Read(degenerate, Hash(degenerate), "degenerate"), "Authored parser rejects zero-area triangle " + id);
        Mesh.Reset(); Texture2D.Created.Clear();
        byte[] suffix = bytes.Concat(new byte[] { 1 }).ToArray();
        Throws(() => Boat5AuthoredAssets.Read(suffix, Hash(suffix), "suffix"), "Authored parser rejects extra bytes " + id);
        Check(Mesh.Created.Count == 4 && Mesh.Created.All(m => m.Destroyed) && Texture2D.Created.Count == 0,
            "Authored suffix rejection disposes all validated meshes before texture upload " + id);
    }

    private static string Hash(byte[] bytes)
    { using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }

    private static Mesh Native()
    {
        return new Mesh { Unreadable = true, subMeshCount = 2,
            bounds = new Bounds(new Vector3(0, 1.2232823f, .2452915f), new Vector3(.7112064f, 1.6060355f, 1.7017434f)) };
    }

    private static void CheckWindowNormals(Boat5Geometry geometry)
    {
        for (int i = 0; i < geometry.Windows.Count; i += 3)
        {
            Boat5Geometry.Point a = geometry.Vertices[geometry.Windows[i]], b = geometry.Vertices[geometry.Windows[i + 1]], c = geometry.Vertices[geometry.Windows[i + 2]];
            float nx = (b.Y - a.Y) * (c.Z - a.Z) - (b.Z - a.Z) * (c.Y - a.Y);
            float nz = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
            float midX = (a.X + b.X + c.X) / 3, midZ = (a.Z + b.Z + c.Z) / 3;
            Check(midZ > 1.15f ? nz > 0 : nx * midX > 0, "Window faces point outside cabin");
        }
    }
}
