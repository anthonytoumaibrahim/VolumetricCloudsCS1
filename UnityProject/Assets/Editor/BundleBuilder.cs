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

    public static void Build()
    {
        Directory.CreateDirectory(OutputDir);

        AssetBundleBuild build = new AssetBundleBuild
        {
            assetBundleName = BundleName,
            assetNames = new[]
            {
                "Assets/VolumetricClouds/CloudRaymarch.shader",
                "Assets/VolumetricClouds/CloudShadowMap.shader",
            },
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
