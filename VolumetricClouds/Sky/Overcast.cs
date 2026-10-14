using System;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// How overcast the sky is as the rows with a cloud button see it (1.5.0): the cloud cover's share
    /// of Overcast against Clear sky, and a row's two values blended by it.
    /// </summary>
    /// <remarks>
    /// Pure (tools/test-timeofday.ps1 checks it on the built DLL); <see cref="Update"/> is called once a
    /// frame from CloudVolume.ApplyLighting, beside Sky.TimeOfDay's, before anything reads the share.
    ///
    /// Straight in the cover: 0% is all Clear sky, 100% all Overcast -- exactly how the brightness's
    /// "clear sky" and "full overcast" always blended (Settings.BrightnessCurve, Mathf.Lerp), so that pair
    /// became the Cloud brightness row's two values with not a bit of difference. Separate from the time
    /// of day: each time has its own two, blended here first, then the three by the sun's height.
    /// </remarks>
    public static class Overcast
    {
        private static float _share;
        private static float _cover;

        /// <summary>Overcast's share now, 0..1 (Clear sky has the rest). 0 until the first frame.</summary>
        public static float Share
        {
            get { return _share; }
        }

        /// <summary>Once a frame, main thread: the share for the cloud cover now, 0..1.</summary>
        public static void Update(float coverage)
        {
            _cover = coverage;
            _share = Clamp01(coverage);
        }

        /// <summary>Back to a clear sky (the city is left).</summary>
        public static void Reset()
        {
            Update(0f);
        }

        /// <summary>A row's Clear sky and Overcast values blended by the share now.</summary>
        public static float Blend(float clear, float overcast)
        {
            return Blend(clear, overcast, _share);
        }

        /// <summary>
        /// The two blended by <paramref name="share"/>: Mathf.Lerp's own sum, so the brightness curve is
        /// reproduced exactly; the two equal -- every row an existing player has -- is the Clear sky value
        /// exactly, whatever the cover.
        /// </summary>
        public static float Blend(float clear, float overcast, float share)
        {
            if (clear == overcast)
                return clear;

            return clear + (overcast - clear) * Clamp01(share);
        }

        /// <summary>For the log: "cover 70%: clear sky 30% overcast 70%".</summary>
        public static string Describe()
        {
            return "cover " + Percent(_cover) + ": clear sky " + Percent(1f - _share) + " overcast " + Percent(_share);
        }

        private static string Percent(float share)
        {
            return Math.Round(share * 100.0).ToString("F0") + "%";
        }

        private static float Clamp01(float value)
        {
            if (!(value > 0f)) // NaN too
                return 0f;
            if (value > 1f)
                return 1f;

            return value;
        }
    }
}
