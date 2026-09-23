using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// How often our own lightning strikes, and WHERE -- mostly where the camera is looking.
    /// </summary>
    /// <remarks>
    /// Pure maths with no engine call in it (Vector3, Quaternion and Mathf are all managed),
    /// so it can be exercised offline the way tools\test-lightning.ps1 does.
    ///
    /// Why the viewport matters more than the rate. Measured from the log of 2026-09-20:
    /// with the override at 100% for 655 s the mod produced 199 strikes -- one every 3.3 s,
    /// exactly what the slider promised -- and it was seen as "one every few in-game minutes".
    /// The old placement picked a uniformly random compass direction 800 m to 7 km out, area
    /// weighted, so half of them were beyond 5 km and only about a fifth were anywhere near
    /// the front of the screen; a city-builder camera looks DOWN, so a strike 5 km away is
    /// usually above the top of the screen even when it is dead ahead. Most of the storm was
    /// happening off screen. Putting seven in ten strikes inside the frustum is worth several
    /// times more SEEN lightning than raising the rate, and costs nothing in thunder.
    ///
    /// The other three in ten stay all-round on purpose: thunder from behind you is right.
    /// </remarks>
    public static class LightningPlacement
    {
        /// <summary>Nothing closer than this: a strike on top of the camera reads as a bug.</summary>
        public const float MinDistance = 800f;

        /// <summary>Nothing further: past this a sheet is a faint pulse and a bolt a thread.</summary>
        public const float MaxDistance = 7000f;

        /// <summary>The share of strikes aimed through the viewport.</summary>
        public const float InViewShare = 0.7f;

        /// <summary>
        /// Rays are cast through the UPPER two thirds of the screen. The bottom third is the
        /// ground at the camera's feet, where a strike would be inside the city rather than
        /// out in the weather.
        /// </summary>
        public const float ViewBottom = 1f / 3f;

        /// <summary>
        /// Strikes per second. Below 100% this is exactly what it always was -- one every
        /// 30 s at 0, one every 3 s at 100% -- and the line continues from there: 200% is one
        /// every 1.5 s, 400% one every 0.75 s.
        /// </summary>
        /// <remarks>
        /// 400% is the ceiling for three reasons, none of them arbitrary: the cloud pass takes
        /// four flash sources (a mean strike lasts 0.67 s, so about 0.9 are alight at once at
        /// 400% and more than four at one instant stays rare); thunder is rate-limited to one
        /// clap per 1.2 s and beyond this that becomes a wall of noise; and the anti-strobe
        /// damping has to stay, which it does.
        /// </remarks>
        public static float StrikesPerSecond(float activity)
        {
            if (activity <= 0f)
                return 0f;

            if (activity <= 1f)
                return 1f / Mathf.Lerp(30f, 3f, activity);

            return Mathf.Min(activity, 4f) / 3f;
        }

        /// <summary>The same thing the other way up, for the log and the slider's readout.</summary>
        public static float SecondsBetween(float activity)
        {
            float rate = StrikesPerSecond(activity);
            return rate <= 0f ? float.PositiveInfinity : 1f / rate;
        }

        /// <summary>Area-weighted: even spread over the disc rather than crowding the camera.</summary>
        public static float Distance(float random)
        {
            return Mathf.Lerp(MinDistance, MaxDistance, Mathf.Sqrt(Mathf.Clamp01(random)));
        }

        /// <summary>A clap is at full volume for a strike this close, in metres; the fade starts here.</summary>
        public const float ThunderFullDistance = 1000f;

        /// <summary>The quietest the fade goes, so a distant storm is still heard.</summary>
        public const float ThunderMinVolume = 0.3f;

        /// <summary>
        /// How loud a clap is for a strike this far from the camera (1.1.0, asked for: thunder
        /// that "dims like 3D"). The game's own audio attenuates a clap over a 10 km range, which
        /// makes a strike 2 km out sound like one overhead. 1 out to
        /// <see cref="ThunderFullDistance"/>, then 1 / sqrt(distance): half volume at 4 km,
        /// 0.38 at the band's far edge (7 km). Gentler than open air's 1 / distance on purpose --
        /// a storm that goes near-silent at the edge of the band reads as broken. The delay the
        /// clap already has (the speed of sound) does the rest.
        /// </summary>
        public static float ThunderVolume(float distance)
        {
            if (distance <= ThunderFullDistance)
                return 1f;

            return Mathf.Max(ThunderMinVolume, Mathf.Sqrt(ThunderFullDistance / distance));
        }

        /// <summary>
        /// Where to try a strike. <paramref name="preferView"/> asks for a spot the camera can
        /// see; everything else is drawn by the caller so this stays testable.
        /// </summary>
        /// <param name="camera">Camera position.</param>
        /// <param name="rotation">Camera rotation.</param>
        /// <param name="fov">Vertical field of view, degrees.</param>
        /// <param name="aspect">Width / height.</param>
        /// <param name="groundY">Ground height near the camera; the plane the ray is met on.</param>
        /// <param name="inView">True when the direction came from the viewport.</param>
        public static Vector3 Spot(Vector3 camera, Quaternion rotation, float fov, float aspect,
            float groundY, bool preferView, float rx, float ry, float rDistance, out bool inView)
        {
            inView = false;

            if (preferView)
            {
                float tanHalf = Mathf.Tan(Mathf.Clamp(fov, 1f, 179f) * 0.5f * Mathf.Deg2Rad);
                float x = (2f * Mathf.Clamp01(rx) - 1f) * tanHalf * Mathf.Max(0.1f, aspect);
                float v = ViewBottom + (1f - ViewBottom) * Mathf.Clamp01(ry);
                float y = (2f * v - 1f) * tanHalf;

                Vector3 direction = rotation * new Vector3(x, y, 1f);
                Vector3 compass = new Vector3(direction.x, 0f, direction.z);

                if (compass.sqrMagnitude > 1e-6f)
                {
                    inView = true;
                    compass = compass.normalized;

                    // Where the ray meets the ground, clamped into the band. A camera looking
                    // level or up has no hit at all: it keeps the direction and takes a random
                    // distance, which is still somewhere ahead.
                    float drop = camera.y - groundY;
                    if (direction.y < -1e-3f && drop > 1f)
                    {
                        Vector3 hit = camera + direction * (drop / -direction.y);
                        float flat = new Vector2(hit.x - camera.x, hit.z - camera.z).magnitude;
                        return camera + compass * Mathf.Clamp(flat, MinDistance, MaxDistance);
                    }

                    return camera + compass * Distance(rDistance);
                }
            }

            // All round, as it always was: a random compass direction and an area-weighted
            // distance. Thunder that comes from behind you belongs in a storm.
            float angle = Mathf.Clamp01(rx) * Mathf.PI * 2f;
            return camera + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * Distance(rDistance);
        }
    }
}
