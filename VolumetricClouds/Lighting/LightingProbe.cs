using System;
using System.Reflection;
using ColossalFramework;
using UnityEngine;

namespace VolumetricClouds.Lighting
{
    /// <summary>
    /// One-shot dump of the game's light rendering setup. Shader and material names
    /// can't be read from the DLLs -- they live in asset data -- so they have to be
    /// read off the live objects.
    /// </summary>
    public static class LightingProbe
    {
        private const BindingFlags AnyInstance =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static void Dump()
        {
            try
            {
                DumpLightSystem();
                DumpRenderProperties();
            }
            catch (Exception e)
            {
                Log.Error("Lighting probe failed.", e);
            }
        }

        private static void DumpLightSystem()
        {
            RenderManager render = Singleton<RenderManager>.instance;
            LightSystem system = render != null ? render.lightSystem : null;

            if (system == null)
            {
                Log.Warn("probe: RenderManager.lightSystem is null.");
                return;
            }

            foreach (string field in new[]
                     {
                         "m_lightMaterial", "m_lightMaterialVolume",
                         "m_lightMaterialVolumeGroup", "m_lightFloatingMaterialVolumeGroup",
                     })
            {
                Material material = GetField(system, field) as Material;
                if (material == null)
                {
                    Log.Msg("probe: " + field + " = null");
                    continue;
                }

                Shader shader = material.shader;
                Log.Msg("probe: " + field + " material='" + material.name +
                        "' shader='" + (shader == null ? "null" : shader.name) + "'" +
                        " renderQueue=" + material.renderQueue +
                        " keywords=[" + string.Join(",", material.shaderKeywords) + "]");
            }

            // A box mesh here would confirm the proxy-volume theory for the artifact.
            Mesh mesh = GetField(system, "m_lightMesh") as Mesh;
            if (mesh != null)
            {
                Log.Msg("probe: m_lightMesh name='" + mesh.name + "' verts=" + mesh.vertexCount +
                        " tris=" + (mesh.triangles == null ? 0 : mesh.triangles.Length / 3) +
                        " bounds=" + mesh.bounds);
            }
        }

        private static void DumpRenderProperties()
        {
            RenderProperties properties = UnityEngine.Object.FindObjectOfType<RenderProperties>();
            if (properties == null)
            {
                Log.Warn("probe: no RenderProperties in the scene.");
                return;
            }

            Log.Msg("probe: RenderProperties inscattering" +
                    " color=" + properties.m_inscatteringColor +
                    " exponent=" + properties.m_inscatteringExponent +
                    " intensity=" + properties.m_inscatteringIntensity +
                    " startDistance=" + properties.m_inscatteringStartDistance +
                    " transitionSoftness=" + properties.m_inscatteringTransitionSoftness);

            Log.Msg("probe: RenderProperties shaders" +
                    " light='" + ShaderName(properties.m_lightShader) + "'" +
                    " lightVolume='" + ShaderName(properties.m_lightVolumeShader) + "'" +
                    " lightFloatingVolume='" + ShaderName(properties.m_lightFloatingVolumeShader) + "'");
        }

        private static string ShaderName(Shader shader)
        {
            return shader == null ? "null" : shader.name;
        }

        private static object GetField(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, AnyInstance);
            return field == null ? null : field.GetValue(target);
        }
    }
}
