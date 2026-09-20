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
            BuildAssetBundleOptions.None,
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
