using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// On the cloud box: tells <see cref="CloudVolume"/> which camera is about to draw it (Smoother clouds,
    /// <see cref="CloudTemporal"/>). Unity calls this once per camera that will draw the box, after that
    /// camera has moved for the frame and before it draws anything.
    /// </summary>
    public sealed class CloudTemporalHook : MonoBehaviour
    {
        public CloudVolume Owner;

        private void OnWillRenderObject()
        {
            if (Owner != null)
                Owner.OnBoxWillRender(Camera.current);
        }
    }
}
