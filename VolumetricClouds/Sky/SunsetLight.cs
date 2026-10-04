using System;
using System.Globalization;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// SUNSET LIGHT: the clouds keep the low sun's light until the sun has set for THEM, turning
    /// redder as it sinks, instead of greying out with the ground's. 0% is the light from before,
    /// exactly (the clouds' sun is then the game's sun, as it always was).
    /// </summary>
    /// <remarks>
    /// The game fades its sun from 3.16 degrees up (DayNightProperties: intensity and colour both x
    /// DayTime, so the clouds' sun went as ~DayTime^3.2) and its colour gradient runs from the
    /// evening orange to the night's grey-blue across sunset itself (his log: 17.76h -> 18.48h). A
    /// cloud a kilometre up sees the sun a degree longer than the ground does (the dip of its
    /// horizon, acos(R / (R + h))), and that last light has come the longest way through the air,
    /// which takes the blue and green out of it first (Rayleigh, ~lambda^-4) -- red and pink
    /// undersides over a city already in dusk. So below the hold elevation the clouds' sun is the
    /// game's own light AT the hold elevation (its gradient read at the time the sun stood there --
    /// whatever a theme or Render It! has put in it), x the game's own day factor once, measured on the
    /// cloud's horizon, x the extra Rayleigh extinction of the longer path (Kasten and Young's air
    /// mass, Bucholtz's optical depths) with the red kept. The Earth's shadow itself -- the moment the
    /// sun goes for a point h metres up -- is worked out per height in the shader (_SunsetEdge), so
    /// the tops stay lit after the bases. Never darker than the game's light before that edge; only
    /// the clouds' sun (the ground, the fog and the shadows keep the game's).
    /// </remarks>
    public static class SunsetLight
    {
        /// <summary>
        /// Degrees: below this the game's sun starts to fade (DayTime = atan(5.35 sin e) / 1.1 + 0.739
        /// reaches 1 here); the hold light is the game's light at this height.
        /// </summary>
        public const float HoldElevation = 3.16f;

        public const double EarthRadius = 6371000.0;

        /// <summary>The air's scale height, metres: a cloud h up has exp(-h / this) of the air above it.</summary>
        public const float ScaleHeight = 8400f;

        /// <summary>Rayleigh optical depth of the whole atmosphere straight up from sea level, at 650 / 550 / 450 nm.</summary>
        public const float RayleighR = 0.050f, RayleighG = 0.098f, RayleighB = 0.225f;

        /// <summary>
        /// The Earth's shadow, in degrees over the cloud's own horizon: the sun's centre is lifted
        /// ~0.57 by refraction at the horizon and its disc is 0.53 across, so the last of it goes at
        /// -0.85 and the whole disc is up from -0.3.
        /// </summary>
        public const float EdgeGone = -0.85f, EdgeFull = -0.3f;

        /// <summary>Degrees of dip per square root of a metre: dip(h) = acos(R / (R + h)) ~ sqrt(2 h / R).</summary>
        public static readonly float DipPerRootMetre = (float)(Math.Sqrt(2.0 / EarthRadius) * 180.0 / Math.PI);

        /// <summary>The setting, 0..2 (the slider's 0%..200%); its default when there is none.</summary>
        public static float Setting
        {
            get
            {
                return Settings.SunsetLight != null
                    ? Math.Max(0f, Math.Min(2f, Settings.SunsetLight.value))
                    : Settings.Defaults.SunsetLight;
            }
        }

        /// <summary>How much of it is used: the slider up to 100%, all of it above. 0 = off.</summary>
        public static float AmountFor(float setting)
        {
            return Math.Max(0f, Math.Min(1f, setting));
        }

        /// <summary>Above 100% the clouds see the sun as from higher up: 200% = twice the dip, longer.</summary>
        public static float StretchFor(float setting)
        {
            return Math.Max(1f, Math.Min(2f, setting));
        }

        /// <summary>How far under the horizontal the sun can be and still reach a point h metres up, degrees.</summary>
        public static float Dip(float height)
        {
            double h = Math.Max(0.0, height);
            return (float)(Math.Acos(EarthRadius / (EarthRadius + h)) * 180.0 / Math.PI);
        }

        /// <summary>The game's DayTime (DayNightProperties.get_DayTime) for a sun this many degrees up.</summary>
        public static float DayTime(float elevation)
        {
            double y = Math.Max(Math.Sin(elevation * Math.PI / 180.0), -0.1975);
            return (float)Math.Max(0.0, Math.Min(1.0, Math.Atan(y * 5.35) / 1.1 + 0.739));
        }

        /// <summary>Kasten and Young's (1989) relative air mass; at and below the horizon, the horizon's (~38).</summary>
        public static float AirMass(float elevation)
        {
            double e = Math.Max(0.0, elevation);
            return (float)(1.0 / (Math.Sin(e * Math.PI / 180.0) + 0.50572 * Math.Pow(e + 6.07995, -1.6364)));
        }

        /// <summary>
        /// The time of day, hours, at which the game's sun stands this many degrees up: its sun path
        /// (DayNightProperties.UpdateSun) is elevation = asin(cos(latitude) sin((t - 6) x 15 deg)).
        /// </summary>
        public static float TimeAt(float elevation, float latitude, bool evening)
        {
            double s = Math.Sin(elevation * Math.PI / 180.0) / Math.Max(1e-3, Math.Cos(latitude * Math.PI / 180.0));
            double hours = Math.Asin(Math.Max(-1.0, Math.Min(1.0, s))) * 180.0 / Math.PI / 15.0;
            return (float)(evening ? 18.0 - hours : 6.0 + hours);
        }

        /// <summary>The air the clouds' sun crosses beyond what it crossed at the hold elevation, in air masses at the cloud.</summary>
        public static float ExtraAirMass(float elevation, float height, float setting)
        {
            float dip = Dip(height) * StretchFor(setting);
            float extra = AirMass(elevation + dip) - AirMass(HoldElevation + dip);
            return Math.Max(0f, extra) * (float)Math.Exp(-Math.Max(0f, height) / ScaleHeight);
        }

        /// <summary>
        /// The clouds' sun h metres up, per channel, as a share of the hold light: 1 at and above the
        /// hold elevation, below it the game's day factor on the cloud's horizon x the longer path's
        /// reddening (the red kept). The Earth's shadow is not in it (the shader's, per height).
        /// </summary>
        public static void Share(float elevation, float height, float setting, out float r, out float g, out float b)
        {
            if (elevation >= HoldElevation)
            {
                r = g = b = 1f;
                return;
            }

            float seen = elevation + Dip(height) * StretchFor(setting);
            float fade = DayTime(seen);
            float extra = ExtraAirMass(elevation, height, setting);
            r = fade;
            g = fade * (float)Math.Exp(-extra * (RayleighG - RayleighR));
            b = fade * (float)Math.Exp(-extra * (RayleighB - RayleighR));
        }

        /// <summary>
        /// The Earth's shadow for a point h metres over the horizon's level: 1 while the sun is up for
        /// it, 0 once it has set for it (smooth over the sun's disc). The shader's EarthShadow.
        /// </summary>
        public static float Edge(float elevation, float height, float setting)
        {
            float seen = elevation + DipPerRootMetre * StretchFor(setting) * (float)Math.Sqrt(Math.Max(0f, height));
            float e = Math.Max(0f, Math.Min(1f, (seen - EdgeGone) / (EdgeFull - EdgeGone)));
            return e * e * (3f - 2f * e);
        }

        public static string Describe(float setting)
        {
            if (AmountFor(setting) <= 0f)
                return "off (the game's sun, as before)";

            float r, g, b;
            Share(-1f, 1300f, setting, out r, out g, out b);
            return (setting * 100f).ToString("F0", CultureInfo.InvariantCulture) + "% -- a cloud 1300 m up keeps the sun until " +
                   (-(Dip(1300f) * StretchFor(setting)) + EdgeGone).ToString("F1", CultureInfo.InvariantCulture) +
                   " deg; at -1 deg it gets (" + r.ToString("F2", CultureInfo.InvariantCulture) + "," +
                   g.ToString("F2", CultureInfo.InvariantCulture) + "," + b.ToString("F2", CultureInfo.InvariantCulture) +
                   ") of the light at " + HoldElevation.ToString("F2", CultureInfo.InvariantCulture) + " deg";
        }
    }
}
