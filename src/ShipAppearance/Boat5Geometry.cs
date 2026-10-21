using System;
using System.Collections.Generic;

namespace Jinke.DredgeShipAppearance
{
    // Pure geometry: no Unity calls, original vertices, material edits or scene objects.
    internal sealed class Boat5Geometry
    {
        internal struct Point
        {
            internal readonly float X, Y, Z;
            internal Point(float x, float y, float z) { X = x; Y = y; Z = z; }
        }

        internal struct UV
        {
            internal readonly float U, V;
            internal UV(float u, float v) { U = u; V = v; }
        }

        internal readonly List<Point> Vertices = new List<Point>();
        internal readonly List<UV> TexCoords = new List<UV>();
        internal readonly List<int> Hull = new List<int>();
        internal readonly List<int> Windows = new List<int>();

        // Original Boat5_Mat uses a small palette and a 16x16 mask. UV centres
        // select the original green hull/red roof/blue neutral mask regions.
        private static readonly UV HullUV = new UV(0.5625f, 0.0625f);
        private static readonly UV RoofUV = new UV(0.5625f, 0.9375f);
        private static readonly UV NeutralUV = new UV(0.5625f, 0.5f);
        private static readonly UV WindowUV = new UV(0.56738f, 0.33911f);

        internal static Boat5Geometry Build(int damage)
        {
            if (damage < 0 || damage > 3) throw new ArgumentOutOfRangeException("damage");
            var result = new Boat5Geometry();
            result.BuildHull(damage);
            result.BuildCabin();
            // Match the two retained Boat5 smoke emitters; these are cosmetic exhausts.
            result.Prism(new Point(0.006f, 0.48f, -0.4f), 0.064f, 1.80f, NeutralUV, 6);
            result.Prism(new Point(0.004f, 0.48f, -0.013f), 0.064f, 1.664f, NeutralUV, 6);
            // Support the retained upper lamp. The existing flag remains above the roof.
            result.Prism(new Point(0, 1.89f, 0.346f), 0.025f, 2.14f, NeutralUV, 6);
            result.Validate();
            return result;
        }

        private void BuildHull(int damage)
        {
            // +Z is the bow, y=0 the native waterline. Deck remains near the
            // existing rods, nets and cargo boxes, instead of raising their floor.
            Point[] rim = {
                new Point(0, .55f, 1.88f), new Point(.45f, .49f, 1.55f),
                new Point(.68f, .44f, .65f), new Point(.67f, .44f, -.65f),
                new Point(.52f, .46f, -1.40f), new Point(-.52f, .46f, -1.40f),
                new Point(-.67f, .44f, -.65f), new Point(-.68f, .44f, .65f),
                new Point(-.45f, .49f, 1.55f)
            };
            var middle = new Point[rim.Length];
            var bottom = new Point[rim.Length];
            for (int i = 0; i < rim.Length; i++)
            {
                Point p = rim[i];
                rim[i] = Dent(p, damage);
                middle[i] = Dent(new Point(p.X * .86f, -.07f, p.Z * .95f), damage);
                bottom[i] = new Point(p.X * .45f, -.375f, p.Z * .82f);
            }
            Point deckCentre = new Point(0, .44f, .05f);
            Point keelCentre = new Point(0, -.375f, .05f);
            for (int i = 0; i < rim.Length; i++)
            {
                int j = (i + 1) % rim.Length;
                Point outward = new Point((rim[i].X + rim[j].X) * .5f, 0,
                    (rim[i].Z + rim[j].Z) * .5f - .05f);
                Quad(bottom[i], middle[i], middle[j], bottom[j], outward, HullUV, Hull);
                Quad(middle[i], rim[i], rim[j], middle[j], outward, HullUV, Hull);
                Triangle(deckCentre, rim[i], rim[j], new Point(0, 1, 0), NeutralUV, Hull);
                Triangle(keelCentre, bottom[j], bottom[i], new Point(0, -1, 0), HullUV, Hull);
                Point topI = new Point(rim[i].X, rim[i].Y + .13f, rim[i].Z);
                Point topJ = new Point(rim[j].X, rim[j].Y + .13f, rim[j].Z);
                Point inI = new Point(topI.X * .95f, topI.Y, topI.Z * .98f);
                Point inJ = new Point(topJ.X * .95f, topJ.Y, topJ.Z * .98f);
                Quad(rim[i], topI, topJ, rim[j], outward, HullUV, Hull);
                Quad(inI, inJ, topJ, topI, new Point(0, 1, 0), NeutralUV, Hull);
                Quad(new Point(inI.X, rim[i].Y, inI.Z), inI, inJ,
                    new Point(inJ.X, rim[j].Y, inJ.Z), Negate(outward), HullUV, Hull);
            }
        }

        private static Point Dent(Point p, int damage)
        {
            float dent = p.X > 0 && p.Z < .8f && p.Z > -1f ? damage * .045f : 0;
            return new Point(p.X - dent, p.Y - dent * .6f, p.Z);
        }

        private void BuildCabin()
        {
            // A tapered fore wheelhouse leaves the aft working deck free.
            Point a = new Point(-.55f, .45f, .18f), b = new Point(.55f, .45f, .18f);
            Point c = new Point(.50f, .45f, 1.42f), d = new Point(-.50f, .45f, 1.42f);
            Point e = new Point(-.44f, 1.78f, .23f), f = new Point(.44f, 1.78f, .23f);
            Point g = new Point(.40f, 1.78f, 1.25f), h = new Point(-.40f, 1.78f, 1.25f);
            Quad(a, e, f, b, new Point(0, 0, -1), NeutralUV, Hull);
            Quad(b, f, g, c, new Point(1, 0, 0), NeutralUV, Hull);
            Quad(c, g, h, d, new Point(0, 0, 1), NeutralUV, Hull);
            Quad(d, h, e, a, new Point(-1, 0, 0), NeutralUV, Hull);
            Box(new Point(-.49f, 1.78f, .14f), new Point(.49f, 1.89f, 1.34f), RoofUV);
            // Meet the retained FishingRod1 clamp without moving its original
            // mesh or Transform. The back of this small shelf embeds in the cabin.
            Box(new Point(.26f, .535f, .135f), new Point(.34f, .585f, .23f), NeutralUV);

            // Glass sits just outside the tapered cabin surfaces, with real
            // slot-1 triangles. Local coordinates do not move retained lamps.
            SideWindow(-1, .49f, 1.06f, .46f, .91f);
            SideWindow(1, .49f, 1.06f, .46f, .91f);
            SideWindow(-1, 1.15f, 1.63f, .43f, .90f);
            SideWindow(1, 1.15f, 1.63f, .43f, .90f);
            float y0 = 1.16f, y1 = 1.62f;
            float z0 = FrontZ(y0) + .004f, z1 = FrontZ(y1) + .004f;
            Quad(new Point(-.32f, y0, z0), new Point(-.32f, y1, z1),
                new Point(-.035f, y1, z1), new Point(-.035f, y0, z0),
                new Point(0, 0, 1), WindowUV, Windows);
            Quad(new Point(.035f, y0, z0), new Point(.035f, y1, z1),
                new Point(.32f, y1, z1), new Point(.32f, y0, z0),
                new Point(0, 0, 1), WindowUV, Windows);
        }

        private void SideWindow(int side, float y0, float y1, float z0, float z1)
        {
            Quad(new Point(side * (SideX(y0, z0) + .004f), y0, z0),
                new Point(side * (SideX(y1, z0) + .004f), y1, z0),
                new Point(side * (SideX(y1, z1) + .004f), y1, z1),
                new Point(side * (SideX(y0, z1) + .004f), y0, z1),
                new Point(side, 0, 0), WindowUV, Windows);
        }

        private static float SideX(float y, float z)
        {
            float t = (y - .45f) / 1.33f;
            float rear = .55f + (.44f - .55f) * t;
            float front = .50f + (.40f - .50f) * t;
            float rearZ = .18f + (.23f - .18f) * t;
            float frontZ = FrontZ(y);
            return rear + (front - rear) * (z - rearZ) / (frontZ - rearZ);
        }

        private static float FrontZ(float y) { return 1.42f + (1.25f - 1.42f) * ((y - .45f) / 1.33f); }

        private void Box(Point min, Point max, UV uv)
        {
            Point a = new Point(min.X, min.Y, min.Z), b = new Point(max.X, min.Y, min.Z);
            Point c = new Point(max.X, min.Y, max.Z), d = new Point(min.X, min.Y, max.Z);
            Point e = new Point(min.X, max.Y, min.Z), f = new Point(max.X, max.Y, min.Z);
            Point g = new Point(max.X, max.Y, max.Z), h = new Point(min.X, max.Y, max.Z);
            Quad(a, e, f, b, new Point(0, 0, -1), uv, Hull);
            Quad(b, f, g, c, new Point(1, 0, 0), uv, Hull);
            Quad(c, g, h, d, new Point(0, 0, 1), uv, Hull);
            Quad(d, h, e, a, new Point(-1, 0, 0), uv, Hull);
            Quad(e, h, g, f, new Point(0, 1, 0), uv, Hull);
            Quad(a, b, c, d, new Point(0, -1, 0), uv, Hull);
        }

        private void Prism(Point centre, float radius, float top, UV uv, int sides)
        {
            for (int i = 0; i < sides; i++)
            {
                double angle = i * 2 * Math.PI / sides, next = (i + 1) * 2 * Math.PI / sides;
                Point a = new Point(centre.X + radius * (float)Math.Cos(angle), centre.Y, centre.Z + radius * (float)Math.Sin(angle));
                Point b = new Point(centre.X + radius * (float)Math.Cos(next), centre.Y, centre.Z + radius * (float)Math.Sin(next));
                Point c = new Point(b.X, top, b.Z), d = new Point(a.X, top, a.Z);
                Quad(a, b, c, d, new Point((a.X + b.X) * .5f - centre.X, 0, (a.Z + b.Z) * .5f - centre.Z), uv, Hull);
                Triangle(new Point(centre.X, top, centre.Z), d, c, new Point(0, 1, 0), uv, Hull);
            }
        }

        private void Quad(Point a, Point b, Point c, Point d, Point outward, UV uv, List<int> indices)
        {
            Triangle(a, b, c, outward, uv, indices);
            Triangle(a, c, d, outward, uv, indices);
        }

        private void Triangle(Point a, Point b, Point c, Point outward, UV uv, List<int> indices)
        {
            Point cross = Cross(Subtract(b, a), Subtract(c, a));
            if (Dot(cross, outward) < 0) { Point swap = b; b = c; c = swap; }
            // Split vertices per triangle for deliberately faceted normals.
            int start = Vertices.Count;
            Vertices.Add(a); Vertices.Add(b); Vertices.Add(c);
            TexCoords.Add(uv); TexCoords.Add(uv); TexCoords.Add(uv);
            indices.Add(start); indices.Add(start + 1); indices.Add(start + 2);
        }

        internal void Validate()
        {
            if (Vertices.Count == 0 || Vertices.Count != TexCoords.Count || Vertices.Count > 10000)
                throw new InvalidOperationException("船型顶点集合无效。");
            foreach (Point p in Vertices)
                if (!Finite(p.X) || !Finite(p.Y) || !Finite(p.Z)) throw new InvalidOperationException("船型含非有限顶点。");
            foreach (UV uv in TexCoords)
                if (!Finite(uv.U) || !Finite(uv.V) || uv.U < 0 || uv.U > 1 || uv.V < 0 || uv.V > 1)
                    throw new InvalidOperationException("船型纹理坐标无效。");
            ValidateTriangles(Hull);
            ValidateTriangles(Windows);
        }

        private void ValidateTriangles(List<int> indices)
        {
            if (indices.Count == 0 || indices.Count % 3 != 0) throw new InvalidOperationException("船型子网格为空。");
            for (int i = 0; i < indices.Count; i += 3)
            {
                for (int j = 0; j < 3; j++)
                    if (indices[i + j] < 0 || indices[i + j] >= Vertices.Count) throw new InvalidOperationException("船型三角索引越界。");
                Point a = Vertices[indices[i]], b = Vertices[indices[i + 1]], c = Vertices[indices[i + 2]];
                Point normal = Cross(Subtract(b, a), Subtract(c, a));
                if (Dot(normal, normal) < .0000000001f) throw new InvalidOperationException("船型含零面积三角形。");
            }
        }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static Point Negate(Point value) { return new Point(-value.X, -value.Y, -value.Z); }
        private static Point Subtract(Point a, Point b) { return new Point(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
        private static Point Cross(Point a, Point b) { return new Point(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X); }
        private static float Dot(Point a, Point b) { return a.X * b.X + a.Y * b.Y + a.Z * b.Z; }
    }
}
