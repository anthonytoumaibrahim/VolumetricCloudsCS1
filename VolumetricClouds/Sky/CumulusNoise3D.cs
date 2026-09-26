namespace VolumetricClouds.Sky
{
    /// <summary>
    /// The CUMULUS style's noise (1.2): the one texture every one of its clouds is carved from.
    /// A tiling 128^3 cube of eight octaves of "spherical" Worley noise with its mip chain --
    /// EVE-Redux V5's own (blackrack's KSP clouds, NoiseWrapper.cs defaults: 8 octaves, period 1,
    /// lacunarity 2, persistence 0.57, spherical 1; min/max normalised), tiled every
    /// <see cref="CloudStyle.NoiseTile"/> metres.
    /// </summary>
    /// <remarks>
    /// Its first octave is ONE round cell per repeat and the last is a texel wide, so it holds
    /// every size of billow from a whole cloud down to the smallest puff. The Cumulus style
    /// carves its clouds out of it (<see cref="CloudStyle"/>); the weather map only says where
    /// they gather. From the city's noise seed, so a city keeps its clouds; part of what a seed
    /// reproduces (invariant 14) -- <see cref="Generator"/> says which numbers made it.
    /// Pure System.Math (<see cref="WorleyVolume"/>): worker threads and offline tests.
    /// </remarks>
    public static class CumulusNoise3D
    {
        /// <summary>Edge of the volume, in texels (EVE's baseNoiseDimension).</summary>
        public const int Size = 128;

        /// <summary>What this generator is. Bump it with any change to what <see cref="Generate"/> makes.</summary>
        public const int Generator = 1;

        private const int Octaves = 8;
        private const int BasePeriod = 1;
        private const float Persistence = 0.57f;

        /// <summary>The volume's seed from the city's noise seed: its own, so it matches no other noise.</summary>
        public static int SeedFor(int noiseSeed)
        {
            return unchecked(noiseSeed * 37 + 104729);
        }

        /// <summary>
        /// The mip chain, level 0 first (see <see cref="WorleyVolume.Generate"/>). Split over
        /// <paramref name="threads"/> worker threads (0 = one per core, up to 8); the result does
        /// not depend on how many.
        /// </summary>
        public static byte[][] Generate(int size, int seed, int threads)
        {
            return WorleyVolume.Generate(size, seed, threads, Octaves, BasePeriod, Persistence, "VolumetricClouds cumulus");
        }
    }
}
