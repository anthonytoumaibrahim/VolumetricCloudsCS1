using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// The single wind offset shared by the shadow cookie and the visible clouds, so the
    /// two drift together instead of each integrating its own drift and slowly diverging.
    /// </summary>
    public static class CloudWind
    {
        /// <summary>Horizontal direction the weather travels.</summary>
        public static readonly Vector3 Direction = new Vector3(1f, 0f, 0.35f).normalized;

        /// <summary>Accumulated world-space offset, in metres.</summary>
        public static Vector3 Offset { get; private set; }

        public static void Advance(float metres)
        {
            Offset += Direction * metres;
        }

        public static void Reset()
        {
            Offset = Vector3.zero;
        }
    }
}
