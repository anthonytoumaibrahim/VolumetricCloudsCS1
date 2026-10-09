namespace VolumetricClouds.Sky
{
    /// <summary>
    /// How the fog looks, as four shares of the room its sliders give it (1.5.0). The sliders --
    /// "Fog starts at", "Fog height", "Fog density", "Fog break-up" -- are the MOST fog there can
    /// be (the author's call: "Ceiling"); a look only ever takes it lower, thinner or wispier.
    /// </summary>
    /// <remarks>
    /// Pure, no Unity types: tools/test-fogoptions.ps1 compiles this file on its own.
    /// </remarks>
    public struct FogLook
    {
        /// <summary>The layer's underside: 0 = at "Fog starts at", 1 = at the top of "Fog height".</summary>
        public float Bottom;

        /// <summary>The layer's top on the same scale, Bottom..1.</summary>
        public float Top;

        /// <summary>Its density as a share of "Fog density", 0..1.</summary>
        public float Density;

        /// <summary>How far its break-up has gone from "Fog break-up" towards fully wispy, 0..1.</summary>
        public float Wisp;

        public FogLook(float bottom, float top, float density, float wisp)
        {
            Bottom = bottom;
            Top = top;
            Density = density;
            Wisp = wisp;
        }

        /// <summary>
        /// The sliders' own fog, exactly: every effective value works out to its slider bit for bit
        /// (B + 0 x H, (1 - 0) x H, D x 1, K + (1 - K) x 0). With the three rows at 0% this is the only
        /// look there is, which is what keeps an update from changing anyone's fog.
        /// </summary>
        public static readonly FogLook Neutral = new FogLook(0f, 1f, 1f, 0f);

        public bool IsNeutral
        {
            get { return Bottom == 0f && Top == 1f && Density == 1f && Wisp == 0f; }
        }

        /// <summary>
        /// A share <paramref name="k"/> of the way to <paramref name="target"/>, each part landing
        /// on it once within 0.0005 (as the amount does): a look easing back to neutral gets there.
        /// </summary>
        public FogLook Towards(FogLook target, float k)
        {
            return new FogLook(Follow(Bottom, target.Bottom, k), Follow(Top, target.Top, k),
                               Follow(Density, target.Density, k), Follow(Wisp, target.Wisp, k));
        }

        private static float Follow(float value, float target, float k)
        {
            float next = value + (target - value) * k;
            return System.Math.Abs(target - next) < 0.0005f ? target : next;
        }
    }

    /// <summary>
    /// The three reasons for fog besides the weather's own amount, and the look each gives it
    /// (1.5.0; a player asked for fog whose density changes "after rain ... at autumn nights ...
    /// lowered if the sun shines again", not "a fixed density of fog moving around").
    /// </summary>
    /// <remarks>
    /// Each source has an amount (share of the map) and a look. The fog drawn takes the largest
    /// amount and the amount-weighted mean of the looks (<see cref="Blend"/>), so a source fading
    /// out hands over smoothly. Every look stays inside the sliders' room, and so does any mean of
    /// them. Hours are the VISUAL clock (DayNightProperties.m_TimeOfDay: sunrise 6h, sunset 18h on
    /// every map). The numbers are starting points, to be tuned in-game.
    /// </remarks>
    public static class FogSources
    {
        // ---- the weather's fog (game or override), with "Fog changes with the weather" ----

        /// <summary>Density share of the smallest spell at 100%; a full spell keeps all of it.</summary>
        public const float SmallSpellDensity = 0.5f;

        /// <summary>How much thinner, lower and wispier the afternoon sun makes it, at 100%.</summary>
        public const float AfternoonThinning = 0.4f;
        public const float AfternoonLowering = 0.3f;
        public const float AfternoonWisp = 0.5f;

        // ---- morning fog ----

        /// <summary>The overnight layer fills the lower 40% of the room, at full density.</summary>
        public const float MorningTop = 0.4f;

        /// <summary>Lifting: the slab rises by this share of the room, and thins and frays this much.</summary>
        public const float LiftRise = 0.6f;
        public const float LiftThinning = 0.5f;
        public const float LiftWisp = 0.8f;

        public const float FormingStart = 21f;
        public const float FormedAt = 3f;
        public const float LiftStart = 6.5f;
        public const float LiftEnd = 10f;

        /// <summary>The least of the slider a morning gets; the rest is that morning's luck.</summary>
        public const float LeastMorning = 0.6f;

        // ---- mist after rain ----

        /// <summary>Mist: the whole height, a third of the density, half way to wispy.</summary>
        public static readonly FogLook MistLook = new FogLook(0f, 1f, 0.3f, 0.5f);

        /// <summary>Ground wetness at which mist starts, and from which it is all there.</summary>
        public const float MistWetStart = 0.05f;
        public const float MistWetFull = 0.6f;

        /// <summary>Rain at which there is no mist at all (it is "after" rain).</summary>
        public const float MistRainEnd = 0.1f;

        /// <summary>
        /// 0..1: how much of the afternoon it is. 0 from 19:00 to 08:00, rising to 1 by noon, 1
        /// until 16:00, falling to 0 by 19:00.
        /// </summary>
        public static float Afternoon(float hour)
        {
            float h = Wrap(hour);
            if (h < 8f || h >= 19f)
                return 0f;
            if (h < 12f)
                return Smooth((h - 8f) / 4f);
            if (h < 16f)
                return 1f;
            return 1f - Smooth((h - 16f) / 3f);
        }

        /// <summary>
        /// The weather's fog's look. <paramref name="change"/> is "Fog changes with the weather"
        /// (0..1; 0 = the sliders, exactly), <paramref name="amount"/> the weather's fog now (a
        /// small spell is lighter than a big one). With the day/night cycle off the clock is
        /// parked at about 11:08, so the time of day plays no part then.
        /// </summary>
        public static FogLook WeatherLook(float change, float amount, float hour, bool dayNight)
        {
            float w = Clamp01(change);
            float day = dayNight ? Afternoon(hour) : 0f;
            float spell = SmallSpellDensity + (1f - SmallSpellDensity) * Clamp01(amount);

            return new FogLook(0f,
                               1f - w * AfternoonLowering * day,
                               1f - w * (1f - spell * (1f - AfternoonThinning * day)),
                               w * AfternoonWisp * day);
        }

        /// <summary>
        /// 0..1, the share of the morning's peak there is now: forming 21:00 -> 03:00, whole until
        /// 06:30, lifting until 10:00, none from then until 21:00. Continuous through midnight.
        /// </summary>
        public static float MorningShare(float hour)
        {
            float h = Wrap(hour);
            if (h >= FormingStart)
                return Smooth((h - FormingStart) / (24f - FormingStart + FormedAt));
            if (h < FormedAt)
                return Smooth((h + 24f - FormingStart) / (24f - FormingStart + FormedAt));
            if (h < LiftStart)
                return 1f;
            if (h < LiftEnd)
                return 1f - MorningLift(h);
            return 0f;
        }

        /// <summary>0..1, how far the morning fog has lifted: 0 overnight, 1 from 10:00 until it forms again at 21:00.</summary>
        public static float MorningLift(float hour)
        {
            float h = Wrap(hour);
            if (h < LiftStart || h >= FormingStart)
                return 0f;
            return Smooth((h - LiftStart) / (LiftEnd - LiftStart));
        }

        /// <summary>
        /// Overnight a low, dense layer in the lower 40% of the room; lifting, the same slab rises to
        /// the top of the room as it thins and frays: fog becoming a low deck, then nothing.
        /// </summary>
        public static FogLook MorningLook(float hour)
        {
            float u = MorningLift(hour);
            return new FogLook(LiftRise * u, MorningTop + LiftRise * u, 1f - LiftThinning * u, LiftWisp * u);
        }

        /// <summary>
        /// Which morning an hour belongs to: the day/night cycle's number (it turns over at
        /// midnight), counted on from noon so the evening that forms a fog and the morning it lifts
        /// on agree on how much of it there is.
        /// </summary>
        public static uint MorningKey(uint cycle, float hour)
        {
            return Wrap(hour) >= 12f ? cycle + 1u : cycle;
        }

        /// <summary>LeastMorning..1: how foggy this morning is, as a share of the slider. Some mornings bring more.</summary>
        public static float MorningPeak(uint key)
        {
            uint x = key * 0x9E3779B9u + 0x7F4A7C15u;
            x ^= x >> 16;
            x *= 0x7FEB352Du;
            x ^= x >> 15;
            x *= 0x846CA68Bu;
            x ^= x >> 16;
            return LeastMorning + (1f - LeastMorning) * ((x & 0xFFFFFFu) / 16777216f);
        }

        /// <summary>
        /// 0..1, the share of the slider's mist there is now: none while it rains, rising as the rain
        /// ends, whole while the ground is more than 60% wet, gone by 5%. None on a map where the rain
        /// is snow.
        /// </summary>
        public static float MistShare(float wetness, float rain, bool snow)
        {
            if (snow)
                return 0f;

            return SmoothStep(MistWetStart, MistWetFull, wetness) * (1f - SmoothStep(0f, MistRainEnd, rain));
        }

        /// <summary>
        /// The look of all the fog: each source's look weighted by its amount. With no morning fog and
        /// no mist it is the weather's look itself, untouched by any arithmetic.
        /// </summary>
        public static FogLook Blend(float weather, FogLook weatherLook, float morning, FogLook morningLook, float mist, FogLook mistLook)
        {
            float a = weather > 0f ? weather : 0f;
            float b = morning > 0f ? morning : 0f;
            float c = mist > 0f ? mist : 0f;

            if (b <= 0f && c <= 0f)
                return weatherLook;

            float sum = a + b + c;
            return new FogLook((a * weatherLook.Bottom + b * morningLook.Bottom + c * mistLook.Bottom) / sum,
                               (a * weatherLook.Top + b * morningLook.Top + c * mistLook.Top) / sum,
                               (a * weatherLook.Density + b * morningLook.Density + c * mistLook.Density) / sum,
                               (a * weatherLook.Wisp + b * morningLook.Wisp + c * mistLook.Wisp) / sum);
        }

        private static float Wrap(float hour)
        {
            float h = hour % 24f;
            return h < 0f ? h + 24f : h;
        }

        private static float Clamp01(float x)
        {
            return x < 0f ? 0f : (x > 1f ? 1f : x);
        }

        private static float Smooth(float t)
        {
            t = Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private static float SmoothStep(float edge0, float edge1, float x)
        {
            return Smooth((x - edge0) / (edge1 - edge0));
        }
    }
}
