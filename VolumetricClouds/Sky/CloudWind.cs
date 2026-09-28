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

        // CLOUD POSITION (1.2.1; asked for: "more control over the position of the clouds ...
        // edit the X and Y values", "for advanced settings"): the player's shift of this city's
        // whole sky, in metres, on top of the drift. Kept in the city's save (SkyState version
        // 2), never in the settings: every city's pattern is its own.
        private static double _shiftX;
        private static double _shiftZ;

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

        /// <summary>The player's shift of the clouds along x, metres ("Cloud position X").</summary>
        public static double ShiftX
        {
            get { return _shiftX; }
        }

        /// <summary>The player's shift of the clouds along z, metres ("Cloud position Y": the map's second axis).</summary>
        public static double ShiftZ
        {
            get { return _shiftZ; }
        }

        /// <summary>
        /// The same as a vector, the shift included, for what does not read a tiling texture (the
        /// flat-cookie fallback, the log). It loses precision far out: never hand it to a shader.
        /// </summary>
        public static Vector3 Offset
        {
            get { return new Vector3((float)(_x + _shiftX), 0f, (float)(_z + _shiftZ)); }
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

        /// <summary>
        /// Moves this city's clouds (the "Cloud position" sliders, or a save being read). Every
        /// lookup moves by the same metres -- the weather map, the noises, the fragments -- so the
        /// sky moves as one and keeps its shapes; the rain, the shadows and lightning's twins
        /// follow through the same phases. Not a drift: it never grows.
        /// </summary>
        public static void SetShift(double x, double z)
        {
            _shiftX = x;
            _shiftZ = z;
        }

        public static void Reset()
        {
            Restore(0.0, 0.0);
            SetShift(0.0, 0.0);
        }

        /// <summary>
        /// How far through one tile of the weather map the wind has carried it, per axis, 0..1.
        /// The shaders read the map at <c>p.xz / tile - phase</c>, as do its C# twins.
        /// </summary>
        public static Vector2 WeatherPhase(float tile)
        {
            return new Vector2(Drift.Phase(_x + _shiftX, tile), Drift.Phase(_z + _shiftZ, tile));
        }

        /// <summary>
        /// The same for the 3D noise, which drifts <see cref="NoiseDrift"/> times as fast.
        /// <paramref name="frequency"/> is how many times the lookup repeats per noise tile: 1
        /// for the base shape, the detail scale for the erosion. That one is not a whole number
        /// (1.5..10), so it gets its own phase: one shared phase times the scale would jump.
        /// The shift moves the noise by the same metres as the map (not 1.25x): shapes kept.
        /// </summary>
        public static Vector3 NoisePhase(float tile, float frequency)
        {
            return new Vector3(Drift.Phase((_x * NoiseDrift + _shiftX) * frequency, tile), 0f,
                               Drift.Phase((_z * NoiseDrift + _shiftZ) * frequency, tile));
        }

        /// <summary>
        /// The phase of a lookup that reads the weather map turned by <paramref name="degrees"/>
        /// at <paramref name="multiple"/> times its frequency (CloudFragments): the shader reads
        /// <c>turn(p / tile) * multiple - phase</c>, and turning is linear, so the phase is the
        /// turned drift over <c>tile / multiple</c>. Moves with the clouds, exactly.
        /// </summary>
        public static Vector2 TurnedPhase(float tile, int multiple, double degrees)
        {
            double x, z;
            Drift.Turn(_x + _shiftX, _z + _shiftZ, degrees, out x, out z);
            double period = (double)tile / multiple;
            return new Vector2(Drift.Phase(x, period), Drift.Phase(z, period));
        }
    }
}
