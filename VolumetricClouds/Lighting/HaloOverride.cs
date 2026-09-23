using System.Collections.Generic;
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
    /// clamped positive and brightness, tightness and radius exposed; LightHaloDynamic.shader
    /// is the same port of the instanced variant the dynamic lights (vehicles, and lamps drawn
    /// without an instance id) use.
    ///
    /// ONE switch (1.1.0, the author's call: "just settle for one checkbox"): `HaloEnabled`
    /// puts the ported shaders on every batched light layer and on the dynamic lights'
    /// material; off leaves the game's materials exactly as they are. Until 1.1.0 a second
    /// switch could keep the game's shader with only its fog overridden, and the dynamic
    /// material was pinned to the smallest glow that shader survives (-0.49); both are gone,
    /// and so is the "Fog amount" slider. The fog the halos see is the WORLD's: what the
    /// game's own shader reads (see <see cref="HaloFog"/>), so foggy weather and Play It!'s
    /// fog slider bloom the lamps as they do in vanilla ("let other mods like Play It or the
    /// game control that"). Only 'Custom/Lights/GroupVolume' has a port; a light material on
    /// any other shader is left as the game's.
    /// </remarks>
    public class HaloOverride : MonoBehaviour
    {
        private const string PortedShaderName = "Custom/Lights/GroupVolume";
        private const float LogInterval = 5f;

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
        }

        private readonly Dictionary<Material, Replacement> _byOriginal = new Dictionary<Material, Replacement>();
        private readonly Dictionary<Material, Replacement> _byReplacement = new Dictionary<Material, Replacement>();

        /// <summary>Game materials on a shader we have no port for: left alone, said once.</summary>
        private readonly HashSet<Material> _unported = new HashSet<Material>();

        private Shader _portedShader;
        private bool _shaderResolved;
        private bool _missingShaderLogged;

        private Shader _dynamicShader;
        private bool _dynamicShaderResolved;
        private Material _dynamicOriginal;
        private Material _dynamicReplacement;

        private bool _wasEnabled;
        private float _nextLogTime;
        private int _layers;
        private int _swaps;

        private static bool Enabled
        {
            get { return Settings.HaloEnabled != null && Settings.HaloEnabled.value; }
        }

        private void LateUpdate()
        {
            if (!Singleton<RenderManager>.exists || !Singleton<WeatherManager>.exists)
                return;

            bool enabled = Enabled;

            if (_wasEnabled && !enabled)
                RestoreAll();

            _wasEnabled = enabled;

            RenderManager render = Singleton<RenderManager>.instance;
            LightSystem lights = render.lightSystem;
            if (lights == null)
                return;

            // Decided before the early-out so switching the feature off puts the game's
            // material back.
            HaloAdjuster.TagLamps = SwapDynamic(lights, enabled);

            if (!enabled)
                return;

            if (PortedShader() == null)
            {
                if (!_missingShaderLogged)
                {
                    _missingShaderLogged = true;
                    Log.Warn("halo: the ported halo shader is not in the bundle; the game's halos are left as they are");
                }

                return;
            }

            SwapSources(lights);
            SwapLayers(render, lights);
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
        private void SwapSources(LightSystem lights)
        {
            lights.m_lightMaterialVolumeGroup =
                SourceReplacementFor(lights.m_lightMaterialVolumeGroup, "m_lightMaterialVolumeGroup");
            lights.m_lightFloatingMaterialVolumeGroup =
                SourceReplacementFor(lights.m_lightFloatingMaterialVolumeGroup, "m_lightFloatingMaterialVolumeGroup");
        }

        private Material SourceReplacementFor(Material current, string field)
        {
            if (current == null || _byReplacement.ContainsKey(current))
                return current;

            Replacement replacement = Obtain(current);
            if (replacement == null)
                return current;

            Log.Msg("halo: LightSystem." + field + " now holds ours too, so a light layer the game " +
                    "rebuilds (a building built or changed) draws with it from its first frame");
            return replacement.Material;
        }

        /// <summary>
        /// The layers built before the sources were swapped still hold the game's material, so
        /// every light layer is re-checked each frame. With the sources swapped, nothing should
        /// be found after the first frame: `swapsSinceLastLog` in the detail line staying at 0
        /// while the city grows is what shows the flash is gone.
        /// </summary>
        private void SwapLayers(RenderManager render, LightSystem lights)
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
                        layer.m_renderMaterial = ReplacementFor(layer.m_renderMaterial);
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
                        layer.m_renderMaterial = ReplacementFor(layer.m_renderMaterial);
                }
            }
        }

        private Material ReplacementFor(Material current)
        {
            if (current == null)
                return null;

            _layers++;

            // Already one of ours.
            if (_byReplacement.ContainsKey(current))
                return current;

            Replacement replacement = Obtain(current);
            if (replacement == null)
                return current;

            _swaps++;
            return replacement.Material;
        }

        /// <summary>
        /// The one replacement for a game material, made on first use; null for a material on
        /// a shader we have no port for, which stays the game's.
        /// </summary>
        private Replacement Obtain(Material original)
        {
            Replacement replacement;
            if (_byOriginal.TryGetValue(original, out replacement))
                return replacement;

            if (_unported.Contains(original))
                return null;

            if (original.shader == null || original.shader.name != PortedShaderName)
            {
                _unported.Add(original);
                Log.Msg("halo: '" + original.name + "' (shader '" +
                        (original.shader == null ? "null" : original.shader.name) + "') is left as the game's: no port for that shader");
                return null;
            }

            replacement = new Replacement
            {
                Original = original,
                Material = new Material(original)
                {
                    name = original.name + " (VolumetricClouds)",
                    shader = PortedShader(),
                },
            };

            _byOriginal.Add(original, replacement);
            _byReplacement.Add(replacement.Material, replacement);

            Log.Msg("halo: replacing '" + original.name + "' with the ported shader, renderQueue " +
                    original.renderQueue + " -> " + replacement.Material.renderQueue);

            return replacement;
        }

        /// <summary>
        /// The fog the halos see. The game's shader reads _WeatherParams.z -- WeatherManager's
        /// fog, unclamped, so a mod that parks it below zero (Persistent Fog Adjuster) is
        /// honoured too -- and ours reads the same, so the weather's fog and Play It!'s fog
        /// slider bloom the lamps exactly as in vanilla. While OUR fog is on screen its amount
        /// is the fog in the air (following the game it is the game's value smoothed; overridden
        /// it is the slider) and stands in for it.
        /// </summary>
        private static float HaloFog()
        {
            float world = Shader.GetGlobalVector(IdWeatherParams).z;
            return CloudFog.Active ? CloudFog.Amount : world;
        }

        private void UpdateMaterials()
        {
            float fog = HaloFog();
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

            foreach (Replacement replacement in _byOriginal.Values)
            {
                SetHaloControls(replacement.Material, fog, brightness, tightness, radius,
                                nearDistance, nearBrightness, nearTightness, nearRadius);
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

        private Shader PortedShader()
        {
            if (!_shaderResolved)
            {
                _shaderResolved = true;
                _portedShader = ShaderBundle.Get(ShaderBundle.LightHalo);
            }

            return _portedShader;
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
            _unported.Clear();

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
                    " materials=" + _byOriginal.Count + " (left to the game: " + _unported.Count + ")" +
                    " sources=" + sources + "/2" +
                    " swapsSinceLastLog=" + _swaps +
                    " | fog=" + HaloFog().ToString("F2") + " (world " + Singleton<WeatherManager>.instance.m_currentFog.ToString("F2") +
                    (CloudFog.Active ? ", ours " + CloudFog.Amount.ToString("F2") + " in use)" : ")") +
                    " brightness=" + Value(Settings.HaloBrightness, 1f).ToString("F2") +
                    " tightness=" + Value(Settings.HaloTightness, 1f).ToString("F2") +
                    " radius=" + Value(Settings.HaloRadius, 1f).ToString("F2") +
                    " | near<" + Value(Settings.HaloNearLightDistance, 0f).ToString("F0") + "m" +
                    " x brightness=" + Value(Settings.HaloNearLightBrightness, 1f).ToString("F2") +
                    " tightness=" + Value(Settings.HaloNearLightTightness, 1f).ToString("F2") +
                    " radius=" + Value(Settings.HaloNearLightRadius, 1f).ToString("F2") +
                    " | dynamic=" + (_dynamicReplacement != null
                        ? "PORTED otherBrightness=" + Value(Settings.HaloVehicleBrightness, 1f).ToString("F2") +
                          " (lamps tagged: see the 'dynamic lights' line)"
                        : "the game's (shader missing)"));

            _swaps = 0;
        }

        private void OnDestroy()
        {
            RestoreAll();

            // The tag must stop before the game's own shader is back in the slot.
            HaloAdjuster.TagLamps = false;
            if (Singleton<RenderManager>.exists && Singleton<RenderManager>.instance.lightSystem != null)
                SwapDynamic(Singleton<RenderManager>.instance.lightSystem, false);
        }
    }
}
