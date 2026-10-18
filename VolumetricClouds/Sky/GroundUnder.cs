using System;
using ColossalFramework;
using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// What the game knows about a spot under a funnel (1.5.0): the height of its ground or water,
    /// how deep the water is, and what kind of ground it is (<see cref="GroundLook.Classify"/>).
    /// Read only, main thread, a few times a second at most.
    /// </summary>
    public static class GroundUnder
    {
        private static bool _loggedError;

        /// <summary>The surface at p: the water's when there is water, else the ground's.</summary>
        public static float Surface(Vector3 p)
        {
            if (!Singleton<TerrainManager>.exists)
                return 0f;

            return Singleton<TerrainManager>.instance.SampleRawHeightSmoothWithWater(p, false, 0f);
        }

        /// <summary>How deep the water over p is, metres (0 on land).</summary>
        public static float WaterDepth(Vector3 p)
        {
            if (!Singleton<TerrainManager>.exists)
                return 0f;

            TerrainManager terrain = Singleton<TerrainManager>.instance;
            return Mathf.Max(0f, terrain.SampleRawHeightSmoothWithWater(p, false, 0f) - terrain.SampleRawHeightSmooth(p));
        }

        /// <summary>A winter map: the game's rain is snow, and so is what a funnel throws up on land.</summary>
        public static bool Winter
        {
            get
            {
                WeatherManager weather = Singleton<WeatherManager>.exists ? Singleton<WeatherManager>.instance : null;
                return weather != null && weather.m_properties != null && weather.m_properties.m_rainIsSnow;
            }
        }

        /// <summary>The kind of ground at p. Dust when the game cannot say.</summary>
        public static GroundKind At(Vector3 p)
        {
            try
            {
                int sand = 0, shore = 0, forest = 0, trees = 0, fertility = 0;
                if (Singleton<NaturalResourceManager>.exists)
                {
                    NaturalResourceManager.ResourceCell[] cells = Singleton<NaturalResourceManager>.instance.m_naturalResources;
                    const int Resolution = NaturalResourceManager.RESOURCEGRID_RESOLUTION;
                    const float CellSize = NaturalResourceManager.RESOURCEGRID_CELL_SIZE;
                    int x = Mathf.Clamp((int)(p.x / CellSize + Resolution * 0.5f), 0, Resolution - 1);
                    int z = Mathf.Clamp((int)(p.z / CellSize + Resolution * 0.5f), 0, Resolution - 1);
                    if (cells != null && cells.Length >= Resolution * Resolution)
                    {
                        NaturalResourceManager.ResourceCell cell = cells[z * Resolution + x];
                        sand = cell.m_sand;
                        shore = cell.m_shore;
                        forest = cell.m_forest;
                        trees = cell.m_tree;
                        fertility = cell.m_fertility;
                    }
                }

                int pavement = 0, gravel = 0, field = 0;
                if (Singleton<TerrainManager>.exists)
                {
                    TerrainManager.SurfaceCell surface = Singleton<TerrainManager>.instance.SampleDetailSurface(p);
                    pavement = Mathf.Max(surface.m_pavementA, surface.m_pavementB);
                    gravel = surface.m_gravel;
                    field = surface.m_field;
                }

                return GroundLook.Classify(WaterDepth(p), Winter, sand, shore, forest, trees, fertility, pavement, gravel, field);
            }
            catch (Exception e)
            {
                if (!_loggedError)
                {
                    _loggedError = true;
                    Log.Msg("waterspouts: the ground under one could not be read (" + e.GetType().Name + ": " + e.Message +
                            "); dust from now on");
                }

                return GroundKind.Dust;
            }
        }

        /// <summary>
        /// The nearest water to p within <paramref name="reach"/> metres, looked for on rings of eight
        /// directions; false if none. For the waterspouts that form over land near the shore.
        /// </summary>
        public static bool WaterNear(Vector3 p, float reach)
        {
            for (float r = reach * 0.25f; r <= reach + 1f; r += reach * 0.25f)
            {
                for (int k = 0; k < 8; k++)
                {
                    float a = k * (Mathf.PI / 4f);
                    if (WaterDepth(p + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r)) >= GroundLook.WaterDepth)
                        return true;
                }
            }

            return false;
        }
    }
}
