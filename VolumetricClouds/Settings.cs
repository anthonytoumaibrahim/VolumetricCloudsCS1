using System;
using ColossalFramework;
using UnityEngine;

namespace VolumetricClouds
{
    /// <summary>
    /// Persisted options. Backed by the game's own settings file so values survive
    /// restarts and show up alongside vanilla settings.
    /// </summary>
    /// <remarks>
    /// Every default is a named constant in <see cref="Defaults"/> and nowhere else: the
    /// constructors below read them, the fallbacks inside the renderers read them, and
    /// <see cref="UI.SettingsCatalog"/> carries the same constant on every row, which is what
    /// lets "Reset all settings to defaults" exist at all. A Saved* does NOT remember its
    /// default -- SavedFloat has one field, the constructor's default is only its initial
    /// content, and Delete() removes the key from the file without changing what is in
    /// memory. So the catalog is the only thing that can put a default back.
    ///
    /// Editing a default does not change a machine that already has
    /// %LOCALAPPDATA%\Colossal Order\Cities_Skylines\VolumetricClouds.cgs: the value is only
    /// consulted when the key is absent. Use the Reset button (or delete that file) to see a
    /// first run.
    /// </remarks>
    public static class Settings
    {
        public const string FileName = "VolumetricClouds";

        public static SavedBool ShowInUnifiedUI { get; private set; }
        public static SavedInputKey ToggleKey { get; private set; }

        /// <summary>
        /// Off (the default): the in-game panel has the three tabs a subscriber needs -- Now,
        /// Clouds, Fog. On: it also gets every tab of the options page, so the whole mod can be
        /// tuned against the sky the way it was built. The options page always has everything;
        /// this only decides how much of it is ALSO one keypress away.
        /// </summary>
        public static SavedBool ShowAdvancedInPanel { get; private set; }

        /// <summary>
        /// Writes every periodic line to the log. Off for subscribers: each Log call is an
        /// open-write-close plus a Unity stack trace, which is fine for a ten-minute test
        /// session and wrong for a four-hour one.
        /// </summary>
        public static SavedBool DetailedLogging { get; private set; }

        /// <summary>
        /// The cloud cover the OVERRIDE asks for, 0..1. Only in effect while
        /// <see cref="CoverageOverride"/> is on; rendering reads CloudWeather.Coverage, never this.
        /// </summary>
        public static SavedFloat Coverage { get; private set; }

        /// <summary>
        /// Off (the default): the cover follows the game's weather. On: <see cref="Coverage"/>
        /// fixes it, for skies the game cannot express -- cloudy with no rain, on demand.
        /// </summary>
        public static SavedBool CoverageOverride { get; private set; }

        /// <summary>Following the weather: the cover on a clear day, 0..1.</summary>
        public static SavedFloat WeatherFairCoverage { get; private set; }

        /// <summary>Following the weather: the cover in rain or a full cloudy spell, 0..1.</summary>
        public static SavedFloat WeatherOvercastCoverage { get; private set; }

        /// <summary>
        /// Replace the game's camera-locked rain with ours: curtains under the clouds and
        /// world-anchored streaks near the camera, both only where it is raining.
        /// </summary>
        public static SavedBool RainEnabled { get; private set; }

        /// <summary>Multiplier on how dense the rain curtains under the clouds are. 0 = none.</summary>
        public static SavedFloat RainCurtains { get; private set; }

        /// <summary>Multiplier on how many streaks show near the camera. 0 = none.</summary>
        public static SavedFloat RainStreaks { get; private set; }

        /// <summary>Camera height above the ground, in metres, at which the streaks are gone.</summary>
        public static SavedFloat RainStreakHeight { get; private set; }

        /// <summary>
        /// The rain sound and wet roads follow the clouds too. The one setting that changes
        /// what the simulation sees (see SampleRainIntensityPatch).
        /// </summary>
        public static SavedBool RainLocalised { get; private set; }

        /// <summary>Lightning lights the clouds and the rain from inside, and thunder gets our storm flashes.</summary>
        public static SavedBool LightningEnabled { get; private set; }

        /// <summary>Draw our own bolt, from our cloud base, instead of the game's fixed model.</summary>
        public static SavedBool LightningReplaceBolt { get; private set; }

        /// <summary>Multiplier on the flash in the clouds and on the bolt.</summary>
        public static SavedFloat LightningBrightness { get; private set; }

        /// <summary>
        /// Off (the default): how often our visual-only lightning flashes follows the rain.
        /// On: <see cref="LightningActivity"/> sets it, e.g. for a dry thunderstorm. Either
        /// way it only ever happens inside cloud, and never starts a fire.
        /// </summary>
        public static SavedBool LightningOverride { get; private set; }

        /// <summary>
        /// 0..4: from a flash every half minute at 100% to one every 0.75 s at 400%. The key
        /// is unchanged from when it stopped at 100%: below that nothing about its meaning
        /// moved, it has only been given more room.
        /// </summary>
        public static SavedFloat LightningActivity { get; private set; }

        /// <summary>
        /// 0..4: multiplier on the activity a natural downpour produces. The Level 1 "reshape"
        /// answer to "a real storm gives too few strikes", so the override does not have to be
        /// left on for ever.
        /// </summary>
        public static SavedFloat LightningStormScale { get; private set; }

        /// <summary>
        /// Volumetric fog in the cloud pass, in place of the game's even grey-out. The game's
        /// distance haze, edge fog and pollution tint stay. OFF by default: it is the most
        /// expensive thing this mod draws.
        /// </summary>
        public static SavedBool FogEnabled { get; private set; }

        /// <summary>
        /// Off (the default): the fog follows the game's weather. On: <see cref="FogAmount"/>
        /// sets it, for OUR fog only. (Play It's fog slider writes the game's m_currentFog, which
        /// our fog follows while this is off -- and which also greys the game's own screen fog
        /// and reaches the savegame. This one is visual, and ours alone.)
        /// </summary>
        public static SavedBool FogOverride { get; private set; }

        /// <summary>0..1: the share of the map that has fog on it. 1 = everywhere.</summary>
        public static SavedFloat FogAmount { get; private set; }

        /// <summary>Multiplier on how thick the fog is inside (how far you can see in it).</summary>
        public static SavedFloat FogDensity { get; private set; }

        /// <summary>
        /// Off (the default): the fog is LEVEL, measured up from the map's sea level like the
        /// game's own fog -- valleys fill, hills stand out of it. On: it is measured from the
        /// ground under it, so the layer drapes over hills. (Until 2026-09-21 that was the only
        /// behaviour; it is liked, and it is the opt-in because level is what fog does.)
        /// </summary>
        public static SavedBool FogFollowsGround { get; private set; }

        /// <summary>
        /// Metres from the reference (sea level, or the ground) to the UNDERSIDE of the fog.
        /// 0 = it starts at the reference; 200 = fog only from 200 m up.
        /// </summary>
        public static SavedFloat FogBase { get; private set; }

        /// <summary>Metres from the underside of the fog to its top: the layer's thickness.</summary>
        public static SavedFloat FogHeight { get; private set; }

        /// <summary>0 = solid, 1 = wispy: how hard fine noise eats into it. The fog's "Break-up".</summary>
        public static SavedFloat FogBreakup { get; private set; }

        /// <summary>Multiplier on how fast the fog drifts and churns. 0 = still.</summary>
        public static SavedFloat FogSpeed { get; private set; }

        /// <summary>
        /// Multiplier on the fog's own light. Since the brightness decoupling it really is the
        /// fog's brightness: the clouds' brightness no longer reaches it.
        /// </summary>
        public static SavedFloat FogBrightness { get; private set; }

        /// <summary>-1 cool blue-grey .. 0 neutral .. +1 warm. The fog's colour, on top of the light it is in.</summary>
        public static SavedFloat FogTint { get; private set; }

        /// <summary>
        /// How much of the clouds' distance transparency is taken away at night, 0..1. At 1
        /// the stars cannot be seen through a cloud; by day this has no effect at all.
        /// </summary>
        public static SavedFloat CloudNightOpacity { get; private set; }

        /// <summary>Multiplier on the faint pale glow under the clouds at night. 0 = off.</summary>
        public static SavedFloat NightGlow { get; private set; }

        /// <summary>Multiplier on how fast cloud shadows drift.</summary>
        public static SavedFloat WindSpeed { get; private set; }

        /// <summary>
        /// World-space size of one weather tile, in metres. Shared by the shadow cookie and
        /// the raymarched clouds so both repeat at the same scale.
        /// </summary>
        public static SavedFloat WeatherTileSize { get; private set; }

        /// <summary>Clouds cast shadows on the ground.</summary>
        public static SavedBool CloudShadows { get; private set; }

        /// <summary>How much direct sunlight thick cloud removes from the ground below it, 0..1.</summary>
        public static SavedFloat CloudShadowDarkness { get; private set; }

        /// <summary>
        /// Multiplier on cloud optical depth for shadows only. Above 1, thin cloud and cloud
        /// edges cast a fuller shadow, so more of the ground reads as shaded.
        /// </summary>
        public static SavedFloat CloudShadowFullness { get; private set; }

        /// <summary>Texels per side of the shadow map. A fixed GPU cost, so it is a lever on a weak card.</summary>
        public static SavedInt ShadowMapResolution { get; private set; }

        /// <summary>How many times a second the shadow map is re-marched.</summary>
        public static SavedInt ShadowMapRate { get; private set; }

        /// <summary>
        /// 0 Low, 1 Medium, 2 High, 3 Custom. Writes the three rows above plus the step count;
        /// touching any of them by hand moves it to Custom. Describes the MACHINE, so it is
        /// never part of a shared preset.
        /// </summary>
        public static SavedInt QualityPreset { get; private set; }

        /// <summary>Raymarched clouds (needs the embedded shader bundle); off means billboards.</summary>
        public static SavedBool UseVolumetric { get; private set; }

        /// <summary>Vertical extent of the cloud layer, in metres.</summary>
        public static SavedFloat CloudThickness { get; private set; }

        /// <summary>
        /// How hard fine noise erodes the clouds: 0 leaves smooth solid masses, the 0.3
        /// default gives soft cumulus, higher tears them into ragged tufts and opens gaps.
        /// </summary>
        public static SavedFloat CloudBreakup { get; private set; }

        /// <summary>Frequency of that erosion noise: low tears off big chunks, high makes fine wisps.</summary>
        public static SavedFloat CloudBreakupScale { get; private set; }

        /// <summary>Multiplier on cloud optical density.</summary>
        public static SavedFloat CloudDensity { get; private set; }

        /// <summary>
        /// Multiplier on how brightly clouds are lit, when <see cref="CloudBrightnessAuto"/>
        /// is off. Its saved value is untouched by the curve, so switching Auto off reproduces
        /// exactly what the mod did before the curve existed.
        /// </summary>
        public static SavedFloat CloudBrightness { get; private set; }

        /// <summary>
        /// Cloud brightness follows the cloud intensity instead of sitting on one number: a
        /// scattered fair-weather sky wants far more light than a full overcast, and one
        /// value cannot be right for both.
        /// </summary>
        public static SavedBool CloudBrightnessAuto { get; private set; }

        /// <summary>Auto: the brightness at 0% intensity.</summary>
        public static SavedFloat CloudBrightnessClear { get; private set; }

        /// <summary>Auto: the brightness at 100% intensity.</summary>
        public static SavedFloat CloudBrightnessOvercast { get; private set; }

        /// <summary>Raymarch steps per pixel. Cost scales linearly.</summary>
        public static SavedFloat CloudQuality { get; private set; }

        /// <summary>Let scene geometry hide clouds behind it. Diagnostic escape hatch.</summary>
        public static SavedBool CloudDepthOcclusion { get; private set; }

        /// <summary>
        /// World illumination at 100% coverage, 0..1. Overcast, not night. Bypassed entirely
        /// while <see cref="CloudBrightnessAuto"/> is on: the curve is then the one and only
        /// coverage-to-brightness relationship the CLOUD has (the light everything else
        /// stands in still carries it).
        /// </summary>
        public static SavedFloat MinIllumination { get; private set; }

        /// <summary>Draw the visible cloud billboards.</summary>
        public static SavedBool CloudsVisible { get; private set; }

        /// <summary>Cloud layer height above sea level, in metres.</summary>
        public static SavedFloat CloudAltitude { get; private set; }

        /// <summary>How many billboard puffs to place at full coverage.</summary>
        public static SavedFloat CloudPuffCount { get; private set; }

        /// <summary>Base width of a single puff, in metres.</summary>
        public static SavedFloat CloudPuffSize { get; private set; }

        /// <summary>Take over the glow pass of batched (distant) lights. Off = untouched game.</summary>
        public static SavedBool HaloEnabled { get; private set; }

        /// <summary>
        /// Use the ported replacement shader, which adds brightness/tightness/size and cannot
        /// produce the NaN "box". Off = a copy of the game's shader with only fog overridden.
        /// </summary>
        public static SavedBool HaloReplaceShader { get; private set; }

        /// <summary>
        /// The fog amount the halos see, on the game's own scale (what PersistentFogAdjuster
        /// edits): 0 is a clear night, 1 is foggy, -0.5 is no glow at all. Independent of the
        /// world's fog.
        /// </summary>
        public static SavedFloat HaloFogAmount { get; private set; }

        /// <summary>Multiplier on glow amplitude. Replacement shader only.</summary>
        public static SavedFloat HaloBrightness { get; private set; }

        /// <summary>
        /// Multiplier on the falloff exponent: above 1 keeps the bright core and sheds the wide
        /// haze, which is what makes a halo read as small. Replacement shader only.
        /// </summary>
        public static SavedFloat HaloTightness { get; private set; }

        /// <summary>Multiplier on the glow's world radius. Replacement shader only.</summary>
        public static SavedFloat HaloRadius { get; private set; }

        /// <summary>
        /// Lamps closer to the camera than this (metres) get the three "near" multipliers below
        /// in full, fading out to none at twice the distance. A halo has a fixed world size, so
        /// what makes a distant lamp a crisp dot makes one 100 m away a blob. 0 = off.
        /// Replacement shader only.
        /// </summary>
        public static SavedFloat HaloNearLightDistance { get; private set; }

        /// <summary>Near lamps: multiplier on top of <see cref="HaloBrightness"/>.</summary>
        public static SavedFloat HaloNearLightBrightness { get; private set; }

        /// <summary>Near lamps: multiplier on top of <see cref="HaloTightness"/>.</summary>
        public static SavedFloat HaloNearLightTightness { get; private set; }

        /// <summary>Near lamps: multiplier on top of <see cref="HaloRadius"/>.</summary>
        public static SavedFloat HaloNearLightRadius { get; private set; }

        /// <summary>
        /// Halo brightness of dynamic lights that are NOT lamps: vehicles and non-batched
        /// effect lights. In place of <see cref="HaloBrightness"/>, which is tuned for lamps;
        /// tightness, size and the near multipliers are shared. Lamps drawn dynamically --
        /// Intersection Marking Tool's -- take the lamp settings.
        /// </summary>
        public static SavedFloat HaloVehicleBrightness { get; private set; }

        /// <summary>Master switch for the dynamic-light (vehicle) adjustments.</summary>
        public static SavedBool HaloAdjustEnabled { get; private set; }

        /// <summary>Multiplier on a dynamic light's range. 1 leaves the game untouched.</summary>
        public static SavedFloat HaloRangeScale { get; private set; }

        /// <summary>Multiplier on a dynamic light's brightness. 1 leaves the game untouched.</summary>
        public static SavedFloat HaloIntensityScale { get; private set; }

        /// <summary>
        /// Dynamic lights closer than this skip the volume pass. 0 disables the cutoff. Nothing
        /// to do with <see cref="HaloNearLightDistance"/>: street lamps are never dynamic.
        /// </summary>
        public static SavedFloat DynamicHaloCutoff { get; private set; }

        /// <summary>Spike aid: projects a checkerboard instead of clouds.</summary>
        public static SavedBool DebugChecker { get; private set; }

        /// <summary>
        /// Every default, in one place. Read off the author's own settings file on 2026-09-20
        /// at 20:00 (tools\read-settings.ps1), leaving out the values that file holds as
        /// experiments rather than as a look: the three overrides are all off here, the rain
        /// endpoint is a full overcast rather than its 0, and lightning activity is 50%.
        /// Re-snapshot before publishing, and read the result with a human eye.
        /// </summary>
        public static class Defaults
        {
            // ---- housekeeping ----
            public const bool ShowInUnifiedUI = true;
            public const bool ShowAdvancedInPanel = false;
            public const bool DetailedLogging = false;
            public const bool DebugChecker = false;

            // ---- the weather and the cover ----

            /// <summary>What the override asks for when it is first switched on.</summary>
            public const float Coverage = 0.91f;

            public const bool CoverageOverride = false;

            /// <summary>Following the weather: a clear day. Scattered cloud, not an empty sky.</summary>
            public const float FairCoverage = 0.47f;

            /// <summary>Following the weather: rain, or a full cloudy spell.</summary>
            public const float OvercastCoverage = 0.98f;

            public const float WindSpeed = 3.3f;
            public const float WeatherTileSize = 13500f;

            // ---- the clouds ----
            public const bool CloudsVisible = true;
            public const float Altitude = 750f;
            public const float Thickness = 650f;
            public const float Breakup = 0.3f;
            public const float BreakupScale = 6f;
            public const float Density = 3f;

            /// <summary>The manual brightness, used when the curve below is switched off.</summary>
            public const float Brightness = 0.25f;

            public const bool BrightnessAuto = true;

            /// <summary>The curve's two ends, in the same units as <see cref="Brightness"/>.</summary>
            public const float BrightnessClear = 2f;
            public const float BrightnessOvercast = 0.5f;

            /// <summary>1 = off. Only consulted when the brightness curve is off.</summary>
            public const float MinIllumination = 1f;

            public const float NightOpacity = 1f;
            public const float NightGlow = 0.9f;

            // ---- shadows ----
            public const bool CloudShadows = true;
            public const float ShadowDarkness = 0.85f;
            public const float ShadowFullness = 4f;
            public const int ShadowMapResolution = 2048;
            public const int ShadowMapRate = 20;

            // ---- rendering (the machine, never a shared preset) ----
            public const bool UseVolumetric = true;
            public const bool DepthOcclusion = true;

            /// <summary>
            /// Raymarch steps. 96 is the slider's maximum, what the author runs, and the shader's
            /// hard limit: CloudRaymarch.shader marches `for (int s = 0; s &lt; 96; s++)` while
            /// the step length is (exit - enter) / steps, so a higher number here only shortens
            /// the step and leaves the far end of every slab unmarched. Raising it means
            /// editing that loop bound first.
            /// </summary>
            public const float Quality = 96f;

            /// <summary>High. See <see cref="QualityPreset"/>.</summary>
            public const int Preset = 2;

            public const float PuffCount = 600f;
            public const float PuffSize = 900f;

            // ---- rain ----
            public const bool RainEnabled = true;
            public const float RainCurtains = 1f;
            public const float RainStreaks = 1f;
            public const float RainStreakHeight = 500f;
            public const bool RainLocalised = true;

            // ---- lightning ----
            public const bool LightningEnabled = true;
            public const bool LightningReplaceBolt = true;
            public const float LightningBrightness = 1f;
            public const bool LightningOverride = false;
            public const float LightningActivity = 0.5f;
            public const float LightningStormScale = 1f;

            // ---- fog ----

            /// <summary>
            /// OFF. The heaviest thing the mod draws, so it is the user's call; everything
            /// else about the fog ships tuned, ready for the moment they tick it.
            /// </summary>
            public const bool FogEnabled = false;

            public const bool FogOverride = false;
            public const float FogAmount = 0.22f;
            public const float FogDensity = 0.35f;
            public const bool FogFollowsGround = false;
            public const float FogBase = 0f;
            public const float FogHeight = 370f;
            public const float FogBreakup = 0.7f;
            public const float FogSpeed = 3.6f;

            /// <summary>
            /// The author's file holds 190%, but under a cloud brightness of 25% that the fog no
            /// longer borrows: after the decoupling the fog is lit through the fixed
            /// <see cref="Sky.CloudVolume.FogLightScale"/> of 0.5 instead, so half that number
            /// puts a new subscriber's fog exactly where the tuned one looks today.
            /// </summary>
            public const float FogBrightness = 0.95f;

            public const float FogTint = -0.4f;

            // ---- light halos ----

            /// <summary>
            /// OFF: a night that looks nothing like vanilla is not something to do to someone
            /// who never asked. Everything below it, though, is the author's night rather than a
            /// neutral one -- ticking the box has to DO something.
            /// </summary>
            public const bool HaloEnabled = false;

            public const bool HaloReplaceShader = true;
            public const float HaloFogAmount = 0f;
            public const float HaloBrightness = 3f;
            public const float HaloTightness = 6f;
            public const float HaloRadius = 1.2f;
            public const float HaloNearDistance = 280f;
            public const float HaloNearBrightness = 0.9f;
            public const float HaloNearTightness = 1.75f;
            public const float HaloNearRadius = 0.1f;
            public const float HaloVehicleBrightness = 2.4f;

            /// <summary>
            /// The older, cruder dynamic-light controls stay neutral. The author's file holds a
            /// range of 1.55 and an intensity of 1.4 with this switch OFF -- leftovers from an
            /// experiment, not a look.
            /// </summary>
            public const bool HaloAdjustEnabled = false;

            public const float HaloRangeScale = 1f;
            public const float HaloIntensityScale = 1f;
            public const float DynamicHaloCutoff = 0f;
        }

        private static bool _initialised;

        public static void Init()
        {
            if (_initialised)
                return;

            try
            {
                if (GameSettings.FindSettingsFileByName(FileName) == null)
                    GameSettings.AddSettingsFile(new SettingsFile { fileName = FileName });

                ShowInUnifiedUI = new SavedBool("ShowInUnifiedUI", FileName, Defaults.ShowInUnifiedUI, true);
                ToggleKey = new SavedInputKey("ToggleKey", FileName, DefaultToggleKey, true);
                ShowAdvancedInPanel = new SavedBool("ShowAdvancedInPanel", FileName, Defaults.ShowAdvancedInPanel, true);
                DetailedLogging = new SavedBool("DetailedLogging", FileName, Defaults.DetailedLogging, true);

                Coverage = new SavedFloat("Coverage", FileName, Defaults.Coverage, true);
                CoverageOverride = new SavedBool("CoverageOverride", FileName, Defaults.CoverageOverride, true);
                WeatherFairCoverage = new SavedFloat("WeatherFairCoverage", FileName, Defaults.FairCoverage, true);
                // A new key rather than "WeatherOvercastCoverage": that one is saved as 0 on the
                // one machine that has it. It was dragged to nothing while the row still lived on
                // the F4 panel, and the polish pass then moved the row to Options -> Weather,
                // where nobody looked at it again. A rain end BELOW the clear end inverts the
                // whole mapping -- the wetter the game's weather, the emptier our sky -- so
                // "Follow the game's weather" handed back a clear day in a downpour. Saved values
                // of a slider that quietly stopped being visible are garbage.
                WeatherOvercastCoverage = new SavedFloat("WeatherRainCoverage", FileName, Defaults.OvercastCoverage, true);

                RainEnabled = new SavedBool("RainEnabled", FileName, Defaults.RainEnabled, true);
                RainCurtains = new SavedFloat("RainCurtains", FileName, Defaults.RainCurtains, true);
                RainStreaks = new SavedFloat("RainStreaks", FileName, Defaults.RainStreaks, true);
                RainStreakHeight = new SavedFloat("RainStreakHeight", FileName, Defaults.RainStreakHeight, true);
                RainLocalised = new SavedBool("RainLocalised", FileName, Defaults.RainLocalised, true);

                LightningEnabled = new SavedBool("LightningEnabled", FileName, Defaults.LightningEnabled, true);
                LightningReplaceBolt = new SavedBool("LightningReplaceBolt", FileName, Defaults.LightningReplaceBolt, true);
                LightningBrightness = new SavedFloat("LightningBrightness", FileName, Defaults.LightningBrightness, true);
                LightningOverride = new SavedBool("LightningOverride", FileName, Defaults.LightningOverride, true);
                // Keeps its key although the slider now reaches 400%: below 100% every value
                // means exactly what it meant, so a saved one is still the sky it described.
                LightningActivity = new SavedFloat("LightningActivity", FileName, Defaults.LightningActivity, true);
                LightningStormScale = new SavedFloat("LightningStormScale", FileName, Defaults.LightningStormScale, true);

                // A new key rather than "FogEnabled": that one is saved as true on the one
                // machine that has it, and the default is now false because the feature costs
                // frames. Reusing it would have left the old answer standing.
                FogEnabled = new SavedBool("VolumetricFogEnabled", FileName, Defaults.FogEnabled, true);
                FogOverride = new SavedBool("FogOverride", FileName, Defaults.FogOverride, true);
                FogAmount = new SavedFloat("FogAmount", FileName, Defaults.FogAmount, true);
                // New keys throughout, because the third fog means different things by them:
                // density multiplies an extinction nearly four times higher than "FogThickness"
                // did, and height is now the TOP of the layer above the ground where "FogHeight"
                // was an e-folding scale above a fixed level. "FogPatchiness" and "FogBanks"
                // (a blanket/banks blend nobody understood) are gone.
                // "FogDensityScale", not "FogDensity": 100% used to be an extinction of 0.028 /m,
                // which was judged to "just look like the clouds but at the terrain level".
                // 100% is now a third of that, so a value saved under the old scale is wrong.
                FogDensity = new SavedFloat("FogDensityScale", FileName, Defaults.FogDensity, true);
                // "FogTopHeight" keeps its key although it is now the layer's THICKNESS: with the
                // base at 0 (the default, and what every saved file has) the two are the same
                // number. Thickness rather than "top" so that no pair of sliders can ask for a
                // top below the base, which would be a fog row that does nothing.
                FogFollowsGround = new SavedBool("FogFollowsGround", FileName, Defaults.FogFollowsGround, true);
                FogBase = new SavedFloat("FogBaseHeight", FileName, Defaults.FogBase, true);
                FogHeight = new SavedFloat("FogTopHeight", FileName, Defaults.FogHeight, true);
                // Keeps its key: what it means has not changed, it has only stopped ALSO
                // carrying the clouds' brightness. See CloudVolume.FogLightScale.
                FogBrightness = new SavedFloat("FogBrightness", FileName, Defaults.FogBrightness, true);
                FogTint = new SavedFloat("FogTint", FileName, Defaults.FogTint, true);
                FogBreakup = new SavedFloat("FogBreakup", FileName, Defaults.FogBreakup, true);
                FogSpeed = new SavedFloat("FogSpeed", FileName, Defaults.FogSpeed, true);

                CloudNightOpacity = new SavedFloat("CloudNightOpacity", FileName, Defaults.NightOpacity, true);
                // Not "CityGlow": that scaled an orange projection of the city's buildings. This
                // is an even pale luminance, and a much smaller one.
                NightGlow = new SavedFloat("NightCloudGlow", FileName, Defaults.NightGlow, true);
                WindSpeed = new SavedFloat("WindSpeed", FileName, Defaults.WindSpeed, true);
                // New key rather than reusing CookieTileSize: the old 2000 m default was saved
                // into existing settings files and is far too small for cloud-sized features.
                WeatherTileSize = new SavedFloat("WeatherTileSize", FileName, Defaults.WeatherTileSize, true);
                CloudShadows = new SavedBool("CloudShadows", FileName, Defaults.CloudShadows, true);
                // New keys rather than reusing CloudShadowStrength: that slider had almost no
                // effect at high coverage, so saved values of it are not meaningful.
                CloudShadowDarkness = new SavedFloat("CloudShadowDarkness", FileName, Defaults.ShadowDarkness, true);
                CloudShadowFullness = new SavedFloat("CloudShadowFullness", FileName, Defaults.ShadowFullness, true);
                ShadowMapResolution = new SavedInt("ShadowMapResolution", FileName, Defaults.ShadowMapResolution, true);
                ShadowMapRate = new SavedInt("ShadowMapRate", FileName, Defaults.ShadowMapRate, true);
                QualityPreset = new SavedInt("QualityPreset", FileName, Defaults.Preset, true);
                UseVolumetric = new SavedBool("UseVolumetric", FileName, Defaults.UseVolumetric, true);
                CloudThickness = new SavedFloat("CloudThickness", FileName, Defaults.Thickness, true);
                CloudDensity = new SavedFloat("CloudDensity", FileName, Defaults.Density, true);
                CloudBreakup = new SavedFloat("CloudBreakup", FileName, Defaults.Breakup, true);
                CloudBreakupScale = new SavedFloat("CloudBreakupScale", FileName, Defaults.BreakupScale, true);
                CloudBrightness = new SavedFloat("CloudBrightness", FileName, Defaults.Brightness, true);
                CloudBrightnessAuto = new SavedBool("CloudBrightnessAuto", FileName, Defaults.BrightnessAuto, true);
                CloudBrightnessClear = new SavedFloat("CloudBrightnessClear", FileName, Defaults.BrightnessClear, true);
                CloudBrightnessOvercast = new SavedFloat("CloudBrightnessOvercast", FileName, Defaults.BrightnessOvercast, true);
                CloudQuality = new SavedFloat("CloudQuality", FileName, Defaults.Quality, true);
                CloudDepthOcclusion = new SavedBool("CloudDepthOcclusion", FileName, Defaults.DepthOcclusion, true);
                MinIllumination = new SavedFloat("MinIllumination", FileName, Defaults.MinIllumination, true);

                CloudsVisible = new SavedBool("CloudsVisible", FileName, Defaults.CloudsVisible, true);
                CloudAltitude = new SavedFloat("CloudAltitude", FileName, Defaults.Altitude, true);
                CloudPuffCount = new SavedFloat("CloudPuffCount", FileName, Defaults.PuffCount, true);
                CloudPuffSize = new SavedFloat("CloudPuffSize", FileName, Defaults.PuffSize, true);

                // All new keys. The old HaloFogEnabled/HaloFogValue belonged to an experiment where
                // values like -5 were normal; here -5 would simply mean "no glow", and an old
                // "enabled" must not switch on a replacement shader nobody has opted into.
                HaloEnabled = new SavedBool("HaloEnabled", FileName, Defaults.HaloEnabled, true);
                HaloReplaceShader = new SavedBool("HaloReplaceShader", FileName, Defaults.HaloReplaceShader, true);
                HaloFogAmount = new SavedFloat("HaloFogAmount", FileName, Defaults.HaloFogAmount, true);
                HaloBrightness = new SavedFloat("HaloBrightness", FileName, Defaults.HaloBrightness, true);
                HaloTightness = new SavedFloat("HaloTightness", FileName, Defaults.HaloTightness, true);
                HaloRadius = new SavedFloat("HaloRadius", FileName, Defaults.HaloRadius, true);

                HaloNearLightDistance = new SavedFloat("HaloNearLightDistance", FileName, Defaults.HaloNearDistance, true);
                HaloNearLightBrightness = new SavedFloat("HaloNearLightBrightness", FileName, Defaults.HaloNearBrightness, true);
                HaloNearLightTightness = new SavedFloat("HaloNearLightTightness", FileName, Defaults.HaloNearTightness, true);
                HaloNearLightRadius = new SavedFloat("HaloNearLightRadius", FileName, Defaults.HaloNearRadius, true);

                HaloVehicleBrightness = new SavedFloat("HaloVehicleBrightness", FileName, Defaults.HaloVehicleBrightness, true);

                HaloAdjustEnabled = new SavedBool("HaloAdjustEnabled", FileName, Defaults.HaloAdjustEnabled, true);
                HaloRangeScale = new SavedFloat("HaloRangeScale", FileName, Defaults.HaloRangeScale, true);
                HaloIntensityScale = new SavedFloat("HaloIntensityScale", FileName, Defaults.HaloIntensityScale, true);
                DynamicHaloCutoff = new SavedFloat("HaloNearDistance", FileName, Defaults.DynamicHaloCutoff, true);

                // There is deliberately no DebugSkipDrawLight any more. It suppressed every
                // dynamic light, was saved as true during one experiment, and then lost its
                // checkbox when the settings moved into the panel -- so it stayed on, unseen,
                // hiding vehicle lights for every session after. A debug switch that can
                // change the picture must never outlive its UI.
                DebugChecker = new SavedBool("DebugChecker", FileName, Defaults.DebugChecker, true);

                _initialised = true;
            }
            catch (Exception e)
            {
                Debug.LogError("[VolumetricClouds] Could not initialise settings.");
                Debug.LogException(e);
            }
        }

        /// <summary>F4. A property because SavedInputKey.Encode is a method, so it cannot be a const.</summary>
        public static int DefaultToggleKey
        {
            get { return SavedInputKey.Encode(KeyCode.F4, false, false, false); }
        }

        /// <summary>
        /// How brightly the clouds are lit right now: the curve if Auto is on, the slider
        /// otherwise. The curve's endpoints are positions on the same scale as the slider, so
        /// switching Auto off changes only which number is used.
        /// </summary>
        public static float EffectiveBrightness(float coverage)
        {
            if (CloudBrightnessAuto == null || !CloudBrightnessAuto.value)
                return CloudBrightness != null ? CloudBrightness.value : Defaults.Brightness;

            float clear = CloudBrightnessClear != null ? CloudBrightnessClear.value : Defaults.BrightnessClear;
            float overcast = CloudBrightnessOvercast != null ? CloudBrightnessOvercast.value : Defaults.BrightnessOvercast;
            return BrightnessCurve(clear, overcast, coverage);
        }

        /// <summary>True while the brightness curve is driving the clouds.</summary>
        public static bool BrightnessAuto
        {
            get { return CloudBrightnessAuto != null && CloudBrightnessAuto.value; }
        }

        /// <summary>
        /// Whether shadows are being cast right now: the shadow switch AND "Show clouds". A
        /// shadow with no cloud above it is a dark patch nothing explains, so hiding the clouds
        /// takes their shadows with them, and showing them again brings the shadows back. The
        /// saved shadow switch is never touched. Everything that casts, marches or reads the
        /// shadows asks this, not <see cref="CloudShadows"/>.
        /// </summary>
        public static bool ShadowsCast
        {
            get
            {
                return (CloudShadows == null || CloudShadows.value)
                    && (CloudsVisible == null || CloudsVisible.value);
            }
        }

        /// <summary>
        /// The curve itself: pure, so it can be exercised offline. Straight in slider
        /// coverage, which is not linear in sky covered (the weather texture is gamma-decoded,
        /// see CLAUDE.md) -- if the middle ever looks too dim for how little cloud there is,
        /// bend this rather than pulling the endpoints apart.
        /// </summary>
        public static float BrightnessCurve(float clear, float overcast, float coverage)
        {
            return Mathf.Lerp(clear, overcast, Mathf.Clamp01(coverage));
        }
    }
}
