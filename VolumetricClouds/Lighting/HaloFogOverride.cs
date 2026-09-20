using System.Collections.Generic;
using System.Reflection;
using ColossalFramework;
using UnityEngine;

namespace VolumetricClouds.Lighting
{
    /// <summary>
    /// Gives the night-light halos their own fog amount, varying with distance from the
    /// camera, and leaves the world's fog alone.
    /// </summary>
    /// <remarks>
    /// Lights are level-of-detail: LightEffect.RenderEffect draws a lamp dynamically through
    /// LightSystem.DrawLight while it is inside its fade distance, and beyond that it is one
    /// of many baked into a per-area mesh, drawn by RenderGroup.RenderMesh /
    /// MegaRenderGroup.RenderMesh. Each such layer is drawn TWICE, with two materials read at
    /// draw time every frame:
    ///   m_bufferMaterial (via the "Batched lights" CommandBuffer) = the surface illumination
    ///   m_renderMaterial (via Graphics.DrawMesh)                 = the halo, 'GroupVolume'
    /// It is m_renderMaterial this class replaces. An earlier version swapped
    /// m_bufferMaterial instead and, faithfully and uselessly, re-fogged the ground lighting.
    ///
    /// The glow shader scales the halo from _WeatherParams.z, the game's fog amount (the
    /// FogProperties vectors are clamped to 0..1 and ignored by it).
    ///
    /// What this buys: the halos can sit at their faintest (-0.49) while the WORLD keeps
    /// whatever fog it likes, instead of the whole map being pinned to -0.5 just to tame the
    /// lights. What it cannot buy is a halo smaller than the shader allows; see
    /// <see cref="MinSafeFog"/> for why values at or below -0.5 draw a box, at any distance.
    /// (The per-group distance blend below was built on the mistaken belief that the box only
    /// affected lights near the camera. It is harmless, and still lets near and far differ
    /// within the safe range.)
    ///
    /// Each light layer gets its OWN clone of the material, carrying its own fog value. The
    /// game's material is never modified, which makes this cleanly reversible: put the
    /// original reference back and destroy the clones.
    ///
    /// Going further means replacing the shader, not feeding it: tools/shaderdump.ps1
    /// disassembles it, and the vertex layout of the batched mesh is POSITION = quad corner,
    /// NORMAL = light position, TANGENT = direction/spot, TEXCOORD0 = (1/range^2, intensity),
    /// TEXCOORD1 = (switch-on threshold, blink pattern), COLOR = light colour.
    /// </remarks>
    public class HaloFogOverride : MonoBehaviour
    {
        /// <summary>
        /// The lowest fog value the game's glow shader survives. Disassembled, its pixel
        /// shader computes k = 0.001 * (fog + 0.5), then glow = k * (1/x - 1), then
        /// pow(glow, ...) via log/exp. At fog &lt;= -0.5, k is zero or negative, log() yields NaN,
        /// and Direct3D's min(NaN, 1) returns 1 -- so the light's entire quad is drawn at full
        /// colour. That is the "box". No fog value below this can ever work.
        /// </summary>
        public const float MinSafeFog = -0.49f;

        private const float LogInterval = 5f;
        private const int SweepEveryFrames = 120;

        private const BindingFlags AnyInstance =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly int IdWeatherParams = Shader.PropertyToID("_WeatherParams");

        /// <summary>
        /// One tracked light layer. RenderGroup.MeshLayer and MegaRenderGroup.MeshLayer are
        /// unrelated types with the same fields, so the entry carries accessors instead of
        /// the layer itself. They are created once per layer, never per frame.
        /// </summary>
        private class Entry
        {
            public Material Original;
            public Material Clone;
            public int LastSeenFrame;
            public bool NeedsAssign;
            public System.Func<Material> GetMaterial;
            public System.Action<Material> SetMaterial;
        }

        private readonly Dictionary<object, Entry> _entries = new Dictionary<object, Entry>();
        private readonly List<object> _stale = new List<object>();

        // The blend being applied this frame, so the per-layer code needn't take ten arguments.
        private Vector4 _weather;
        private Vector3 _cameraPosition;
        private float _nearFog;
        private float _farFog;
        private float _blendStart;
        private float _blendEnd;

        private Material _dynamicVolume;
        private bool _dynamicResolved;
        private Camera _camera;
        private float _nextLogTime;
        private bool _wasEnabled;

        // Per-interval statistics for the log.
        private int _layersThisFrame;
        private int _swaps;
        private float _minDistance;
        private float _maxDistance;
        private float _minFog;
        private float _maxFog;

        private static bool Enabled
        {
            get { return Settings.HaloFogEnabled != null && Settings.HaloFogEnabled.value; }
        }

        private void LateUpdate()
        {
            if (!Singleton<RenderManager>.exists || !Singleton<WeatherManager>.exists)
                return;

            if (!Enabled)
            {
                if (_wasEnabled)
                {
                    RestoreAll();
                    _wasEnabled = false;
                }

                return;
            }

            _wasEnabled = true;

            if (_camera == null)
                _camera = Camera.main;
            if (_camera == null)
                return;

            Apply(_camera.transform.position);

            if (Time.frameCount % SweepEveryFrames == 0)
                Sweep();

            if (Time.time >= _nextLogTime)
            {
                _nextLogTime = Time.time + LogInterval;
                WriteLog();
            }
        }

        private void Apply(Vector3 cameraPosition)
        {
            RenderManager render = Singleton<RenderManager>.instance;
            LightSystem lights = render.lightSystem;
            if (lights == null)
                return;

            _farFog = Mathf.Max(MinSafeFog, Settings.HaloFogValue != null ? Settings.HaloFogValue.value : MinSafeFog);
            _nearFog = Mathf.Max(MinSafeFog, Settings.HaloFogNear != null ? Settings.HaloFogNear.value : -0.45f);
            _blendStart = Settings.HaloFogStart != null ? Settings.HaloFogStart.value : 500f;
            _blendEnd = Settings.HaloFogEnd != null ? Settings.HaloFogEnd.value : 2500f;
            _blendEnd = Mathf.Max(_blendEnd, _blendStart + 1f);
            _cameraPosition = cameraPosition;

            // Temperature, rain and wetness pass straight through; only fog (z) is ours.
            _weather = Shader.GetGlobalVector(IdWeatherParams);

            int lightLayer = lights.m_lightLayer;
            int floatingLayer = lights.m_lightLayerFloating;

            _layersThisFrame = 0;
            _minDistance = float.MaxValue;
            _maxDistance = 0f;
            _minFog = float.MaxValue;
            _maxFog = float.MinValue;

            RenderGroup[] groups = render.m_groups;
            for (int i = 0; groups != null && i < groups.Length; i++)
            {
                if (groups[i] == null)
                    continue;

                for (RenderGroup.MeshLayer layer = groups[i].m_layers; layer != null; layer = layer.m_nextLayer)
                {
                    if ((layer.m_layer != lightLayer && layer.m_layer != floatingLayer) || layer.m_renderMaterial == null)
                        continue;

                    Entry entry = Track(layer, layer.m_renderMaterial);
                    if (entry.SetMaterial == null)
                    {
                        RenderGroup.MeshLayer captured = layer;
                        entry.GetMaterial = () => captured.m_renderMaterial;
                        entry.SetMaterial = material => captured.m_renderMaterial = material;
                    }

                    // Distance to the nearest light in the group, not to the group's centre:
                    // what matters is how close the camera can be to any one of them.
                    ApplyFog(entry, layer.m_bounds);
                }
            }

            MegaRenderGroup[] megaGroups = render.m_megaGroups;
            for (int i = 0; megaGroups != null && i < megaGroups.Length; i++)
            {
                MegaRenderGroup mega = megaGroups[i];
                if (mega == null)
                    continue;

                for (MegaRenderGroup.MeshLayer layer = mega.m_layers; layer != null; layer = layer.m_nextLayer)
                {
                    if ((layer.m_layer != lightLayer && layer.m_layer != floatingLayer) ||
                        layer.m_renderMaterial == null || layer.m_mesh == null)
                    {
                        continue;
                    }

                    Entry entry = Track(layer, layer.m_renderMaterial);
                    if (entry.SetMaterial == null)
                    {
                        MegaRenderGroup.MeshLayer captured = layer;
                        entry.GetMaterial = () => captured.m_renderMaterial;
                        entry.SetMaterial = material => captured.m_renderMaterial = material;
                    }

                    // Mega layers carry no bounds of their own; the mesh's are local to the
                    // group, so move them into the world with the matrix it is drawn with.
                    Bounds bounds = layer.m_mesh.bounds;
                    bounds.center = mega.m_matrix.MultiplyPoint(bounds.center);
                    ApplyFog(entry, bounds);
                }
            }

            // Vehicles and other dynamic lights share one instanced material, so they can
            // only take a single value. They are usually what is right under the camera,
            // so they get the safe one.
            Material dynamicVolume = ResolveDynamicVolume(lights);
            if (dynamicVolume != null)
            {
                Vector4 weather = _weather;
                weather.z = _nearFog;
                dynamicVolume.SetVector(IdWeatherParams, weather);
            }
        }

        /// <summary>Finds or creates the entry for a layer, (re)cloning if the game swapped its material back.</summary>
        private Entry Track(object layer, Material current)
        {
            Entry entry;
            if (!_entries.TryGetValue(layer, out entry))
            {
                entry = new Entry();
                _entries.Add(layer, entry);
            }

            entry.LastSeenFrame = Time.frameCount;

            // The game writes its own material back whenever it rebuilds the group's mesh,
            // so this is re-checked every frame rather than done once.
            if (current != entry.Clone)
            {
                if (entry.Clone != null)
                    Destroy(entry.Clone);

                entry.Original = current;
                entry.Clone = new Material(current) { name = current.name + " (VolumetricClouds halo)" };
                entry.NeedsAssign = true;
                _swaps++;
            }

            return entry;
        }

        private void ApplyFog(Entry entry, Bounds worldBounds)
        {
            if (entry.NeedsAssign)
            {
                entry.SetMaterial(entry.Clone);
                entry.NeedsAssign = false;
            }

            float distance = Mathf.Sqrt(worldBounds.SqrDistance(_cameraPosition));
            float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(_blendStart, _blendEnd, distance));
            float fog = Mathf.Lerp(_nearFog, _farFog, t);

            Vector4 weather = _weather;
            weather.z = fog;
            entry.Clone.SetVector(IdWeatherParams, weather);

            _layersThisFrame++;
            if (distance < _minDistance) _minDistance = distance;
            if (distance > _maxDistance) _maxDistance = distance;
            if (fog < _minFog) _minFog = fog;
            if (fog > _maxFog) _maxFog = fog;
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

        /// <summary>Drops entries for layers the game has since discarded.</summary>
        private void Sweep()
        {
            _stale.Clear();

            foreach (KeyValuePair<object, Entry> pair in _entries)
            {
                if (Time.frameCount - pair.Value.LastSeenFrame > SweepEveryFrames)
                    _stale.Add(pair.Key);
            }

            foreach (object layer in _stale)
            {
                Entry entry = _entries[layer];
                if (entry.Clone != null)
                    Destroy(entry.Clone);
                _entries.Remove(layer);
            }
        }

        private void RestoreAll()
        {
            foreach (KeyValuePair<object, Entry> pair in _entries)
            {
                Entry entry = pair.Value;

                // Only put the game's material back if the layer still holds ours; the game
                // may have replaced it already.
                if (entry.GetMaterial != null && entry.Original != null &&
                    entry.GetMaterial() == entry.Clone)
                {
                    entry.SetMaterial(entry.Original);
                }

                if (entry.Clone != null)
                    Destroy(entry.Clone);
            }

            _entries.Clear();

            // The instanced material cannot be un-set in Unity 5.6, so match it to the world.
            if (_dynamicVolume != null && Singleton<WeatherManager>.exists)
            {
                Vector4 weather = Shader.GetGlobalVector(IdWeatherParams);
                weather.z = Singleton<WeatherManager>.instance.m_currentFog;
                _dynamicVolume.SetVector(IdWeatherParams, weather);
            }

            Log.Msg("halo fog: restored the game's light materials");
        }

        private void WriteLog()
        {
            Log.Msg("halo fog: lightLayers=" + _layersThisFrame +
                    " clones=" + _entries.Count +
                    " swapsSinceLastLog=" + _swaps +
                    " | worldFog=" + Singleton<WeatherManager>.instance.m_currentFog.ToString("F2") +
                    " near=" + (Settings.HaloFogNear != null ? Settings.HaloFogNear.value : 0f).ToString("F2") +
                    " far=" + (Settings.HaloFogValue != null ? Settings.HaloFogValue.value : 0f).ToString("F2") +
                    " blend=" + (Settings.HaloFogStart != null ? Settings.HaloFogStart.value : 0f).ToString("F0") +
                    ".." + (Settings.HaloFogEnd != null ? Settings.HaloFogEnd.value : 0f).ToString("F0") + "m" +
                    " | groupDistance=" + (_layersThisFrame == 0 ? "n/a" :
                        _minDistance.ToString("F0") + ".." + _maxDistance.ToString("F0") + "m") +
                    " fogAssigned=" + (_layersThisFrame == 0 ? "n/a" :
                        _maxFog.ToString("F2") + ".." + _minFog.ToString("F2")) +
                    " | cameraY=" + (_camera == null ? 0f : _camera.transform.position.y).ToString("F0"));

            _swaps = 0;
        }

        private void OnDestroy()
        {
            RestoreAll();
        }
    }
}
