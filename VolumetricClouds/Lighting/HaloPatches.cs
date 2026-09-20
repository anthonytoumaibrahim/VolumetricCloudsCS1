using HarmonyLib;
using UnityEngine;

namespace VolumetricClouds.Lighting
{
    /// <summary>
    /// Adjusts the size of dynamic lights and suppresses their volume pass near the camera.
    /// Parameter names must match the game's, which is how Harmony binds them. The prefix
    /// returns true: returning false would skip the original and the light with it.
    /// </summary>
    [HarmonyPatch(typeof(LightSystem), nameof(LightSystem.DrawLight), new[]
    {
        typeof(LightType), typeof(Vector3), typeof(Vector3), typeof(Vector3), typeof(Color),
        typeof(float), typeof(float), typeof(float), typeof(float), typeof(bool),
    })]
    public static class DrawLightParametersPatch
    {
        public static bool Prefix(Vector3 pos, ref Vector3 vel, ref float intensity, ref float range, ref bool volume)
        {
            return HaloAdjuster.Adjust(pos, ref vel, ref intensity, ref range, ref volume);
        }
    }

    /// <summary>
    /// Notes when a LAMP is about to be drawn dynamically, so the DrawLight prefix can tag it.
    /// </summary>
    /// <remarks>
    /// LightEffect.RenderEffect opens with "if (m_batchedLight &amp;&amp; !id.IsEmpty) return": a
    /// lamp-type light only continues to DrawLight when it has NO instance id. Intersection
    /// Marking Tool renders its props with a default InstanceID (and only batches them into
    /// their own layer, never the light layer), so its lamps arrive here -- through the game's
    /// instanced halo shader, which knew none of the halo settings. The argument types are
    /// spelled out so an overload added by a game update cannot make PatchAll fail for every
    /// patch in the mod. A finalizer rather than a postfix: the flag must not stay set if the
    /// original throws.
    /// </remarks>
    [HarmonyPatch(typeof(LightEffect), nameof(LightEffect.RenderEffect), new[]
    {
        typeof(InstanceID), typeof(EffectInfo.SpawnArea), typeof(Vector3), typeof(float),
        typeof(float), typeof(float), typeof(float), typeof(RenderManager.CameraInfo),
    })]
    public static class LampRenderEffectPatch
    {
        public static void Prefix(LightEffect __instance, InstanceID id)
        {
            HaloAdjuster.InLampEffect = __instance.m_batchedLight && id.IsEmpty;
        }

        public static void Finalizer()
        {
            HaloAdjuster.InLampEffect = false;
        }
    }

    /// <summary>
    /// Counts calls only. Confirmed unused in practice, but kept as a tripwire in case
    /// another code path starts routing through it.
    /// </summary>
    [HarmonyPatch(typeof(LightSystem), nameof(LightSystem.DrawLight), new[] { typeof(LightData) })]
    public static class DrawLightDataPatch
    {
        public static void Prefix()
        {
            HaloAdjuster.CountLightDataCall();
        }
    }
}
