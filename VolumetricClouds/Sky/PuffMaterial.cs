using System.Collections.Generic;
using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// Builds the material the cloud billboards render with.
    /// </summary>
    /// <remarks>
    /// Which built-in shaders survive into a Unity 5.6 build depends on what the project
    /// referenced, and that can't be read from the DLLs -- so try known names in order and
    /// report what was actually found rather than assuming one exists.
    /// </remarks>
    public static class PuffMaterial
    {
        private static readonly string[] ShaderCandidates =
        {
            "Particles/Alpha Blended",
            "Mobile/Particles/Alpha Blended",
            "Legacy Shaders/Particles/Alpha Blended",
            "Particles/Additive",
            "Sprites/Default",
            "Unlit/Transparent",
        };

        private const int TextureSize = 64;

        public static Material Create()
        {
            Shader shader = ResolveShader();
            if (shader == null)
            {
                Log.Error("No usable transparent shader found; clouds cannot render. " +
                          "Available shaders logged below.");
                DumpAvailableShaders();
                return null;
            }

            Material material = new Material(shader)
            {
                name = "VolumetricCloudsPuff",
                mainTexture = CreateTexture(),
            };

            // Transparent, after the skybox and the world.
            material.renderQueue = 3000;

            Log.Msg("cloud material using shader '" + shader.name + "'");
            return material;
        }

        private static Shader ResolveShader()
        {
            foreach (string name in ShaderCandidates)
            {
                Shader shader = Shader.Find(name);
                if (shader != null)
                    return shader;
            }

            return null;
        }

        private static void DumpAvailableShaders()
        {
            List<string> names = new List<string>();

            foreach (Shader shader in Resources.FindObjectsOfTypeAll<Shader>())
            {
                if (shader != null && !string.IsNullOrEmpty(shader.name))
                    names.Add(shader.name);
            }

            names.Sort();
            Log.Msg("available shaders (" + names.Count + "): " + string.Join(", ", names.ToArray()));
        }

        /// <summary>A soft round puff with a slightly ragged edge, so quads don't read as squares.</summary>
        private static Texture2D CreateTexture()
        {
            Texture2D texture = new Texture2D(TextureSize, TextureSize, TextureFormat.ARGB32, true)
            {
                name = "VolumetricCloudsPuffTexture",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            Color32[] pixels = new Color32[TextureSize * TextureSize];
            const float centre = (TextureSize - 1) * 0.5f;

            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    float dx = (x - centre) / centre;
                    float dy = (y - centre) / centre;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);

                    // Wobble the radius so the silhouette isn't a perfect circle.
                    float angle = Mathf.Atan2(dy, dx);
                    r *= 1f + 0.16f * Mathf.Sin(angle * 5f) + 0.10f * Mathf.Sin(angle * 9f + 1.7f);

                    float alpha = Mathf.Clamp01(1f - r);
                    alpha = alpha * alpha * (3f - 2f * alpha);

                    pixels[y * TextureSize + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(true);
            return texture;
        }
    }
}
