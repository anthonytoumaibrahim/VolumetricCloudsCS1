using System;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// The time of day as the rows with a clock see it (1.5.0): how much of Day, Sunset and Night is in
    /// the sky right now, from the sun's height, and their values blended by those shares.
    /// </summary>
    /// <remarks>
    /// Pure (tools/test-timeofday.ps1 checks it on the built DLL); <see cref="Update"/> is called once a
    /// frame from CloudVolume.ApplyLighting, on the main thread, before anything reads the shares.
    ///
    /// Night is the night factor the night colours, opacity and brightness have used since 1.3.0, bit
    /// for bit: 0 with the sun 2 degrees up, 1 at 8 degrees under the horizon. Sunset takes its share of
    /// what is left: none with the sun 3.16 degrees up, all of it at the horizon (then Night takes over, so
    /// Sunset peaks near 90%). Sunrise is the same.
    ///
    /// Sunset starts where the game's sunlight starts to fade (3.16 degrees, SunsetLight.HoldElevation):
    /// above it the light is the day's, full strength. It first ran from 15 degrees, and a Sunset value
    /// set for the dim light of the horizon (300% brightness against 15% by day, the author's) then lit
    /// the clouds ten times brighter than by day with the sun at 5 degrees, still in full sunlight
    /// ("way too bright" just before sunset and after dawn).
    ///
    /// A row whose Sunset equals its Day blends exactly as the old day/night pairs did
    /// (<see cref="DayAndNight"/>), so a night switch carried over from an older settings file draws
    /// exactly what it drew; a row with all three equal is its Day value exactly, whatever the time.
    /// </remarks>
    public static class TimeOfDay
    {
        /// <summary>The sun's height (degrees) where Sunset starts coming in, and where it is all of the day's share.</summary>
        public const float SunsetFrom = SunsetLight.HoldElevation;
        public const float SunsetFull = 0f;

        /// <summary>The sun's height (degrees) where Night starts coming in, and where it is all of the sky.</summary>
        public const float NightFrom = 2f;
        public const float NightFull = -8f;

        private static float _day = 1f;
        private static float _sunset;
        private static float _night;
        private static float _elevation = 90f;

        /// <summary>Day's share of the sky now, 0..1 (1 until the first frame).</summary>
        public static float Day
        {
            get { return _day; }
        }

        public static float Sunset
        {
            get { return _sunset; }
        }

        public static float Night
        {
            get { return _night; }
        }

        /// <summary>The sun's height the shares were worked out for, degrees.</summary>
        public static float Elevation
        {
            get { return _elevation; }
        }

        /// <summary>Once a frame, main thread: the shares for the sun at <paramref name="elevation"/> degrees.</summary>
        public static void Update(float elevation)
        {
            _elevation = elevation;
            Weights(elevation, out _day, out _sunset, out _night);
        }

        /// <summary>Back to broad day (the city is left).</summary>
        public static void Reset()
        {
            Update(90f);
        }

        /// <summary>The three shares for the sun at <paramref name="elevation"/> degrees; they add up to 1.</summary>
        public static void Weights(float elevation, out float day, out float sunset, out float night)
        {
            night = SmoothStep01(InverseLerp(NightFrom, NightFull, elevation));
            float s = SmoothStep01(InverseLerp(SunsetFrom, SunsetFull, elevation));
            sunset = s * (1f - night);
            day = (1f - s) * (1f - night);
        }

        /// <summary>A row's three values blended by the shares right now.</summary>
        public static float Blend(float day, float sunset, float night)
        {
            return Blend(day, sunset, night, _day, _sunset, _night);
        }

        /// <summary>
        /// The three values blended by these shares. All equal: <paramref name="day"/> exactly. Sunset
        /// equal to Day: <see cref="DayAndNight"/>, the old pairs' blend. Night equal to Day: the same
        /// with Sunset. Otherwise the weighted sum.
        /// </summary>
        public static float Blend(float day, float sunset, float night, float dayShare, float sunsetShare, float nightShare)
        {
            if (sunset == day && night == day)
                return day;

            if (sunset == day)
                return DayAndNight(day, night, nightShare);

            if (night == day)
                return DayAndNight(day, sunset, sunsetShare);

            if (sunsetShare >= 1f)
                return sunset;

            if (nightShare >= 1f)
                return night;

            if (dayShare >= 1f)
                return day;

            return day * dayShare + sunset * sunsetShare + night * nightShare;
        }

        /// <summary>
        /// Two values, the second coming in by <paramref name="share"/>: exactly the first at 0 and
        /// exactly the second at 1, straight between (Settings.DayAndNight and Mathf.Lerp, as the night's
        /// brightness and opacity blended before 1.5.0).
        /// </summary>
        public static float DayAndNight(float day, float other, float share)
        {
            if (!(share > 0f)) // NaN too
                return day;
            if (share >= 1f)
                return other;

            return day + (other - day) * share;
        }

        /// <summary>
        /// The Night value an older settings file or profile meant for a row that was the day half of
        /// a 1.3.0 switch ("Brightness differs at night", "Different colors at night"): its night
        /// element's text when the switch was on in that file -- <paramref name="valueDefault"/> if the
        /// element is missing -- and null (the Day value) when it was off or is not there.
        /// </summary>
        public static string OlderNight(System.Collections.Generic.IDictionary<string, string> values, string switchName,
            string valueName, string valueDefault)
        {
            string flag;
            bool on;
            if (values == null || !values.TryGetValue(switchName, out flag) || !SettingsXmlFormat.TryParseBool(flag, out on) || !on)
                return null;

            string value;
            return values.TryGetValue(valueName, out value) ? value : valueDefault;
        }

        /// <summary>Which of the three has the largest share now: 0 Day, 1 Sunset, 2 Night.</summary>
        public static int Main
        {
            get { return _night >= _sunset && _night >= _day ? 2 : _sunset >= _day ? 1 : 0; }
        }

        /// <summary>For the log: "sun 5.3 deg, day 16% sunset 84% night 0%".</summary>
        public static string Describe()
        {
            return "sun " + _elevation.ToString("F1") + " deg, day " + Percent(_day) + " sunset " + Percent(_sunset) +
                   " night " + Percent(_night);
        }

        private static string Percent(float share)
        {
            return Math.Round(share * 100.0).ToString("F0") + "%";
        }

        // Mathf.InverseLerp and Mathf.SmoothStep(0, 1, t), written out the same way so this stays pure
        // and the night share stays the night factor bit for bit.
        private static float InverseLerp(float a, float b, float value)
        {
            if (a != b)
                return Clamp01((value - a) / (b - a));

            return 0f;
        }

        private static float SmoothStep01(float t)
        {
            t = Clamp01(t);
            t = -2f * t * t * t + 3f * t * t;
            return 1f * t + 0f * (1f - t);
        }

        private static float Clamp01(float value)
        {
            if (value < 0f)
                return 0f;
            if (value > 1f)
                return 1f;

            return value;
        }
    }
}
