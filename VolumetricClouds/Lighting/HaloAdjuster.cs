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
    /// field refreshed once per frame by <see cref="HaloController"/>, rather than the settings
    /// and the switches that gate them being worked through again on every call.
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

        public static int LongCalls;
        public static int DataCalls;
        public static int VolumeTrue;

        // What the game actually passes in, so we can tell what these lights are.
        public static float ObservedMinRange = float.MaxValue;
        public static float ObservedMaxRange;
        public static float ObservedMaxIntensity;

        /// <summary>
        /// Tags lamp-type lights for the ported dynamic halo shader, and counts what passes.
        /// It changes nothing else about a light: until 1.1.1 it also scaled the range and
        /// brightness here ("Adjust dynamic lights"), which put out every light drawn this way,
        /// not just the vehicles' -- the range is the light itself, not only its halo.
        /// </summary>
        /// <returns>Always true: the original DrawLight must run, or the light vanishes.</returns>
        public static bool Adjust(ref Vector3 vel, float intensity, float range, bool volume)
        {
            LongCalls++;

            // What makes an IMT lamp look like the street lamps next to it.
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
            VolumeTrue = 0;
            ObservedMinRange = float.MaxValue;
            ObservedMaxRange = 0f;
            ObservedMaxIntensity = 0f;
        }
    }
}
