namespace VolumetricClouds.Sky
{
    /// <summary>
    /// CLOUD DETAIL's texture (1.2): a tiling 128^3 volume of "puffy" Worley noise, the fine
    /// billows <see cref="CloudDetail"/> erodes into the clouds' surfaces, with its whole mip chain.
    /// </summary>
    /// <remarks>
    /// Three octaves of Worley noise (periods 4, 8, 16; each half the weight of the one before),
    /// each cell given the round profile sqrt(1 - d^2) instead of the cone 1 - d the old G channel
    /// uses: EVE-Redux V5's "spherical" Worley, "billowy and puffy". Designed on offline renders of
    /// the shader's formula before it was built (the 500 m repeat, the three octaves, the round
    /// profile were all chosen from pictures). Made by <see cref="WorleyVolume"/>, which the Cumulus
    /// style's noise shares.
    ///
    /// A SEPARATE texture on purpose: the 64^3 noise (<see cref="CloudNoise3D"/>) and the weather
    /// field -- what every saved city's big clouds are made of -- are untouched, so a save keeps
    /// its sky and detail only sculpts its surfaces. The volume comes from the city's noise seed,
    /// so it is the same every time the city is loaded. Like the other generators, these numbers
    /// are part of what a seed reproduces (invariant 14): change one and every city's billows
    /// change; that is what <see cref="Generator"/> is for.
    ///
    /// Pure System.Math on plain arrays -- no UnityEngine calls -- so it runs on worker threads
    /// and offline in tools/test-detail.ps1. The mips are built here too: Unity 5.6 would build a
    /// 3D texture's on the main thread, and a box filter of a noise keeps its mean, which is what
    /// the shader's distance fade relies on.
    /// </remarks>
    public static class CloudDetail3D
    {
        /// <summary>Edge of the volume, in texels.</summary>
        public const int Size = 128;

        /// <summary>What this generator is. Bump it with any change to what <see cref="Generate"/> makes.</summary>
        public const int Generator = 1;

        private const int Octaves = 3;
        private const int BasePeriod = 4;
        private const float Persistence = 0.5f;

        /// <summary>The volume's seed from the city's noise seed: its own, so it matches no other noise.</summary>
        public static int SeedFor(int noiseSeed)
        {
            return unchecked(noiseSeed * 31 + 7919);
        }

        /// <summary>
        /// The mip chain, level 0 first: <paramref name="size"/>^3 bytes, then each level half the
        /// edge, down to 1^3. Index x + y * n + z * n * n. <paramref name="size"/> must be a power
        /// of two. Split over <paramref name="threads"/> worker threads (0 = one per core, up to 8);
        /// the result does not depend on how many.
        /// </summary>
        public static byte[][] Generate(int size, int seed, int threads)
        {
            return WorleyVolume.Generate(size, seed, threads, Octaves, BasePeriod, Persistence, "VolumetricClouds detail");
        }
    }
}
