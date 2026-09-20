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
    /// with no port. They are left on the WORLD's fog, only clamped to what that shader
    /// survives: the sliders here are tuned against the ported shader and mean something
    /// else to the game's.
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
        private bool _dynamicResolved;
        private bool _dynamicTouched;

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

            SyncDynamicVolume(lights, enabled);

            if (!enabled)
                return;

            SwapLayers(render, lights, ported);
            UpdateMaterials();

            if (Time.time >= _nextLogTime)
            {
                _nextLogTime = Time.time + LogInterval;
                WriteLog();
            }
        }

        /// <summary>
        /// The game writes its own material back whenever it rebuilds a group's mesh, so
        /// every light layer is re-checked each frame rather than swapped once.
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

            Replacement replacement;
            if (!_byOriginal.TryGetValue(current, out replacement))
            {
                replacement = new Replacement
                {
                    Original = current,
                    Material = new Material(current) { name = current.name + " (VolumetricClouds)" },
                };

                Shader shader = ported ? PortedShader() : null;
                if (shader != null && current.shader != null && current.shader.name == PortedShaderName)
                {
                    replacement.Material.shader = shader;
                    replacement.Ported = true;
                }

                _byOriginal.Add(current, replacement);
                _byReplacement.Add(replacement.Material, replacement);

                Log.Msg("halo: replacing '" + current.name + "' (shader '" +
                        (current.shader == null ? "null" : current.shader.name) + "') with " +
                        (replacement.Ported ? "the ported shader" : "a fog-overridden copy of the game's") +
                        ", renderQueue " + current.renderQueue + " -> " + replacement.Material.renderQueue);
            }

            _swaps++;
            return replacement.Material;
        }

        private void UpdateMaterials()
        {
            float fog = Value(Settings.HaloFogAmount, 0f);
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
                {
                    replacement.Material.SetFloat(IdHaloFog, fog);
                    replacement.Material.SetFloat(IdHaloBrightness, Mathf.Max(0f, brightness));
                    replacement.Material.SetFloat(IdHaloTightness, Mathf.Max(0.05f, tightness));
                    replacement.Material.SetFloat(IdHaloRadius, Mathf.Clamp(radius, 0.05f, 4f));
                    replacement.Material.SetFloat(IdHaloNearDistance, nearDistance);
                    replacement.Material.SetFloat(IdHaloNearBrightness, nearBrightness);
                    replacement.Material.SetFloat(IdHaloNearTightness, nearTightness);
                    replacement.Material.SetFloat(IdHaloNearRadius, nearRadius);
                }
                else
                {
                    replacement.Material.SetVector(IdWeatherParams, weather);
                }
            }
        }

        private static float Value(SavedFloat setting, float fallback)
        {
            return setting != null ? setting.value : fallback;
        }

        /// <summary>
        /// Dynamic lights share one instanced material, on the game's own shader. While the
        /// feature is on it sees the world's fog clamped to what that shader survives. A value
        /// set on a material cannot be un-set in Unity 5.6, so once touched it is kept in step
        /// with the world for good: unclamped while the feature is off, which is exactly what
        /// the shader global would have given it.
        /// </summary>
        private void SyncDynamicVolume(LightSystem lights, bool enabled)
        {
            if (!enabled && !_dynamicTouched)
                return;

            Material dynamicVolume = ResolveDynamicVolume(lights);
            if (dynamicVolume == null)
                return;

            Vector4 weather = Shader.GetGlobalVector(IdWeatherParams);
            if (enabled)
                weather.z = Mathf.Max(MinSafeFog, weather.z);

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

        private Material ResolveDynamicVolume(LightSystem lights)
        {
            if (!_dynamicResolved)
            {
                _dynamicResolved = true;
                FieldInfo field = typeof(LightSystem).GetField("m_lightMaterialVolume", AnyInstance);
                _dynamicVolume = field == null ? null : field.GetValue(lights) as Material;
            }

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

            foreach (Replacement replacement in _byOriginal.Values)
                Destroy(replacement.Material);

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

            Log.Msg("halo: layers=" + _layers +
                    " materials=" + _byOriginal.Count + " (ported=" + ported + ")" +
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
                    " dynamicFog=" + (_dynamicTouched && _dynamicVolume != null
                        ? _dynamicVolume.GetVector(IdWeatherParams).z.ToString("F2") : "untouched"));

            _swaps = 0;
        }

        private void OnDestroy()
        {
            RestoreAll();

            // Last chance to leave the dynamic material on the world's own value.
            if (_dynamicTouched && _dynamicVolume != null)
                _dynamicVolume.SetVector(IdWeatherParams, Shader.GetGlobalVector(IdWeatherParams));
        }
    }
}
