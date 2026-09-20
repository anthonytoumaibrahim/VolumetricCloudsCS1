using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the shader bundle the mod embeds. Run headless:
///   Unity.exe -batchmode -nographics -quit -projectPath . -executeMethod BundleBuilder.Build
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
    };

    /// <summary>
    /// Verification only: an uncompressed copy in Bundles/debug, so tools/shaderdump.ps1 can
    /// find and disassemble OUR compiled shaders -- to diff a port against the game's
    /// original, or simply to prove a shader really compiled. Never shipped.
    /// </summary>
    public static void BuildUncompressed()
    {
        string dir = Path.Combine(OutputDir, "debug");
        Directory.CreateDirectory(dir);

        AssetBundleBuild build = new AssetBundleBuild
        {
            assetBundleName = BundleName,
            assetNames = Shaders,
        };

        BuildPipeline.BuildAssetBundles(
            dir,
            new[] { build },
            BuildAssetBundleOptions.ForceRebuildAssetBundle | BuildAssetBundleOptions.UncompressedAssetBundle,
            BuildTarget.StandaloneWindows64);

        Debug.Log("UNCOMPRESSED DEBUG BUNDLE: " + Path.Combine(dir, BundleName));
    }

    public static void Build()
    {
        Directory.CreateDirectory(OutputDir);

        AssetBundleBuild build = new AssetBundleBuild
        {
            assetBundleName = BundleName,
            assetNames = Shaders,
        };

        AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(
            OutputDir,
            new[] { build },
            // Always rebuild. The incremental check only hashes the .shader files themselves,
            // so an edit confined to CloudCommon.cginc would otherwise yield a byte-identical,
            // stale bundle -- with no error anywhere.
            BuildAssetBundleOptions.ForceRebuildAssetBundle,
            BuildTarget.StandaloneWindows64);

        if (manifest == null)
        {
            Debug.LogError("BUNDLE BUILD FAILED");
            EditorApplication.Exit(1);
            return;
        }

        Debug.Log("BUNDLE BUILD OK: " + Path.Combine(OutputDir, BundleName));
    }
}
