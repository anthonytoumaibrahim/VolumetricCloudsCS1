using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// The rainbow ARCH's geometry (1.3.0, round 2). Pure: Vector3 and Mathf only, tested offline
    /// (tools/test-rainbow.ps1).
    /// </summary>
    /// <remarks>
    /// The first build drew the bow each camera would see, centred on the camera, as a real one is:
    /// "the rainbow is happening on the camera lens itself ... we need it to happen in a 'fixed'
    /// place". Pinning that bow to a spot inside the rain does not work: from anywhere but the spot
    /// each line of sight crosses drops of every colour and it smears to nothing (preview 2, row B).
    /// So the bow a SPOT sees is drawn on a thin shell of radius R round that spot -- an arch
    /// standing in the shower, sharp from every camera, fixed in the world (preview 2, row D:
    /// docs/previews/render-rainbow.ps1 -Set anchor). The shader colours the shell by each point's
    /// angle from the spot's antisolar axis (RainbowTable) and lights it only where it stands in
    /// sunlit rain.
    ///
    /// ALWAYS A SEMICIRCLE ON THE GROUND (the author's first look: "too close to the ground", "too
    /// low"). With the spot ON the ground the arch is what a person there sees: with the sun at 33
    /// degrees a shallow arc 8.6 degrees high, and nothing at all above ~38 -- two and a half minutes
    /// of his 54-degree sun gave no arch. Now the spot is lifted to where the circle's centre is on
    /// the ground: the arch is the upper half of the circle, its feet on the ground, its top
    /// <see cref="Radius"/> puts just under the cloud base, leaning back from the sun by the sun's
    /// elevation (the circle is square to the antisolar axis). The iconic rainbow at any sun up to
    /// <see cref="MaxSunElevation"/>; red outside, the secondary outside that, as ever.
    /// </remarks>
    public static class RainbowArch
    {
        /// <summary>Degrees from the antisolar axis the arch is measured at: the primary's green.</summary>
        public const float BowAngle = 41.6f;

        /// <summary>Above this the arch would lean back into a flat ring lying over the city: none.</summary>
        public const float MaxSunElevation = 60f;

        /// <summary>The largest arch, metres from its spot (a low sun and a high base).</summary>
        public const float MaxRadius = 6000f;

        /// <summary>
        /// Where along the spot's line of sight through a point of the arch the rain and the sun are
        /// read, in radii: 0.6 to 1.4 in four even steps (the shader's ArchLight: 0.6 + k x 0.8 / 3).
        /// A real bow is the light of drops at every depth; read at the thin shell alone, the arch
        /// was cut off sharp along the edge of every cloud shadow crossing it -- "the rainbow is
        /// rendering 'behind' some clouds". Averaged, it breaks only where the rain really is in shadow.
        /// </summary>
        public static readonly float[] Depths = { 0.6f, 0.6f + 0.8f / 3f, 0.6f + 1.6f / 3f, 1.4f };

        /// <summary>
        /// The radius (spot to arch) that puts the top <paramref name="topHeight"/> metres over the
        /// ground the arch stands on: the circle's radius R sin(BowAngle), tilted back by the sun's
        /// elevation. 0 when there can be no arch (the sun under the horizon or too high).
        /// </summary>
        public static float Radius(float topHeight, float sunElevation)
        {
            if (sunElevation <= 0f || sunElevation > MaxSunElevation || topHeight <= 0f)
                return 0f;

            float r = topHeight / (Mathf.Sin(BowAngle * Mathf.Deg2Rad) * Mathf.Cos(sunElevation * Mathf.Deg2Rad));
            return Mathf.Min(r, MaxRadius);
        }

        /// <summary>
        /// The spot an arch of radius <paramref name="radius"/> is seen from, for the arch to stand on
        /// the ground at <paramref name="centre"/> (the middle between its feet): R cos(BowAngle)
        /// towards the sun from it, lifted with the sun.
        /// </summary>
        public static Vector3 Spot(Vector3 centre, float radius, Vector3 toSun)
        {
            return centre + toSun * (radius * Mathf.Cos(BowAngle * Mathf.Deg2Rad));
        }

        /// <summary>
        /// A point of the arch: <paramref name="phi"/> radians round the antisolar axis (0 = its top,
        /// ±pi/2 its feet), <paramref name="radius"/> metres from the spot, at <see cref="BowAngle"/>.
        /// <paramref name="toSun"/> is a unit vector towards the sun.
        /// </summary>
        public static Vector3 Point(Vector3 spot, float radius, Vector3 toSun, float phi)
        {
            Vector3 axis = -toSun;

            // Across the axis, level; and "up" round it (the arch's top side).
            Vector3 side = Vector3.Cross(Vector3.up, axis);
            if (side.sqrMagnitude < 1e-6f)
                side = Vector3.right;
            side = side.normalized;
            Vector3 over = Vector3.Cross(axis, side).normalized;
            if (over.y < 0f)
                over = -over;

            float angle = BowAngle * Mathf.Deg2Rad;
            Vector3 dir = axis * Mathf.Cos(angle) + (over * Mathf.Cos(phi) + side * Mathf.Sin(phi)) * Mathf.Sin(angle);
            return spot + dir * radius;
        }

        /// <summary>
        /// Where a ray crosses the arch's shell (the shader's ArchCrossings): the two distances along
        /// it, (-1, -1) when it misses. <paramref name="dir"/> is a unit vector.
        /// </summary>
        public static Vector2 Crossings(Vector3 origin, Vector3 dir, Vector3 spot, float radius)
        {
            Vector3 l = origin - spot;
            float b = Vector3.Dot(l, dir);
            float c = Vector3.Dot(l, l) - radius * radius;
            float disc = b * b - c;
            if (disc <= 0f)
                return new Vector2(-1f, -1f);

            float root = Mathf.Sqrt(disc);
            return new Vector2(-b - root, -b + root);
        }
    }
}
