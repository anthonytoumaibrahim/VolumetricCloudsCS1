using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// The shaders compiled in Unity 5.6 and embedded in this assembly as an AssetBundle.
    /// </summary>
    /// <remarks>
    /// Loaded once and the bundle released immediately (keeping the shaders alive): a
    /// bundle left loaded cannot be loaded a second time, which would break the mod's
    /// hot-reload loop.
    /// </remarks>
    public static class ShaderBundle
    {
        public const string Raymarch = "VolumetricClouds/CloudRaymarch";
        public const string ShadowMap = "VolumetricClouds/CloudShadowMap";
        public const string LightHalo = "VolumetricClouds/LightHalo";
        public const string LightHaloDynamic = "VolumetricClouds/LightHaloDynamic";
        public const string RainDrops = "VolumetricClouds/RainDrops";
        public const string LightningBolt = "VolumetricClouds/LightningBolt";
        public const string Invisible = "VolumetricClouds/Invisible";
        public const string FogLamps = "VolumetricClouds/FogLamps";

        private const string Resource = "VolumetricClouds.Resources.volumetricclouds.bundle";

        private static Dictionary<string, Shader> _shaders;

        /// <summary>Returns the named shader, or null if it is missing or unsupported here.</summary>
        public static Shader Get(string shaderName)
        {
            if (_shaders == null)
                _shaders = Load();

            Shader shader;
            if (!_shaders.TryGetValue(shaderName, out shader))
            {
                Log.Warn("Shader '" + shaderName + "' is not in the bundle.");
                return null;
            }

            if (!shader.isSupported)
            {
                Log.Error("Shader '" + shaderName + "' is not supported on this GPU/graphics API.");
                return null;
            }

            return shader;
        }

        private static Dictionary<string, Shader> Load()
        {
            Dictionary<string, Shader> result = new Dictionary<string, Shader>();

            try
            {
                byte[] data = ReadResource();
                if (data == null)
                {
                    Log.Warn("Shader bundle is not embedded in this build; the mod cannot draw and stands down. " +
                             "Run build-bundle.ps1, then rebuild the mod.");
                    return result;
                }

                AssetBundle bundle = AssetBundle.LoadFromMemory(data);
                if (bundle == null)
                {
                    Log.Error("Shader bundle failed to load (built with a mismatched Unity version?).");
                    return result;
                }

                foreach (string asset in bundle.GetAllAssetNames())
                {
                    if (!asset.EndsWith(".shader", StringComparison.OrdinalIgnoreCase))
                        continue;

                    Shader shader = bundle.LoadAsset<Shader>(asset);
                    if (shader != null)
                        result[shader.name] = shader;
                }

                bundle.Unload(false);

                string[] names = new string[result.Count];
                result.Keys.CopyTo(names, 0);
                Log.Msg("shader bundle loaded: " + string.Join(", ", names));
            }
            catch (Exception e)
            {
                Log.Error("Loading the shader bundle threw.", e);
            }

            return result;
        }

        private static byte[] ReadResource()
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource))
            {
                if (stream == null)
                    return null;

                byte[] data = new byte[stream.Length];
                int offset = 0;

                while (offset < data.Length)
                {
                    int read = stream.Read(data, offset, data.Length - offset);
                    if (read <= 0)
                        break;
                    offset += read;
                }

                return data;
            }
        }
    }
}
