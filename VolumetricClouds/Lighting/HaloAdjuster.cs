using UnityEngine;

namespace VolumetricClouds.Lighting
{
    /// <summary>
    /// Shared state for the DrawLight patches, which reach DYNAMIC lights only.
    /// </summary>
    /// <remarks>
    /// LightEffect.RenderEffect opens with "if (m_batchedLight &amp;&amp; !id.IsEmpty) return", so a
    /// street or building lamp never gets here at any distance; what does is lights that are
    /// not batched (vehicles, for one) and batched ones rendered without an instance id.
    /// That is still hundreds of calls a frame at night, so everything here is a plain static
    /// field refreshed once per frame by <see cref="HaloController"/>. Reading
    /// SavedFloat.value per call would put the settings system on the hot path.
    /// </remarks>
    public static class HaloAdjuster
    {
        /// <summary>
        /// Must equal LAMP_TAG in LightHaloDynamic.shader. A lamp-type light drawn dynamically
        /// is submitted with velocity (0, LampTag, 0); DrawLight only stores the velocity into
        /// the _LightVel array, and the shader takes the tag out before using it.
        /// </summary>
        public const float LampTag = 40000f;

        /// <summary>
        /// True while the ported dynamic halo shader is in place (set each frame by
        /// HaloOverride). The tag must NEVER reach the game's own shader, which would read it
        /// as a velocity and fling that light's rain streaks away.
        /// </summary>
        public static bool TagLamps;

        /// <summary>
        /// True while LightEffect.RenderEffect is running for a light that is lamp-type
        /// (m_batchedLight) but has no instance id -- which is the only reason such a light
        /// reaches DrawLight at all. Intersection Marking Tool renders its props that way.
        /// </summary>
        public static bool InLampEffect;

        public static int LampsTagged;

        public static bool Enabled;
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
        /// <returns>Always true: the original DrawLight must run, or the light vanishes.</returns>
        public static bool Adjust(Vector3 pos, ref Vector3 vel, ref float intensity, ref float range, ref bool volume)
        {
            LongCalls++;

            // Independent of the "adjust dynamic lights" switch below: this is what makes an
            // IMT lamp look like the street lamps next to it.
            if (TagLamps && InLampEffect)
            {
                vel = new Vector3(0f, LampTag, 0f);
                LampsTagged++;
            }

            if (volume)
                VolumeTrue++;

            if (range < ObservedMinRange)
                ObservedMinRange = range;
            if (range > ObservedMaxRange)
                ObservedMaxRange = range;
            if (intensity > ObservedMaxIntensity)
                ObservedMaxIntensity = intensity;

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
            LampsTagged = 0;
            DataCalls = 0;
            VolumeSuppressed = 0;
            VolumeTrue = 0;
            ObservedMinRange = float.MaxValue;
            ObservedMaxRange = 0f;
            ObservedMaxIntensity = 0f;
        }
    }
}
