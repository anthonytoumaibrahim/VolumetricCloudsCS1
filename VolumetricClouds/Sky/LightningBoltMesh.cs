using System.Collections.Generic;
using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// Grows one lightning channel: a main stroke from inside the cloud to the ground, with
    /// forks that lean away from it and die out in the air. See LightningBolt.shader for the
    /// vertex layout; everything is in world space.
    /// </summary>
    /// <remarks>
    /// The shape is midpoint displacement: split every segment, push the new point sideways
    /// by a random amount, halve the amount, repeat. Six rounds turn one line into 64 segments
    /// that are jagged at every scale, which is what a lightning channel looks like.
    /// </remarks>
    public static class LightningBoltMesh
    {
        private const int MainLevels = 6;
        private const int ForkLevels = 4;
        private const float MainWidth = 3f;
        private const float ForkWidth = 1.5f;

        private sealed class Builder
        {
            public readonly List<Vector3> Positions = new List<Vector3>();
            public readonly List<Vector3> Tangents = new List<Vector3>();
            public readonly List<Vector2> Shapes = new List<Vector2>();
            public readonly List<Vector2> Branches = new List<Vector2>();
            public readonly List<int> Triangles = new List<int>();
        }

        public static Mesh Build(Vector3 top, Vector3 ground, System.Random random)
        {
            Builder builder = new Builder();

            // No two bolts alike: how jagged, how heavy, how bushy all come from the strike's
            // own seed -- from a clean single stroke to a wide tree of forks.
            float roughness = Mathf.Lerp(0.09f, 0.19f, (float)random.NextDouble());
            float weight = Mathf.Lerp(0.7f, 1.5f, (float)random.NextDouble());
            double forkChance = Mathf.Lerp(0.03f, 0.2f, (float)random.NextDouble());

            List<Vector3> main = Channel(top, ground, MainLevels, roughness, random);
            AddRibbon(builder, main, MainWidth * weight, 1f, 1f);

            Vector3 down = (ground - top).normalized;

            // Forks leave the upper three quarters of the stroke; near the ground there is
            // only the stroke itself.
            for (int i = main.Count / 10; i < main.Count * 3 / 4; i++)
            {
                if (random.NextDouble() > forkChance)
                    continue;

                List<Vector3> fork = Fork(main[i], ground, down, 0.2f, 0.5f, random);
                AddRibbon(builder, fork, ForkWidth * weight, 0.45f, 0.1f);

                // And the occasional fork of a fork.
                if (random.NextDouble() < 0.5)
                {
                    Vector3 from = fork[fork.Count / 2];
                    AddRibbon(builder, Fork(from, ground, down, 0.1f, 0.25f, random), ForkWidth * weight * 0.7f, 0.22f, 0.05f);
                }
            }

            Mesh mesh = new Mesh { name = "VolumetricCloudsLightningBolt" };
            mesh.SetVertices(builder.Positions);
            mesh.SetNormals(builder.Tangents);
            mesh.SetUVs(0, builder.Shapes);
            mesh.SetUVs(1, builder.Branches);
            mesh.SetTriangles(builder.Triangles, 0);

            // The shader widens the ribbons, more so far from the camera.
            mesh.RecalculateBounds();
            Bounds bounds = mesh.bounds;
            bounds.Expand(200f);
            mesh.bounds = bounds;

            return mesh;
        }

        private static List<Vector3> Fork(Vector3 from, Vector3 ground, Vector3 down, float shortest, float longest,
            System.Random random)
        {
            float angle = (float)random.NextDouble() * Mathf.PI * 2f;
            Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 direction = (down * 0.75f + outward * 0.65f).normalized;

            float remaining = (ground - from).magnitude;
            float length = remaining * Mathf.Lerp(shortest, longest, (float)random.NextDouble());

            return Channel(from, from + direction * length, ForkLevels, 0.17f, random);
        }

        private static List<Vector3> Channel(Vector3 a, Vector3 b, int levels, float roughness, System.Random random)
        {
            List<Vector3> points = new List<Vector3> { a, b };
            float amplitude = (b - a).magnitude * roughness;

            for (int level = 0; level < levels; level++)
            {
                List<Vector3> next = new List<Vector3>(points.Count * 2);

                for (int i = 0; i < points.Count - 1; i++)
                {
                    Vector3 axis = (points[i + 1] - points[i]).normalized;

                    // A random push, with the part along the segment removed.
                    Vector3 push = new Vector3((float)random.NextDouble() * 2f - 1f,
                                               (float)random.NextDouble() * 2f - 1f,
                                               (float)random.NextDouble() * 2f - 1f);
                    push -= axis * Vector3.Dot(push, axis);

                    next.Add(points[i]);
                    next.Add((points[i] + points[i + 1]) * 0.5f + push * amplitude);
                }

                next.Add(points[points.Count - 1]);
                points = next;
                amplitude *= 0.5f;
            }

            return points;
        }

        /// <summary>
        /// One quad per segment. Each POINT carries the direction of the channel through it
        /// (from its neighbours), so consecutive quads share an edge and the ribbon has no
        /// gaps at the bends.
        /// </summary>
        private static void AddRibbon(Builder builder, List<Vector3> points, float width, float brightness, float tipBrightness)
        {
            if (points.Count < 2)
                return;

            for (int i = 0; i < points.Count - 1; i++)
            {
                int first = builder.Positions.Count;

                for (int end = 0; end < 2; end++)
                {
                    int p = i + end;
                    Vector3 tangent = (points[Mathf.Min(p + 1, points.Count - 1)] - points[Mathf.Max(p - 1, 0)]).normalized;

                    float along = p / (float)(points.Count - 1);
                    float level = Mathf.Lerp(brightness, brightness * tipBrightness, along);

                    for (int side = -1; side <= 1; side += 2)
                    {
                        builder.Positions.Add(points[p]);
                        builder.Tangents.Add(tangent);
                        builder.Shapes.Add(new Vector2(side, width));
                        builder.Branches.Add(new Vector2(level, along));
                    }
                }

                // first+0 = start/left, +1 = start/right, +2 = end/left, +3 = end/right
                builder.Triangles.Add(first + 0);
                builder.Triangles.Add(first + 1);
                builder.Triangles.Add(first + 3);
                builder.Triangles.Add(first + 0);
                builder.Triangles.Add(first + 3);
                builder.Triangles.Add(first + 2);
            }
        }
    }
}
