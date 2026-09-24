using System.Globalization;
using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// CLOUD FRAGMENTS: small, thin shreds of cloud clustered round the big ones, in the same
    /// layer and on the same flat base -- the fractus and small cumulus real skies have between
    /// and beside their large clouds. Designed on offline renders of this very formula (a CPU
    /// port of the density function, fed by the mod's own textures), then built to match them.
    /// </summary>
    /// <remarks>
    /// The same weather texture, read again at <see cref="Multiple"/> times the frequency, turned
    /// by <see cref="Angle"/> and shifted by <see cref="Offset"/> so its pieces line up with
    /// nothing of the big pattern; its own threshold, solved as DRAWN (the slider is a real share
    /// of the sky); lowered near a big cloud's edge so the shreds cluster there; shorter columns
    /// (<see cref="Style.Thickness"/>) from the same base. Where both exist the larger shape wins, so the
    /// big clouds are exactly as they were. It drifts with the same wind (a turned, scaled phase
    /// of the one drift -- nothing new in the save) and rain stays with the big clouds.
    /// These constants are part of what a seed reproduces (invariant 14): change one and every
    /// city's fragments change.
    /// </remarks>
    public static class CloudFragments
    {
        /// <summary>Repeats per weather tile: a whole number, so the phase wraps with the lookup.</summary>
        public const int Multiple = 9;

        /// <summary>Degrees the fragment lookup is turned against the big pattern.</summary>
        public const double Angle = 37.0;

        /// <summary>Share of one fragment repeat the lookup is shifted by.</summary>
        public static readonly Vector2 Offset = new Vector2(0.37f, 0.61f);

        /// <summary>How far out from a big cloud (weather units under its threshold) clustering reaches.</summary>
        public const float EdgeReach = 0.15f;

        // RANDOM AREAS: a coarser lookup of the same map, turned the other way, lets fragments
        // into only part of the sky, in patches that do not repeat with the fragments' own
        // 1.5 km pattern. The fragments' own share is raised to match, so the total stays about
        // what the slider says. (A style with AreaShare 1 lets them in everywhere.)

        /// <summary>Repeats per weather tile of the area lookup (2.7 km on 13.5 km): a whole number.</summary>
        public const int AreaMultiple = 5;

        /// <summary>Degrees the area lookup is turned.</summary>
        public const double AreaAngle = -61.0;

        public static readonly Vector2 AreaOffset = new Vector2(0.19f, 0.83f);

        /// <summary>
        /// One look. The three were chosen side by side from offline renders of the same scene.
        /// </summary>
        public struct Style
        {
            /// <summary>For the log (English, never translated).</summary>
            public string Name;

            /// <summary>Metres a fragment stands above the base (never more than the layer).</summary>
            public float Thickness;

            /// <summary>
            /// Their density as a share of the big clouds' ("less full"). A weaker SHAPE instead
            /// makes them vanish: the noise that shapes clouds eats a faint shape whole (rendered).
            /// </summary>
            public float Density;

            /// <summary>How far the threshold drops right at a big cloud's edge (weather units).</summary>
            public float EdgeBoost;

            /// <summary>The share of the sky they are let into (1 = everywhere).</summary>
            public float AreaShare;

            /// <summary>Their own break-up strength, or -1 for the clouds' own.</summary>
            public float Erosion;

            public Style(string name, float thickness, float density, float edgeBoost, float areaShare, float erosion)
            {
                Name = name;
                Thickness = thickness;
                Density = density;
                EdgeBoost = edgeBoost;
                AreaShare = areaShare;
                Erosion = erosion;
            }
        }

        /// <summary>
        /// By the setting's VALUE, which is what the file stores: append only, never renumber.
        /// History: the first in-game round was 150 m, full density, EdgeBoost 0.35 ("much
        /// thinner and less full", "randomize their positions more" came back).
        /// </summary>
        private static readonly Style[] Styles =
        {
            new Style("clustered", 70f, 0.35f, 0.35f, 1f,    -1f),    // 0: round the big clouds' edges
            new Style("scattered", 70f, 0.35f, 0.15f, 0.5f,  -1f),    // 1: random patches (default)
            new Style("wispy",     60f, 0.25f, 0.08f, 0.45f, 0.8f),   // 2: thinner, fainter, more torn
        };

        /// <summary>The chosen style; an unknown value (a newer file) reads as the default.</summary>
        public static Style Current
        {
            get
            {
                int value = Settings.CloudFragmentStyle != null ? Settings.CloudFragmentStyle.value : Settings.Defaults.FragmentStyle;
                if (value < 0 || value >= Styles.Length)
                    value = Settings.Defaults.FragmentStyle;
                return Styles[value];
            }
        }

        public static bool On
        {
            get { return Share > 0f; }
        }

        /// <summary>The share of the sky they cover, 0..1.</summary>
        public static float Share
        {
            get
            {
                return Settings.CloudFragments != null
                    ? Mathf.Clamp01(Settings.CloudFragments.value)
                    : Settings.Defaults.Fragments;
            }
        }

        public static string Describe()
        {
            return On ? (Share * 100f).ToString("F0", CultureInfo.InvariantCulture) + "% of the sky, " + Current.Name : "off";
        }
    }
}
