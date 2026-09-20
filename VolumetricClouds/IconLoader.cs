using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace VolumetricClouds
{
    /// <summary>Loads the mod icon out of the assembly's embedded resources.</summary>
    internal static class IconLoader
    {
        private const string ResourceName = "VolumetricClouds.Resources.CloudIcon.png";

        private static Texture2D _cached;

        public static Texture2D Load()
        {
            if (_cached != null)
                return _cached;

            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
                {
                    if (stream == null)
                    {
                        Debug.LogWarning("[VolumetricClouds] Embedded icon '" + ResourceName + "' not found.");
                        return null;
                    }

                    byte[] data = ReadFully(stream);
                    Texture2D texture = new Texture2D(1, 1, TextureFormat.ARGB32, false);
                    texture.LoadImage(data);
                    texture.name = "VolumetricCloudsIcon";
                    _cached = texture;
                    return _cached;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return null;
            }
        }

        private static byte[] ReadFully(Stream stream)
        {
            byte[] buffer = new byte[stream.Length];
            int offset = 0;

            while (offset < buffer.Length)
            {
                int read = stream.Read(buffer, offset, buffer.Length - offset);
                if (read <= 0)
                    break;
                offset += read;
            }

            return buffer;
        }
    }
}
