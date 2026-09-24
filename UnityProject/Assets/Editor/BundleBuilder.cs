using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Builds the shader bundles the mod embeds: one per platform, because a bundle only holds
/// code for the graphics APIs of its build target (and the Mac's Metal can only come out of
/// a Mac-target build). Run headless:
///   Unity.exe -batchmode -nographics -quit -projectPath . -executeMethod BundleBuilder.Build
/// Needs the Mac and Linux Build Support modules of this exact editor version installed.
/// </summary>
public static class BundleBuilder
{
    private const string OutputDir = "Bundles";
    private const string BundleName = "volumetricclouds";

    /// <summary>Every shader the mod ships. Add new ones here and nowhere else.</summary>
    private static readonly string[] Shaders =
    {
        "Assets/VolumetricClouds/CloudRaymarch.shader",
        "Assets/VolumetricClouds/CloudShadowMap.shader",
        "Assets/VolumetricClouds/LightHalo.shader",
        "Assets/VolumetricClouds/LightHaloDynamic.shader",
        "Assets/VolumetricClouds/RainDrops.shader",
        "Assets/VolumetricClouds/LightningBolt.shader",
        "Assets/VolumetricClouds/Invisible.shader",
        "Assets/VolumetricClouds/FogLamps.shader",
    };

    private struct Target
    {
        public string Name;
        public BuildTarget BuildTarget;
        public GraphicsDeviceType[] Apis;
    }

    /// <summary>
    /// The three bundles and the graphics APIs each carries. The names are the resource
    /// names ("volumetricclouds-win.bundle"...) ShaderBundle picks from by Application.platform,
    /// and build-bundle.ps1 checks each debug copy against the same platform lists.
    /// - Windows: no D3D9 any more. The target-4.0 raymarch never had D3D9 code, so the mod
    ///   stands down there anyway; OpenGL Core is for a game started with -force-glcore.
    /// - Linux: the game's Linux player runs OpenGL Core. No Vulkan: the player was not
    ///   built with it, and a launch option can only pick an API the player has.
    /// - Mac: the game runs Metal on every Mac seen (read from a Mac's Player.log); OpenGL
    ///   Core beside it is cheap.
    /// </summary>
    private static readonly Target[] Targets =
    {
        new Target
        {
            Name = "win",
            BuildTarget = BuildTarget.StandaloneWindows64,
            Apis = new[] { GraphicsDeviceType.Direct3D11, GraphicsDeviceType.OpenGLCore },
        },
        new Target
        {
            Name = "linux",
            BuildTarget = BuildTarget.StandaloneLinux64,
            Apis = new[] { GraphicsDeviceType.OpenGLCore },
        },
        new Target
        {
            Name = "mac",
            BuildTarget = BuildTarget.StandaloneOSXUniversal,
            Apis = new[] { GraphicsDeviceType.Metal, GraphicsDeviceType.OpenGLCore },
        },
    };

    /// <summary>
    /// The shipped bundles, Bundles/[name]/volumetricclouds, AND their uncompressed twins in
    /// Bundles/debug/[name]/ from the same run, so tools/bundle-apis.ps1 can prove what each
    /// one holds and tools/shaderdump.ps1 can disassemble OUR compiled shaders. Only the
    /// compressed ones are embedded.
    /// </summary>
    public static void Build()
    {
        foreach (Target target in Targets)
        {
            if (!BuildOne(target, false) || !BuildOne(target, true))
            {
                EditorApplication.Exit(1);
                return;
            }
        }

        Debug.Log("BUNDLE BUILD ALL OK");
    }

    /// <summary>Verification only: the uncompressed copies alone. Never shipped.</summary>
    public static void BuildUncompressed()
    {
        foreach (Target target in Targets)
        {
            if (!BuildOne(target, true))
            {
                EditorApplication.Exit(1);
                return;
            }
        }

        Debug.Log("DEBUG BUNDLE ALL OK");
    }

    private static bool BuildOne(Target target, bool uncompressed)
    {
        // Set from the script every time, never left as hidden state in the binary
        // ProjectSettings.asset: what each bundle holds must be readable here.
        PlayerSettings.SetUseDefaultGraphicsAPIs(target.BuildTarget, false);
        PlayerSettings.SetGraphicsAPIs(target.BuildTarget, target.Apis);

        GraphicsDeviceType[] readBack = PlayerSettings.GetGraphicsAPIs(target.BuildTarget);
        string list = "";
        for (int i = 0; i < readBack.Length; i++)
            list += (i > 0 ? ", " : "") + readBack[i];

        string dir = uncompressed
            ? Path.Combine(Path.Combine(OutputDir, "debug"), target.Name)
            : Path.Combine(OutputDir, target.Name);
        Directory.CreateDirectory(dir);

        AssetBundleBuild build = new AssetBundleBuild
        {
            assetBundleName = BundleName,
            assetNames = Shaders,
        };

        // Always rebuild. The incremental check only hashes the .shader files themselves,
        // so an edit confined to CloudCommon.cginc would otherwise yield a byte-identical,
        // stale bundle -- with no error anywhere.
        BuildAssetBundleOptions options = BuildAssetBundleOptions.ForceRebuildAssetBundle;
        if (uncompressed)
            options |= BuildAssetBundleOptions.UncompressedAssetBundle;

        AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(dir, new[] { build }, options, target.BuildTarget);

        string what = (uncompressed ? "DEBUG BUNDLE" : "BUNDLE BUILD") + " " + target.Name +
                      " (" + target.BuildTarget + ", apis=[" + list + "])";

        if (manifest == null)
        {
            Debug.LogError(what + " FAILED");
            return false;
        }

        Debug.Log((uncompressed ? "DEBUG BUNDLE OK: " : "BUNDLE BUILD OK: ") + target.Name +
                  " -> " + Path.Combine(dir, BundleName) + "  [" + what + "]");
        return true;
    }
}
