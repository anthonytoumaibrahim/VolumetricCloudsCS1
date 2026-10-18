using System.Collections.Generic;
using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// The funnels the cloud pass draws this frame (1.5.0): our waterspouts (Waterspouts), at most
    /// <see cref="FunnelShape.Max"/>, nearest first. Advanced once a frame from CloudLighting with the
    /// weather; read by CloudVolume (the shader's arrays) and FunnelDebris (the flecks and the water).
    /// With none, the shader's branch is never taken: nothing of it runs. (Natural Disasters' tornadoes
    /// were drawn here too until 2026-10-19 -- the author: "I don't like the tornadoes at all ... let's
    /// remove the tornadoes entirely ... keep the tornadoes in the plan"; their code is kept with it.)
    /// </summary>
    public static class Funnels
    {
        /// <summary>How far funnels are drawn, metres, and over how much they fade out before that.</summary>
        public const float Reach = 30000f;
        public const float FadeLength = 8000f;

        private static readonly List<Funnel> _all = new List<Funnel>();
        private static readonly Funnel[] _drawn = new Funnel[FunnelShape.Max];

        // Always Max long: Unity fixes an array uniform's size on its first use.
        private static readonly Vector4[] Ground = new Vector4[FunnelShape.Max];
        private static readonly Vector4[] Top = new Vector4[FunnelShape.Max];
        private static readonly Vector4[] Shape = new Vector4[FunnelShape.Max];
        private static readonly Vector4[] Bend = new Vector4[FunnelShape.Max];
        private static readonly Vector4[] Skirt = new Vector4[FunnelShape.Max];
        private static readonly Vector4[] SkirtColor = new Vector4[FunnelShape.Max];
        private static readonly Vector4[] Colour = new Vector4[FunnelShape.Max];
        private static readonly Vector4[] Spin = new Vector4[FunnelShape.Max];
        private static readonly Vector4[] Bound = new Vector4[FunnelShape.Max];
        private static readonly Vector4[] Look = new Vector4[FunnelShape.Max];

        private static readonly int IdCount = Shader.PropertyToID("_FunnelCount");
        private static readonly int IdGround = Shader.PropertyToID("_FunnelGround");
        private static readonly int IdTop = Shader.PropertyToID("_FunnelTop");
        private static readonly int IdShape = Shader.PropertyToID("_FunnelShape");
        private static readonly int IdBend = Shader.PropertyToID("_FunnelBend");
        private static readonly int IdSkirt = Shader.PropertyToID("_FunnelSkirt");
        private static readonly int IdSkirtColor = Shader.PropertyToID("_FunnelSkirtColor");
        private static readonly int IdColour = Shader.PropertyToID("_FunnelColor");
        private static readonly int IdSpin = Shader.PropertyToID("_FunnelSpin");
        private static readonly int IdBound = Shader.PropertyToID("_FunnelBound");
        private static readonly int IdLook = Shader.PropertyToID("_FunnelLook");
        private static readonly int IdParams = Shader.PropertyToID("_FunnelParams");

        private static Vector4 _params;
        private static int _countLogged = -1;

        /// <summary>How many are drawn this frame.</summary>
        public static int Count { get; private set; }

        /// <summary>The i-th drawn this frame (0..Count-1), nearest first.</summary>
        public static Funnel Get(int i)
        {
            return _drawn[i];
        }

        /// <summary>Seconds of simulation since the city loaded (stops while paused): what the flecks fly by.</summary>
        public static double SimTime { get; private set; }

        /// <summary>
        /// The air's light low down a funnel, its skirt and its flecks are lit by, as the fog has it: its
        /// ambient and its sun (CloudVolume, each frame). Higher up the shader turns to the clouds' own.
        /// </summary>
        public static Color AirAmbient { get; set; }
        public static Color Sun { get; set; }

        /// <summary>
        /// Once a frame, from CloudLighting, after the wind and the rain: advances our waterspouts and
        /// packs what the shader draws.
        /// </summary>
        public static void Advance(CloudDensityField field, float deltaTime, float simulationRate)
        {
            SimTime += deltaTime * simulationRate;

            Camera camera = Camera.main;
            bool clouds = Settings.CloudsVisible == null || Settings.CloudsVisible.value;
            float cloudBase = CloudRain.CloudBottom;
            Vector3 eye = camera != null ? camera.transform.position : Vector3.zero;

            _all.Clear();
            Waterspouts.Advance(field, camera, clouds, deltaTime, simulationRate);
            if (clouds)
                Waterspouts.Collect(_all, cloudBase, CloudWeather.WindDirection, eye);

            // Each reaches up into the cloud it hangs from and fades out there: under the base alone it
            // hung below the clouds' visible bottom, "detached" (2026-10-18), as a cloud's bottom is
            // eaten away above where the layer begins. From under the base the shader shows that part
            // only as much as the cloud in front of it lets through (drawn over it, it stood "fully in
            // front of the clouds", 2026-10-19).
            float thickness = Settings.CloudThickness != null ? Settings.CloudThickness.value : Settings.Defaults.Thickness;
            float overlap = Mathf.Clamp(0.25f * thickness, 60f, 200f);
            for (int i = 0; i < _all.Count; i++)
            {
                Funnel f = _all[i];
                f.Overlap = overlap;
                _all[i] = f;
            }

            // The nearest, drawn in that order, which the shader composites nearest over farther.
            _all.Sort(Nearest);
            int count = Mathf.Min(_all.Count, FunnelShape.Max);
            for (int i = 0; i < count; i++)
                _drawn[i] = _all[i];

            for (int i = 0; i < FunnelShape.Max; i++)
                Pack(i, i < count ? _drawn[i] : new Funnel());
            Count = count;

            float pixelAngle = camera != null
                ? 2f * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, camera.pixelHeight)
                : 0.001f;
            _params = new Vector4(pixelAngle, Reach, FadeLength, 0f);

            // One line per change: they come and go a few times an hour.
            if (count != _countLogged)
            {
                _countLogged = count;
                Log.Msg(count > 0
                    ? "funnels: drawing " + count + " in the air pass -- " + Describe() + " | lit low down as the fog: ambient " +
                      AirAmbient.ToString("F2") + ", sun " + Sun.ToString("F2") + "; at the base by the clouds' own light"
                    : "funnels: none to draw");
            }
        }

        private sealed class NearestFirst : IComparer<Funnel>
        {
            public int Compare(Funnel a, Funnel b)
            {
                return a.Distance.CompareTo(b.Distance);
            }
        }

        private static readonly IComparer<Funnel> Nearest = new NearestFirst();

        private static void Pack(int i, Funnel f)
        {
            Ground[i] = new Vector4(f.Ground.x, f.Ground.y, f.Ground.z, f.GroundRadius);
            Top[i] = new Vector4(f.Top.x, f.Top.y, f.Top.z, f.TopRadius);
            Shape[i] = new Vector4(f.CollarRadius, f.Exponent, f.Bottom, f.Hollow);
            Bend[i] = new Vector4(f.Bend.x, f.Bend.y, f.Opacity, f.Density);
            Skirt[i] = new Vector4(f.SkirtRadius, f.SkirtHeight, f.SkirtRing, f.SkirtStrength);
            SkirtColor[i] = new Vector4(f.SkirtColor.r, f.SkirtColor.g, f.SkirtColor.b, f.SkirtDensity);
            Colour[i] = new Vector4(f.FunnelColor.r, f.FunnelColor.g, f.FunnelColor.b, f.Dirt);
            Spin[i] = new Vector4(f.Spin, f.Rise, f.Seed, f.Twist);
            Bound[i] = f.Top.y > f.Ground.y ? FunnelShape.Bound(f) : Vector4.zero;
            Look[i] = new Vector4(f.Thread, f.Overlap, f.Wisp, 0f);
        }

        /// <summary>Sets the funnels' uniforms on a cloud-pass material. Safe when nothing runs.</summary>
        public static void ApplyTo(Material material)
        {
            material.SetFloat(IdCount, Count);
            material.SetVectorArray(IdGround, Ground);
            material.SetVectorArray(IdTop, Top);
            material.SetVectorArray(IdShape, Shape);
            material.SetVectorArray(IdBend, Bend);
            material.SetVectorArray(IdSkirt, Skirt);
            material.SetVectorArray(IdSkirtColor, SkirtColor);
            material.SetVectorArray(IdColour, Colour);
            material.SetVectorArray(IdSpin, Spin);
            material.SetVectorArray(IdBound, Bound);
            material.SetVectorArray(IdLook, Look);
            material.SetVector(IdParams, _params);
        }

        /// <summary>For the frame line: "none", or "2: waterspout #2 1.4 km waterspout #3 6.0 km".</summary>
        public static string Describe()
        {
            if (Count == 0)
                return "none";

            string text = Count + ":";
            for (int i = 0; i < Count; i++)
                text += " waterspout #" + _drawn[i].Id + " " + (_drawn[i].Distance / 1000f).ToString("F1") + " km";
            return text;
        }

        /// <summary>On unload: nothing kept.</summary>
        public static void Clear()
        {
            Waterspouts.Clear(null);
            _all.Clear();
            Count = 0;
            SimTime = 0.0;
            _countLogged = -1;
            for (int i = 0; i < FunnelShape.Max; i++)
                Pack(i, new Funnel());
        }
    }
}
