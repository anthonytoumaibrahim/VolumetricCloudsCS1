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
    /// </remarks>
    public static class CloudWind
    {
        /// <summary>Used until the game's weather can be read.</summary>
        public static readonly Vector3 DefaultDirection = new Vector3(1f, 0f, 0.35f).normalized;

        /// <summary>Accumulated world-space offset, in metres.</summary>
        public static Vector3 Offset { get; private set; }

        public static void Advance(Vector3 direction, float metres)
        {
            Offset += direction * metres;
        }

        public static void Reset()
        {
            Offset = Vector3.zero;
        }
    }
}
