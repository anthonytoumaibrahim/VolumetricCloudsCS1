using System.Collections.Generic;
using System.Reflection;
using ColossalFramework;
using UnityEngine;
using VolumetricClouds.Sky;

namespace VolumetricClouds.Lighting
{
    /// <summary>
    /// Takes over the glow ("halo") pass of the game's batched lights.
    /// </summary>
    /// <remarks>
    /// Street and building lamps are batched at EVERY distance, not only far away (read from
    /// the IL, after believing otherwise): LightEffect.RenderEffect returns at once for a
    /// batched light that belongs to an instance, and LightEffect.PopulateGroupData raises the
    /// layer's maxRenderDistance to 30 km while never touching maxInstanceDistance, so
    /// RenderGroup.Render never trades the mesh for instances. A lamp 20 m from the camera is
    /// drawn by the same mesh layer, and so by the same replacement material, as one 5 km
    /// away -- which is why the near/far distinction is made inside the shader, per light.
    ///
    /// Those meshes are drawn by RenderGroup.RenderMesh / MegaRenderGroup.RenderMesh. Each
    /// light layer is drawn TWICE, with two materials that are read at draw time every frame:
    ///   m_bufferMaterial (the "Batched lights" CommandBuffer) = the surface illumination
    ///   m_renderMaterial (Graphics.DrawMesh)                  = the halo, 'GroupVolume'
    /// This class swaps m_renderMaterial for a material of its own. The game's materials are
    /// never modified, so switching the feature off is exact: put the references back.
    ///
    /// The swap is made at the SOURCE too: LightSystem.m_lightMaterialVolumeGroup and
    /// m_lightFloatingMaterialVolumeGroup, the two fields RenderGroup.UpdateMesh (and the mega
    /// variant) copy into a light layer whenever it rebuilds. A layer is rebuilt when anything
    /// in its 384 m cell changes -- a building finished, upgraded, zoned -- and the rebuild runs
    /// INSIDE the game's draw: RenderManager.LateUpdate -> RenderGroup.Render finds the layer
    /// dirty, UpdateMesh writes the material, and RenderMesh draws with it in the same call. A
    /// per-frame swap of the layers alone therefore always lost that frame: the cell's lamps
    /// flashed the game's big soft halos for one frame per rebuild (reported by a player in
    /// 1.0.0; `swapsSinceLastLog` counted 3-25 such layers per 5 s while the city grew). Nothing
    /// else in the game reads those two fields (IL: InitializeProperties creates them,
    /// DestroyProperties destroys them), and no installed mod names them.
    ///
    /// Why a replacement shader rather than feeding the game's one: disassembled, its glow is
    /// 0.001 * (fog + 0.5) * (1/x - 1), raised to a power via exp(log()). Fog is therefore
    /// only a brightness knob, there is no control over falloff or size at all, and at
    /// fog &lt;= -0.5 the log() is NaN and Direct3D's min(NaN, 1) paints the light's whole quad
    /// at full colour -- the "box". LightHalo.shader is a port of that shader with the glow
    /// clamped positive and brightness, tightness and radius exposed.
    ///
    /// Only 'Custom/Lights/GroupVolume' has a port. The floating variant, and everything when
    /// the replacement shader is switched off or missing, stays on the game's shader with just
    /// the fog amount overridden (clamped to the range it survives).
    ///
    /// Dynamic lights (vehicles and the like, LightSystem.DrawLight) use an instanced variant
    /// with no port. The sliders here are tuned against the ported shader and mean something
    /// else to the game's, so that material just gets the smallest glow its shader can draw
    /// (fog pinned to <see cref="MinSafeFog"/>) -- independent of the world's fog, so foggy
    /// weather no longer has to be sacrificed to keep vehicle halos small.
    /// </remarks>
    public class HaloOverride : MonoBehaviour
    {
        /// <summary>The lowest fog value the game's own glow shader survives; see the remarks.</summary>
        public const float MinSafeFog = -0.49f;

        private const string PortedShaderName = "Custom/Lights/GroupVolume";
        private const float LogInterval = 5f;

        private const BindingFlags AnyInstance =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly int IdWeatherParams = Shader.PropertyToID("_WeatherParams");
        private static readonly int IdHaloFog = Shader.PropertyToID("_HaloFog");
        private static readonly int IdHaloBrightness = Shader.PropertyToID("_HaloBrightness");
        private static readonly int IdHaloTightness = Shader.PropertyToID("_HaloTightness");
        private static readonly int IdHaloRadius = Shader.PropertyToID("_HaloRadius");
        private static readonly int IdHaloNearDistance = Shader.PropertyToID("_HaloNearDistance");
        private static readonly int IdHaloNearBrightness = Shader.PropertyToID("_HaloNearBrightness");
        private static readonly int IdHaloNearTightness = Shader.PropertyToID("_HaloNearTightness");
        private static readonly int IdHaloNearRadius = Shader.PropertyToID("_HaloNearRadius");
        private static readonly int IdHaloOtherBrightness = Shader.PropertyToID("_HaloOtherBrightness");

        private class Replacement
        {
            public Material Original;
            public Material Material;
            public bool Ported;
        }

        private readonly Dictionary<Material, Replacement> _byOriginal = new Dictionary<Material, Replacement>();
        private readonly Dictionary<Material, Replacement> _byReplacement = new Dictionary<Material, Replacement>();

        private Shader _portedShader;
        private bool _shaderResolved;
        private Material _dynamicVolume;
        private bool _dynamicTouched;

        private Shader _dynamicShader;
        private bool _dynamicShaderResolved;
        private Material _dynamicOriginal;
        private Material _dynamicReplacement;

        private bool _wasEnabled;
        private bool _wasPorted;
        private float _nextLogTime;
        private int _layers;
        private int _swaps;

        private static bool Enabled
        {
            get { return Settings.HaloEnabled != null && Settings.HaloEnabled.value; }
        }

        private static bool WantPorted
        {
            get { return Settings.HaloReplaceShader == null || Settings.HaloReplaceShader.value; }
        }

        private void LateUpdate()
        {
            if (!Singleton<RenderManager>.exists || !Singleton<WeatherManager>.exists)
                return;

            bool enabled = Enabled;
            bool ported = WantPorted;

            // Anything that changes which shader the replacements use means rebuilding them.
            if (_wasEnabled && (!enabled || ported != _wasPorted))
                RestoreAll();

            _wasEnabled = enabled;
            _wasPorted = ported;

            RenderManager render = Singleton<RenderManager>.instance;
            LightSystem lights = render.lightSystem;
            if (lights == null)
                return;

            // Dynamic lights: the ported shader when it can be had, otherwise the game's own with
            // its fog pinned. Decided before the early-out so switching the feature off puts
            // the game's material back.
            bool dynamicPorted = SwapDynamic(lights, enabled && ported);
            HaloAdjuster.TagLamps = dynamicPorted;
            if (!dynamicPorted)
                SyncDynamicVolume(lights, enabled);

            if (!enabled)
                return;

            SwapSources(lights, ported);
            SwapLayers(render, lights, ported);
            UpdateMaterials();

            if (Log.Detailed && Time.time >= _nextLogTime)
            {
                _nextLogTime = Time.time + LogInterval;
                WriteLog();
            }
        }

        /// <summary>
        /// Puts ours into the two fields a rebuilt light layer copies its halo material from
        /// (see the remarks), so a rebuild picks up ours and never draws a frame with the
        /// game's. Checked every frame, like the dynamic slot: it holds if the game ever
        /// creates new materials there.
        /// </summary>
        private void SwapSources(LightSystem lights, bool ported)
        {
            lights.m_lightMaterialVolumeGroup =
                SourceReplacementFor(lights.m_lightMaterialVolumeGroup, ported, "m_lightMaterialVolumeGroup");
            lights.m_lightFloatingMaterialVolumeGroup =
                SourceReplacementFor(lights.m_lightFloatingMaterialVolumeGroup, ported, "m_lightFloatingMaterialVolumeGroup");
        }

        private Material SourceReplacementFor(Material current, bool ported, string field)
        {
            if (current == null || _byReplacement.ContainsKey(current))
                return current;

            Material replacement = Obtain(current, ported).Material;
            Log.Msg("halo: LightSystem." + field + " now holds ours too, so a light layer the game " +
                    "rebuilds (a building built or changed) draws with it from its first frame");
            return replacement;
        }

        /// <summary>
        /// The layers built before the sources were swapped still hold the game's material, so
        /// every light layer is re-checked each frame. With the sources swapped, nothing should
        /// be found after the first frame: `swapsSinceLastLog` in the detail line staying at 0
        /// while the city grows is what shows the flash is gone.
        /// </summary>
        private void SwapLayers(RenderManager render, LightSystem lights, bool ported)
        {
            int lightLayer = lights.m_lightLayer;
            int floatingLayer = lights.m_lightLayerFloating;
            _layers = 0;

            RenderGroup[] groups = render.m_groups;
            for (int i = 0; groups != null && i < groups.Length; i++)
            {
                if (groups[i] == null)
                    continue;

                for (RenderGroup.MeshLayer layer = groups[i].m_layers; layer != null; layer = layer.m_nextLayer)
                {
                    if (layer.m_layer == lightLayer || layer.m_layer == floatingLayer)
                        layer.m_renderMaterial = ReplacementFor(layer.m_renderMaterial, ported);
                }
            }

            // Same fields, unrelated type.
            MegaRenderGroup[] megaGroups = render.m_megaGroups;
            for (int i = 0; megaGroups != null && i < megaGroups.Length; i++)
            {
                if (megaGroups[i] == null)
                    continue;

                for (MegaRenderGroup.MeshLayer layer = megaGroups[i].m_layers; layer != null; layer = layer.m_nextLayer)
                {
                    if (layer.m_layer == lightLayer || layer.m_layer == floatingLayer)
                        layer.m_renderMaterial = ReplacementFor(layer.m_renderMaterial, ported);
                }
            }
        }

        private Material ReplacementFor(Material current, bool ported)
        {
            if (current == null)
                return null;

            _layers++;

            // Already one of ours.
            if (_byReplacement.ContainsKey(current))
                return current;

            _swaps++;
            return Obtain(current, ported).Material;
        }

        /// <summary>The one replacement for a game material, made on first use.</summary>
        private Replacement Obtain(Material original, bool ported)
        {
            Replacement replacement;
            if (_byOriginal.TryGetValue(original, out replacement))
                return replacement;

            replacement = new Replacement
            {
                Original = original,
                Material = new Material(original) { name = original.name + " (VolumetricClouds)" },
            };

            Shader shader = ported ? PortedShader() : null;
            if (shader != null && original.shader != null && original.shader.name == PortedShaderName)
            {
                replacement.Material.shader = shader;
                replacement.Ported = true;
            }

            _byOriginal.Add(original, replacement);
            _byReplacement.Add(replacement.Material, replacement);

            Log.Msg("halo: replacing '" + original.name + "' (shader '" +
                    (original.shader == null ? "null" : original.shader.name) + "') with " +
                    (replacement.Ported ? "the ported shader" : "a fog-overridden copy of the game's") +
                    ", renderQueue " + original.renderQueue + " -> " + replacement.Material.renderQueue);

            return replacement;
        }

        /// <summary>
        /// Lamps bloom in fog. The halo's fog input is ours now (it used to follow the world's
        /// fog), so our fog has to feed it or a foggy night would have clear-night lamps.
        /// </summary>
        private const float HaloFogPerFog = 0.6f;

        private void UpdateMaterials()
        {
            float fog = Value(Settings.HaloFogAmount, 0f) + CloudFog.Amount * HaloFogPerFog;
            float brightness = Value(Settings.HaloBrightness, 1f);
            float tightness = Value(Settings.HaloTightness, 1f);
            float radius = Value(Settings.HaloRadius, 1f);

            // With the distance at 0 the near multipliers are forced neutral rather than
            // trusting the shader's blend to vanish.
            float nearDistance = Mathf.Max(0f, Value(Settings.HaloNearLightDistance, 0f));
            bool near = nearDistance > 0f;
            float nearBrightness = near ? Mathf.Max(0f, Value(Settings.HaloNearLightBrightness, 1f)) : 1f;
            float nearTightness = near ? Mathf.Max(0.05f, Value(Settings.HaloNearLightTightness, 1f)) : 1f;
            float nearRadius = near ? Mathf.Clamp(Value(Settings.HaloNearLightRadius, 1f), 0.05f, 4f) : 1f;

            // Temperature, rain and wetness pass straight through; only fog (z) is ours, and
            // the game's shader must never be handed a value it turns into NaN.
            Vector4 weather = Shader.GetGlobalVector(IdWeatherParams);
            weather.z = Mathf.Max(MinSafeFog, fog);

            foreach (Replacement replacement in _byOriginal.Values)
            {
                if (replacement.Ported)
                    SetHaloControls(replacement.Material, fog, brightness, tightness, radius,
                                    nearDistance, nearBrightness, nearTightness, nearRadius);
                else
                    replacement.Material.SetVector(IdWeatherParams, weather);
            }

            // The dynamic material takes the SAME values, from the same place, so a lamp drawn
            // dynamically (tagged; see HaloAdjuster) cannot drift from the batched lamps beside
            // it. Untagged lights -- vehicles, non-batched effects -- get their own brightness.
            if (_dynamicReplacement != null)
            {
                SetHaloControls(_dynamicReplacement, fog, brightness, tightness, radius,
                                nearDistance, nearBrightness, nearTightness, nearRadius);
                _dynamicReplacement.SetFloat(IdHaloOtherBrightness, Mathf.Max(0f, Value(Settings.HaloVehicleBrightness, 1f)));
            }
        }

        private static void SetHaloControls(Material material, float fog, float brightness, float tightness, float radius,
            float nearDistance, float nearBrightness, float nearTightness, float nearRadius)
        {
            material.SetFloat(IdHaloFog, fog);
            material.SetFloat(IdHaloBrightness, Mathf.Max(0f, brightness));
            material.SetFloat(IdHaloTightness, Mathf.Max(0.05f, tightness));
            material.SetFloat(IdHaloRadius, Mathf.Clamp(radius, 0.05f, 4f));
            material.SetFloat(IdHaloNearDistance, nearDistance);
            material.SetFloat(IdHaloNearBrightness, nearBrightness);
            material.SetFloat(IdHaloNearTightness, nearTightness);
            material.SetFloat(IdHaloNearRadius, nearRadius);
        }

        private static float Value(FloatSetting setting, float fallback)
        {
            return setting != null ? setting.value : fallback;
        }

        /// <summary>
        /// Puts the ported dynamic halo material into LightSystem.m_lightMaterialVolume, or the
        /// game's back. Both DrawLight and EndRendering read that (public) field at draw time,
        /// so the swap is all it takes, and restoring is exact. Returns true while ours is in.
        /// </summary>
        private bool SwapDynamic(LightSystem lights, bool wanted)
        {
            if (wanted && _dynamicShader == null && !_dynamicShaderResolved)
            {
                _dynamicShaderResolved = true;
                _dynamicShader = ShaderBundle.Get(ShaderBundle.LightHaloDynamic);
            }

            if (!wanted || _dynamicShader == null)
            {
                if (_dynamicReplacement != null)
                {
                    if (lights.m_lightMaterialVolume == _dynamicReplacement)
                        lights.m_lightMaterialVolume = _dynamicOriginal;

                    Destroy(_dynamicReplacement);
                    _dynamicReplacement = null;
                    _dynamicOriginal = null;
                    Log.Msg("halo: dynamic lights are back on the game's halo material");
                }

                return false;
            }

            Material current = lights.m_lightMaterialVolume;
            if (current == null)
                return false;

            if (_dynamicReplacement == null)
            {
                _dynamicOriginal = current;
                _dynamicReplacement = new Material(current)
                {
                    name = current.name + " (VolumetricClouds dynamic)",
                    shader = _dynamicShader,
                };

                Log.Msg("halo: dynamic lights (vehicles, and lamps drawn without an instance id such as " +
                        "Intersection Marking Tool's) now use the ported shader; was '" +
                        (current.shader == null ? "null" : current.shader.name) + "', renderQueue " +
                        current.renderQueue + " -> " + _dynamicReplacement.renderQueue);
            }

            // Checked every frame: cheap, and it holds if the game recreates its material.
            if (current != _dynamicReplacement)
            {
                if (current != _dynamicOriginal)
                    _dynamicOriginal = current;

                lights.m_lightMaterialVolume = _dynamicReplacement;
            }

            return true;
        }

        /// <summary>
        /// Dynamic lights share one instanced material, on the game's own shader. While the
        /// feature is on its fog is pinned to the minimum. A value set on a material cannot be
        /// un-set in Unity 5.6, so once touched it is kept in step with the world for good:
        /// the world's own value while the feature is off, which is exactly what the shader
        /// global would have given it.
        /// </summary>
        private void SyncDynamicVolume(LightSystem lights, bool enabled)
        {
            if (!enabled && !_dynamicTouched)
                return;

            Material dynamicVolume = ResolveDynamicVolume(lights);
            if (dynamicVolume == null)
                return;

            // While the feature is on, vehicle halos are pinned to the smallest glow the game's
            // shader can draw. That is what PersistentFogAdjuster's -0.5 world fog used to buy;
            // pinning it here instead lets the WORLD's fog be real weather again.
            Vector4 weather = Shader.GetGlobalVector(IdWeatherParams);
            if (enabled)
                weather.z = MinSafeFog + CloudFog.Amount * HaloFogPerFog;

            dynamicVolume.SetVector(IdWeatherParams, weather);
            _dynamicTouched = true;
        }

        private Shader PortedShader()
        {
            if (!_shaderResolved)
            {
                _shaderResolved = true;
                _portedShader = ShaderBundle.Get(ShaderBundle.LightHalo);
            }

            return _portedShader;
        }

        /// <summary>
        /// The GAME's dynamic halo material, for the fallback that only pins its fog. Never our
        /// replacement: that one is destroyed when the feature goes off, and SwapDynamic has
        /// already put the game's back by the time this is asked.
        /// </summary>
        private Material ResolveDynamicVolume(LightSystem lights)
        {
            Material current = lights.m_lightMaterialVolume;
            if (current != null && current != _dynamicReplacement)
                _dynamicVolume = current;

            return _dynamicVolume;
        }

        /// <summary>Puts the game's materials back on every layer still holding one of ours.</summary>
        private void RestoreAll()
        {
            if (_byOriginal.Count == 0)
                return;

            if (Singleton<RenderManager>.exists)
            {
                RenderManager render = Singleton<RenderManager>.instance;

                // The sources first. LightSystem.DestroyProperties destroys whatever these hold
                // when the city's scene goes, so the game's own must be back by then; they are,
                // because OnLevelUnloading (-> our OnDestroy, end of that frame) runs before the
                // scene switch. A field the game already emptied stays empty.
                LightSystem lights = render.lightSystem;
                if (lights != null)
                {
                    lights.m_lightMaterialVolumeGroup = OriginalOf(lights.m_lightMaterialVolumeGroup);
                    lights.m_lightFloatingMaterialVolumeGroup = OriginalOf(lights.m_lightFloatingMaterialVolumeGroup);
                }

                RenderGroup[] groups = render.m_groups;
                for (int i = 0; groups != null && i < groups.Length; i++)
                {
                    if (groups[i] == null)
                        continue;

                    for (RenderGroup.MeshLayer layer = groups[i].m_layers; layer != null; layer = layer.m_nextLayer)
                        layer.m_renderMaterial = OriginalOf(layer.m_renderMaterial);
                }

                MegaRenderGroup[] megaGroups = render.m_megaGroups;
                for (int i = 0; megaGroups != null && i < megaGroups.Length; i++)
                {
                    if (megaGroups[i] == null)
                        continue;

                    for (MegaRenderGroup.MeshLayer layer = megaGroups[i].m_layers; layer != null; layer = layer.m_nextLayer)
                        layer.m_renderMaterial = OriginalOf(layer.m_renderMaterial);
                }
            }

            // Unity's null test: at quit the game may have destroyed one of ours already, if it
            // was still in a source field when LightSystem.DestroyProperties ran.
            foreach (Replacement replacement in _byOriginal.Values)
            {
                if (replacement.Material != null)
                    Destroy(replacement.Material);
            }

            _byOriginal.Clear();
            _byReplacement.Clear();

            Log.Msg("halo: restored the game's light materials");
        }

        private Material OriginalOf(Material current)
        {
            Replacement replacement;
            if (current != null && _byReplacement.TryGetValue(current, out replacement))
                return replacement.Original;

            return current;
        }

        private void WriteLog()
        {
            int ported = 0;
            foreach (Replacement replacement in _byOriginal.Values)
            {
                if (replacement.Ported)
                    ported++;
            }

            // "sources" = how many of the two LightSystem fields hold ours (2 expected; see
            // SwapSources). With 2, swapsSinceLastLog should read 0 after the first line.
            int sources = 0;
            LightSystem lights = Singleton<RenderManager>.instance.lightSystem;
            if (lights != null)
            {
                if (lights.m_lightMaterialVolumeGroup != null && _byReplacement.ContainsKey(lights.m_lightMaterialVolumeGroup))
                    sources++;
                if (lights.m_lightFloatingMaterialVolumeGroup != null && _byReplacement.ContainsKey(lights.m_lightFloatingMaterialVolumeGroup))
                    sources++;
            }

            Log.Detail("halo: layers=" + _layers +
                    " materials=" + _byOriginal.Count + " (ported=" + ported + ")" +
                    " sources=" + sources + "/2" +
                    " swapsSinceLastLog=" + _swaps +
                    " | fog=" + (Settings.HaloFogAmount != null ? Settings.HaloFogAmount.value : 0f).ToString("F2") +
                    " brightness=" + (Settings.HaloBrightness != null ? Settings.HaloBrightness.value : 1f).ToString("F2") +
                    " tightness=" + (Settings.HaloTightness != null ? Settings.HaloTightness.value : 1f).ToString("F2") +
                    " radius=" + (Settings.HaloRadius != null ? Settings.HaloRadius.value : 1f).ToString("F2") +
                    " | near<" + Value(Settings.HaloNearLightDistance, 0f).ToString("F0") + "m" +
                    " x brightness=" + Value(Settings.HaloNearLightBrightness, 1f).ToString("F2") +
                    " tightness=" + Value(Settings.HaloNearLightTightness, 1f).ToString("F2") +
                    " radius=" + Value(Settings.HaloNearLightRadius, 1f).ToString("F2") +
                    " | worldFog=" + Singleton<WeatherManager>.instance.m_currentFog.ToString("F2") +
                    " | dynamic=" + (_dynamicReplacement != null
                        ? "PORTED otherBrightness=" + Value(Settings.HaloVehicleBrightness, 1f).ToString("F2") +
                          " (lamps tagged: see the 'dynamic lights' line)"
                        : "game shader, fog " + (_dynamicTouched && _dynamicVolume != null
                            ? _dynamicVolume.GetVector(IdWeatherParams).z.ToString("F2") : "untouched")));

            _swaps = 0;
        }

        private void OnDestroy()
        {
            RestoreAll();

            // The tag must stop before the game's own shader is back in the slot.
            HaloAdjuster.TagLamps = false;
            if (Singleton<RenderManager>.exists && Singleton<RenderManager>.instance.lightSystem != null)
                SwapDynamic(Singleton<RenderManager>.instance.lightSystem, false);

            // Last chance to leave the game's dynamic material on the world's own value.
            if (_dynamicTouched && _dynamicVolume != null)
                _dynamicVolume.SetVector(IdWeatherParams, Shader.GetGlobalVector(IdWeatherParams));
        }
    }
}
