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
    /// Lights are level-of-detail: LightEffect.RenderEffect draws a lamp dynamically through
    /// LightSystem.DrawLight while it is inside its fade distance, and beyond that it is one
    /// of many baked into a per-area mesh, drawn by RenderGroup.RenderMesh /
    /// MegaRenderGroup.RenderMesh. Each such layer is drawn TWICE, with two materials that
    /// are read at draw time every frame:
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

            if (!enabled)
                return;

            RenderManager render = Singleton<RenderManager>.instance;
            LightSystem lights = render.lightSystem;
            if (lights == null)
                return;

            SwapLayers(render, lights, ported);
            UpdateMaterials(lights);

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

        private void UpdateMaterials(LightSystem lights)
        {
            float fog = Settings.HaloFogAmount != null ? Settings.HaloFogAmount.value : 0f;
            float brightness = Settings.HaloBrightness != null ? Settings.HaloBrightness.value : 1f;
            float tightness = Settings.HaloTightness != null ? Settings.HaloTightness.value : 1f;
            float radius = Settings.HaloRadius != null ? Settings.HaloRadius.value : 1f;

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
                }
                else
                {
                    replacement.Material.SetVector(IdWeatherParams, weather);
                }
            }

            // Lights near the camera are drawn dynamically with one shared instanced material,
            // which has no port yet, so it only gets the (safe) fog amount.
            Material dynamicVolume = ResolveDynamicVolume(lights);
            if (dynamicVolume != null)
                dynamicVolume.SetVector(IdWeatherParams, weather);
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

            // The instanced material cannot be un-set in Unity 5.6, so match it to the world.
            if (_dynamicVolume != null && Singleton<WeatherManager>.exists)
            {
                Vector4 weather = Shader.GetGlobalVector(IdWeatherParams);
                weather.z = Singleton<WeatherManager>.instance.m_currentFog;
                _dynamicVolume.SetVector(IdWeatherParams, weather);
            }

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
                    " | worldFog=" + Singleton<WeatherManager>.instance.m_currentFog.ToString("F2"));

            _swaps = 0;
        }

        private void OnDestroy()
        {
            RestoreAll();
        }
    }
}
