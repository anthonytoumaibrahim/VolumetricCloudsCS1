using ICities;
using UnityEngine;

namespace VolumetricClouds
{
    /// <summary>
    /// Discovered automatically by the game alongside <see cref="Mod"/>. Owns the
    /// controller GameObject for the lifetime of a loaded city.
    /// </summary>
    public class Loader : LoadingExtensionBase
    {
        private static GameObject _root;

        public override void OnLevelLoaded(LoadMode mode)
        {
            base.OnLevelLoaded(mode);

            if (mode != LoadMode.NewGame && mode != LoadMode.LoadGame)
                return;

            if (_root != null)
                return;

            _root = new GameObject("VolumetricCloudsController");
            Object.DontDestroyOnLoad(_root);
            _root.AddComponent<ModController>();

            Debug.Log("[VolumetricClouds] Controller created.");
        }

        public override void OnLevelUnloading()
        {
            base.OnLevelUnloading();

            if (_root == null)
                return;

            Object.Destroy(_root);
            _root = null;
        }
    }
}
