using UnityEngine;

namespace VolumetricClouds.Lighting
{
    /// <summary>
    /// Shared state for the light-halo patches.
    /// </summary>
    /// <remarks>
    /// DrawLight runs for every visible light every frame -- hundreds a frame at night --
    /// so everything here is a plain static field refreshed once per frame by
    /// <see cref="HaloController"/>. Reading SavedFloat.value per call would put the
    /// settings system on the hot path.
    /// </remarks>
    public static class HaloAdjuster
    {
        public static bool Enabled;
        public static bool SkipDrawLight;
        public static float RangeScale = 1f;
        public static float IntensityScale = 1f;
        public static float NearDistanceSqr;
        public static Vector3 CameraPosition;

        public static int LongCalls;
        public static int DataCalls;
        public static int VolumeSuppressed;
        public static int VolumeTrue;

        // What the game actually passes in, so we can tell what these lights are.
        public static float ObservedMinRange = float.MaxValue;
        public static float ObservedMaxRange;
        public static float ObservedMaxIntensity;

        /// <summary>
        /// Shrinks the halo and, for lights close to the camera, skips the volume pass.
        /// </summary>
        /// <returns>false to skip the original DrawLight entirely.</returns>
        public static bool Adjust(Vector3 pos, ref float intensity, ref float range, ref bool volume)
        {
            LongCalls++;

            if (volume)
                VolumeTrue++;

            if (range < ObservedMinRange)
                ObservedMinRange = range;
            if (range > ObservedMaxRange)
                ObservedMaxRange = range;
            if (intensity > ObservedMaxIntensity)
                ObservedMaxIntensity = intensity;

            // Decisive test: suppressing the call entirely should make these lights
            // vanish. If they don't, DrawLight isn't what draws what we're looking at.
            if (SkipDrawLight)
                return false;

            if (!Enabled)
                return true;

            if (volume && NearDistanceSqr > 0f)
            {
                float dx = pos.x - CameraPosition.x;
                float dy = pos.y - CameraPosition.y;
                float dz = pos.z - CameraPosition.z;

                if (dx * dx + dy * dy + dz * dz < NearDistanceSqr)
                {
                    volume = false;
                    VolumeSuppressed++;
                }
            }

            if (RangeScale != 1f)
                range *= RangeScale;

            if (IntensityScale != 1f)
                intensity *= IntensityScale;

            return true;
        }

        public static void CountLightDataCall()
        {
            DataCalls++;
        }

        public static void ResetCounters()
        {
            LongCalls = 0;
            DataCalls = 0;
            VolumeSuppressed = 0;
            VolumeTrue = 0;
            ObservedMinRange = float.MaxValue;
            ObservedMaxRange = 0f;
            ObservedMaxIntensity = 0f;
        }
    }
}
