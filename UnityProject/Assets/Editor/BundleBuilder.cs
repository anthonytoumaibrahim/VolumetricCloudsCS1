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

    /// <summary>
    /// Every shader the mod ships. Add new ones here and nowhere else: build-bundle.ps1 reads
    /// this list out of this file and makes tools/bundle-apis.ps1 check every name in it.
    /// </summary>
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
    /// and build-bundle.ps1 checks each bundle against the same platform lists.
    /// - Windows: no D3D9 any more. The target-3.5 raymarch never had D3D9 code, so the mod
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
    /// One UNCOMPRESSED bundle per target in Bundles/[name]/volumetricclouds, and that exact
    /// file is what ships. Uncompressed on purpose: tools/bundle-apis.ps1 can only read an
    /// uncompressed bundle, and what it verifies must be the bytes that are embedded, not a
    /// twin from a second compile (the LZMA-compressed shipped bundle of 1.1.0 and the
    /// uncompressed debug copy were two builds). ~115 KB instead of ~45 KB per bundle inside
    /// a 400 KB DLL is nothing; AssetBundle.LoadFromMemory takes either.
    /// </summary>
    public static void Build()
    {
        foreach (Target target in Targets)
        {
            if (!BuildOne(target))
            {
                EditorApplication.Exit(1);
                return;
            }
        }

        Debug.Log("BUNDLE BUILD ALL OK");
    }

    private static bool BuildOne(Target target)
    {
        // Set from the script every time, never left as hidden state in the binary
        // ProjectSettings.asset: what each bundle holds must be readable here.
        PlayerSettings.SetUseDefaultGraphicsAPIs(target.BuildTarget, false);
        PlayerSettings.SetGraphicsAPIs(target.BuildTarget, target.Apis);

        GraphicsDeviceType[] readBack = PlayerSettings.GetGraphicsAPIs(target.BuildTarget);
        string list = "";
        for (int i = 0; i < readBack.Length; i++)
            list += (i > 0 ? ", " : "") + readBack[i];

        string dir = Path.Combine(OutputDir, target.Name);
        Directory.CreateDirectory(dir);

        AssetBundleBuild build = new AssetBundleBuild
        {
            assetBundleName = BundleName,
            assetNames = Shaders,
        };

        // Always rebuild. The incremental check only hashes the .shader files themselves,
        // so an edit confined to CloudCommon.cginc would otherwise yield a byte-identical,
        // stale bundle -- with no error anywhere.
        AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(
            dir,
            new[] { build },
            BuildAssetBundleOptions.ForceRebuildAssetBundle | BuildAssetBundleOptions.UncompressedAssetBundle,
            target.BuildTarget);

        string what = "BUNDLE BUILD " + target.Name + " (" + target.BuildTarget + ", apis=[" + list + "])";

        if (manifest == null)
        {
            Debug.LogError(what + " FAILED");
            return false;
        }

        Debug.Log("BUNDLE BUILD OK: " + target.Name + " -> " + Path.Combine(dir, BundleName) + "  [" + what + "]");
        return true;
    }
}
