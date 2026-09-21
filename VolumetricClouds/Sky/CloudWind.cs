using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// The single wind offset shared by the shadow cookie and the visible clouds, so the
    /// two drift together instead of each integrating its own drift and slowly diverging.
    /// </summary>
    /// <remarks>
    /// The direction is an argument of <see cref="Advance"/> rather than a constant: it comes
    /// from the game's weather now. Because only the accumulated offset is ever read, a
    /// change of direction bends the drift from where the clouds are; nothing jumps.
    ///
    /// Kept in doubles, and saved with the city (SkySaveData), so it grows for the city's
    /// whole life. Shaders get <see cref="WeatherPhase"/> and <see cref="NoisePhase"/>, never
    /// the offset itself: see <see cref="Drift"/>.
    /// </remarks>
    public static class CloudWind
    {
        /// <summary>Used until the game's weather can be read.</summary>
        public static readonly Vector3 DefaultDirection = new Vector3(1f, 0f, 0.35f).normalized;

        /// <summary>
        /// The 3D noise drifts this much faster than the weather map, so clouds churn instead
        /// of sliding as a rigid sheet. (It was the literal 1.25 in CloudCommon.cginc.)
        /// </summary>
        public const double NoiseDrift = 1.25;

        private static double _x;
        private static double _z;

        /// <summary>Accumulated world-space offset along x, in metres. Exact, and can be very large.</summary>
        public static double X
        {
            get { return _x; }
        }

        /// <summary>Accumulated world-space offset along z, in metres.</summary>
        public static double Z
        {
            get { return _z; }
        }

        /// <summary>
        /// The same as a vector, for what does not read a tiling texture (the billboards, the
        /// flat-cookie fallback, the log). It loses precision far out: never hand it to a shader.
        /// </summary>
        public static Vector3 Offset
        {
            get { return new Vector3((float)_x, 0f, (float)_z); }
        }

        public static void Advance(Vector3 direction, float metres)
        {
            _x += (double)direction.x * metres;
            _z += (double)direction.z * metres;
        }

        /// <summary>Puts the offset where a save left it (or at 0 for a city without one).</summary>
        public static void Restore(double x, double z)
        {
            _x = x;
            _z = z;
        }

        public static void Reset()
        {
            Restore(0.0, 0.0);
        }

        /// <summary>
        /// How far through one tile of the weather map the wind has carried it, per axis, 0..1.
        /// The shaders read the map at <c>p.xz / tile - phase</c>, as do its C# twins.
        /// </summary>
        public static Vector2 WeatherPhase(float tile)
        {
            return new Vector2(Drift.Phase(_x, tile), Drift.Phase(_z, tile));
        }

        /// <summary>
        /// The same for the 3D noise, which drifts <see cref="NoiseDrift"/> times as fast.
        /// <paramref name="frequency"/> is how many times the lookup repeats per noise tile: 1
        /// for the base shape, the detail scale for the erosion. That one is not a whole number
        /// (1.5..10), so it gets its own phase: one shared phase times the scale would jump.
        /// </summary>
        public static Vector3 NoisePhase(float tile, float frequency)
        {
            double k = NoiseDrift * frequency;
            return new Vector3(Drift.Phase(_x * k, tile), 0f, Drift.Phase(_z * k, tile));
        }
    }
}
