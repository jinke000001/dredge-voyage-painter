using System;
using System.Collections.Generic;

namespace UnityEngine
{
    internal class Object
    {
        internal bool Destroyed;
        internal static void Destroy(Object value) { value.Destroyed = true; }
    }
    internal enum HideFlags { DontSave }
    internal enum TextureFormat { RGBA32 }
    internal enum FilterMode { Point }
    internal enum TextureWrapMode { Clamp }
    internal sealed class Texture2D : Object
    {
        internal static readonly List<Texture2D> Created = new List<Texture2D>();
        internal string name;
        internal HideFlags hideFlags;
        internal FilterMode filterMode;
        internal TextureWrapMode wrapMode;
        internal readonly int Width, Height;
        internal readonly bool Mipmaps, Linear;
        internal byte[] Pixels;
        internal bool Applied, NotReadable;
        internal Texture2D(int width, int height, TextureFormat format, bool mipmaps, bool linear)
        { Width = width; Height = height; Mipmaps = mipmaps; Linear = linear; Created.Add(this); }
        internal void LoadRawTextureData(byte[] pixels) { Pixels = (byte[])pixels.Clone(); }
        internal void Apply(bool updateMipmaps, bool makeNoLongerReadable) { Applied = true; NotReadable = makeNoLongerReadable; }
    }
    internal struct Vector3
    {
        internal float x, y, z;
        internal Vector3(float a, float b, float c) { x = a; y = b; z = c; }
    }
    internal struct Vector2
    {
        internal float x, y;
        internal Vector2(float a, float b) { x = a; y = b; }
    }
    internal struct Color32
    {
        internal byte r, g, b, a;
        internal Color32(byte red, byte green, byte blue, byte alpha) { r = red; g = green; b = blue; a = alpha; }
    }
    internal struct Bounds
    {
        internal Vector3 center, extents;
        internal Bounds(Vector3 c, Vector3 e) { center = c; extents = e; }
    }
    internal sealed class Mesh : Object
    {
        internal static readonly List<Mesh> Created = new List<Mesh>();
        internal static int ThrowOnTriangleCall = -1;
        private static int triangleCalls;
        internal bool Unreadable;
        internal string name;
        internal HideFlags hideFlags;
        internal Bounds bounds;
        internal int subMeshCount;
        internal Vector2[] uv;
        internal Vector3[] normals;
        internal Color32[] colors32;
        private Vector3[] data;
        internal readonly Dictionary<int, List<int>> Triangles = new Dictionary<int, List<int>>();
        internal Mesh() { Created.Add(this); }
        internal Vector3[] vertices
        {
            get { if (Unreadable) throw new InvalidOperationException("native-read-forbidden"); return data; }
            set { if (Unreadable) throw new InvalidOperationException("native-write-forbidden"); data = value; }
        }
        internal int vertexCount { get { return data == null ? 0 : data.Length; } }
        internal void SetTriangles(List<int> indices, int submesh)
        {
            triangleCalls++;
            if (triangleCalls == ThrowOnTriangleCall) throw new InvalidOperationException("synthetic-upload-failure");
            Triangles[submesh] = new List<int>(indices);
        }
        internal void RecalculateNormals() { }
        internal void RecalculateBounds()
        {
            Vector3 lo = data[0], hi = data[0];
            foreach (Vector3 v in data)
            {
                lo.x = Math.Min(lo.x, v.x); lo.y = Math.Min(lo.y, v.y); lo.z = Math.Min(lo.z, v.z);
                hi.x = Math.Max(hi.x, v.x); hi.y = Math.Max(hi.y, v.y); hi.z = Math.Max(hi.z, v.z);
            }
            bounds = new Bounds(new Vector3((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, (lo.z + hi.z) / 2),
                new Vector3((hi.x - lo.x) / 2, (hi.y - lo.y) / 2, (hi.z - lo.z) / 2));
        }
        internal static void Reset()
        {
            Created.Clear(); triangleCalls = 0; ThrowOnTriangleCall = -1;
        }
    }
}
namespace Jinke.DredgeShipAppearance
{
    internal static class ShipAppearance
    {
        internal const string Cthulhu = "cthulhu-trawler-v1";
        internal const string Cyberpunk = "cyberpunk-trawler-v1";
    }
}
