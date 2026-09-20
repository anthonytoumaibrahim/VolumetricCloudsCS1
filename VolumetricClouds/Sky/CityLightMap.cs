using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// The arithmetic behind the city's glow: buildings in, a blurred 0..1 brightness grid out.
    /// </summary>
    /// <remarks>
    /// Pure (no Unity objects), so tools/test-citylights.ps1 can feed it synthetic cities and
    /// check what comes out -- which is how the first version's calibration was caught: its
    /// "fully lit" reference was so low that a cell of ordinary houses already saturated, so a
    /// suburb glowed exactly like a downtown of towers and a whole city was one flat tint.
    ///
    /// Each building adds footprint x floors to the cell it stands in. A cell is 135 m square:
    ///   detached houses, 16 m square, 8 m tall, one per 24 m      ~ 10 700
    ///   mid-rise blocks, 32 m square, 30 m tall, one per 48 m     ~ 17 800
    ///   towers, 40 m square, 120 m tall, one per 60 m             ~ 47 000
    /// FullyLit sits above the towers, and the result goes through a gentle curve, because
    /// sky glow is not linear in lamp count: the suburb comes out near 0.35, the mid-rise
    /// district near 0.5, the towers near 0.85, and only a really dense core reaches 1.
    /// </remarks>
    public sealed class CityLightMap
    {
        /// <summary>The game's map, 9 x 9 tiles of 1920 m.</summary>
        public const float MapSize = 17280f;

        public const int Resolution = 128;

        /// <summary>The weighted footprint, in m^2, at which a cell counts as fully lit.</summary>
        public const float FullyLit = 60000f;

        /// <summary>Below 1: lifts the suburbs relative to downtown, as real sky glow does.</summary>
        private const float Curve = 0.6f;

        /// <summary>
        /// Passes of a 5-tap box blur each way: three are close to a gaussian ~330 m wide, the
        /// spread ground light has by the time it reaches a cloud base half a kilometre up.
        /// </summary>
        private const int BlurPasses = 3;

        private readonly float[] _light = new float[Resolution * Resolution];
        private readonly float[] _scratch = new float[Resolution * Resolution];

        public int Count { get; private set; }

        /// <summary>The brightest cell before blurring, 0..1: how dense the densest block is.</summary>
        public float Brightest { get; private set; }

        public void Clear()
        {
            System.Array.Clear(_light, 0, _light.Length);
            Count = 0;
            Brightest = 0f;
        }

        /// <summary>Cell index for a world position, or -1 outside the map. x -> column (u), z -> row (v).</summary>
        public static int IndexOf(float worldX, float worldZ)
        {
            int x = Mathf.FloorToInt((worldX / MapSize + 0.5f) * Resolution);
            int z = Mathf.FloorToInt((worldZ / MapSize + 0.5f) * Resolution);
            if (x < 0 || z < 0 || x >= Resolution || z >= Resolution)
                return -1;

            return z * Resolution + x;
        }

        /// <summary>Adds one lit building. False if it stands outside the map.</summary>
        public bool Add(float worldX, float worldZ, float footprint, float height)
        {
            int index = IndexOf(worldX, worldZ);
            if (index < 0)
                return false;

            // A tower is many floors of lit windows where a house is one.
            float floors = 1f + Mathf.Clamp(height, 0f, 300f) / 25f;
            _light[index] += Mathf.Max(0f, footprint) * floors;
            Count++;
            return true;
        }

        /// <summary>Normalises, curves and blurs. The returned array is this map's own; copy what you keep.</summary>
        public float[] Finish()
        {
            Brightest = 0f;
            for (int i = 0; i < _light.Length; i++)
            {
                float v = Mathf.Pow(Mathf.Clamp01(_light[i] / FullyLit), Curve);
                _light[i] = v;
                if (v > Brightest)
                    Brightest = v;
            }

            for (int pass = 0; pass < BlurPasses; pass++)
            {
                Blur(_light, _scratch, 1, 0);
                Blur(_scratch, _light, 0, 1);
            }

            // The texture clamps at its edges; a lit border would smear out to infinity.
            for (int i = 0; i < Resolution; i++)
            {
                _light[i] = 0f;
                _light[(Resolution - 1) * Resolution + i] = 0f;
                _light[i * Resolution] = 0f;
                _light[i * Resolution + Resolution - 1] = 0f;
            }

            return _light;
        }

        /// <summary>5-tap box blur along one axis, clamped at the edges.</summary>
        private static void Blur(float[] source, float[] target, int dx, int dz)
        {
            for (int z = 0; z < Resolution; z++)
            {
                for (int x = 0; x < Resolution; x++)
                {
                    float sum = 0f;
                    for (int k = -2; k <= 2; k++)
                    {
                        int sx = Mathf.Clamp(x + k * dx, 0, Resolution - 1);
                        int sz = Mathf.Clamp(z + k * dz, 0, Resolution - 1);
                        sum += source[sz * Resolution + sx];
                    }

                    target[z * Resolution + x] = sum * 0.2f;
                }
            }
        }
    }
}
