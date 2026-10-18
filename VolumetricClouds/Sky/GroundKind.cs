using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// What lies under a funnel (1.5.0): what its skirt and flecks are made of. The values are
    /// stored nowhere: append freely.
    /// </summary>
    public enum GroundKind
    {
        Dust = 0,
        Water = 1,
        Sand = 2,
        Forest = 3,
        Field = 4,
        City = 5,
        Snow = 6,
    }

    /// <summary>
    /// What a funnel throws up from each kind of ground (1.5.0; the author: "moving leaves around,
    /// sand, dust, etc. ... over water we can make the water splash"). Pure: the game's data is read
    /// by <see cref="GroundUnder"/>, which hands the numbers to <see cref="Classify"/>.
    /// </summary>
    public static class GroundLook
    {
        /// <summary>A cell's byte (0..255) from this up counts as that kind of ground.</summary>
        public const int Enough = 48;

        /// <summary>Water this deep (metres) is water; shallower is shore.</summary>
        public const float WaterDepth = 1f;

        /// <summary>
        /// The kind of ground from what the game knows about a spot: the water's depth over it, a
        /// winter map, its natural resources (NaturalResourceManager: sand, shore, forest, trees,
        /// fertile land; 0..255) and the surface painted on the terrain (TerrainManager: pavement,
        /// gravel, fields; 0..255). The first that applies, in that order.
        /// </summary>
        public static GroundKind Classify(float waterDepth, bool winter, int sand, int shore, int forest, int trees,
                                          int fertility, int pavement, int gravel, int field)
        {
            if (waterDepth >= WaterDepth)
                return GroundKind.Water;
            if (winter)
                return GroundKind.Snow;
            if (pavement >= Enough || gravel >= Enough)
                return GroundKind.City;
            if (sand >= Enough || shore >= Enough)
                return GroundKind.Sand;
            if (forest >= Enough || trees >= Enough)
                return GroundKind.Forest;
            if (field >= Enough || fertility >= Enough)
                return GroundKind.Field;
            return GroundKind.Dust;
        }

        /// <summary>The skirt's colour, a linear albedo: white spray and snow, sand, dark forest litter, earth, grey grit.</summary>
        public static Color SkirtColor(GroundKind kind)
        {
            switch (kind)
            {
                case GroundKind.Water: return new Color(0.80f, 0.82f, 0.85f);
                case GroundKind.Snow: return new Color(0.85f, 0.87f, 0.90f);
                case GroundKind.Sand: return new Color(0.55f, 0.45f, 0.30f);
                case GroundKind.Forest: return new Color(0.20f, 0.22f, 0.13f);
                case GroundKind.Field: return new Color(0.38f, 0.30f, 0.20f);
                case GroundKind.City: return new Color(0.40f, 0.38f, 0.36f);
                default: return new Color(0.42f, 0.35f, 0.26f);
            }
        }

        /// <summary>How much of the skirt is a ring (spray thrown out round a waterspout's foot) rather than a full whirl (dust).</summary>
        public static float SkirtRing(GroundKind kind)
        {
            return kind == GroundKind.Water ? 0.85f : 0.3f;
        }

        /// <summary>The flecks' colour: the skirt's, a little stronger (a leaf is darker than the dust it flies in, spray whiter).</summary>
        public static Color FleckColor(GroundKind kind)
        {
            switch (kind)
            {
                case GroundKind.Water: return new Color(0.88f, 0.90f, 0.92f);
                case GroundKind.Snow: return new Color(0.92f, 0.93f, 0.95f);
                case GroundKind.Sand: return new Color(0.50f, 0.40f, 0.26f);
                case GroundKind.Forest: return new Color(0.16f, 0.20f, 0.07f);
                case GroundKind.Field: return new Color(0.42f, 0.34f, 0.18f);
                case GroundKind.City: return new Color(0.34f, 0.32f, 0.30f);
                default: return new Color(0.36f, 0.29f, 0.20f);
            }
        }

        /// <summary>
        /// How the flecks fly and look (FunnelDebris.shader's _FleckLook): x = their size in metres,
        /// y = how long they are along their flight (streaks) over their width, z = how much they
        /// flutter (leaves turning), w = how high they rise, as a share of the skirt's height.
        /// </summary>
        public static Vector4 FleckLook(GroundKind kind)
        {
            switch (kind)
            {
                // Spray: soft droplets a little drawn out, not streaks (2026-10-18: "too defined").
                case GroundKind.Water: return new Vector4(0.7f, 1.4f, 0f, 1.4f);
                case GroundKind.Snow: return new Vector4(0.25f, 1.2f, 0.3f, 2.5f);
                case GroundKind.Sand: return new Vector4(0.2f, 3f, 0f, 1.6f);
                case GroundKind.Forest: return new Vector4(0.5f, 1f, 1f, 3f);
                case GroundKind.Field: return new Vector4(0.35f, 1.5f, 0.6f, 2.2f);
                case GroundKind.City: return new Vector4(0.6f, 1f, 0.8f, 2.5f);
                default: return new Vector4(0.25f, 2f, 0.2f, 1.8f);
            }
        }

        /// <summary>How opaque the flecks are: spray a faint mist, leaves and grit solid things.</summary>
        public static float FleckOpacity(GroundKind kind)
        {
            switch (kind)
            {
                case GroundKind.Water: return 0.3f;
                case GroundKind.Snow: return 0.55f;
                case GroundKind.Sand: return 0.45f;
                case GroundKind.Forest: return 0.85f;
                case GroundKind.Field: return 0.6f;
                case GroundKind.City: return 0.75f;
                default: return 0.5f;
            }
        }

        /// <summary>For the log.</summary>
        public static string Describe(GroundKind kind)
        {
            switch (kind)
            {
                case GroundKind.Water: return "water (spray)";
                case GroundKind.Snow: return "snow";
                case GroundKind.Sand: return "sand";
                case GroundKind.Forest: return "forest (leaves)";
                case GroundKind.Field: return "fields (dust and chaff)";
                case GroundKind.City: return "city (grit)";
                default: return "dust";
            }
        }
    }
}
