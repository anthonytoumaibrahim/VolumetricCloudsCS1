using System;
using System.Globalization;
using System.IO;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// What a savegame remembers of the sky: which pattern, and how far it has drifted. The
    /// record and its bytes; <see cref="SkySaveData"/> puts them in the save. Pure: no engine
    /// calls, so tools/test-skystate.ps1 runs it against the built DLL.
    /// </summary>
    /// <remarks>
    /// The bytes live under our own key in the game's dictionary of mod data
    /// (SimulationManager.m_serializableDataStorage), which the game reads and writes back
    /// whole without knowing whose each entry is: a save loads the same with the mod gone, and
    /// keeps our bytes for when it comes back.
    ///
    /// FORMAT RULES, so that no version can misread an older or newer save:
    /// - Append only. A field is never reordered, removed or given a new meaning; appending
    ///   one bumps <see cref="CurrentVersion"/>.
    /// - A reader takes the fields it knows and ignores the rest, so an older mod still reads
    ///   a newer save's sky. A record shorter than version 1 is refused.
    /// - A seed means the same sky only while the generators are unchanged:
    ///   CloudDensityField.Generate and its Resolution, CloudNoise3D.Generate and
    ///   CloudVolume.NoiseSize, and the lookups' tiles and drift rates (CloudWind.NoiseDrift,
    ///   the CloudFog constants, Drift.FogLead). Changing one on purpose is fine -- old saves
    ///   get a different pattern -- but say so with a generator version in a NEW field.
    /// </remarks>
    public struct SkyState
    {
        public const byte CurrentVersion = 1;

        /// <summary>Bytes in a version-1 record: 1 + 2 x 4 + 4 x 8 + 5 x 4.</summary>
        public const int Version1Length = 61;

        public int FieldSeed;
        public int NoiseSeed;

        /// <summary>CloudWind's offset, metres, unwrapped: the shaders' phases are derived from it.</summary>
        public double WindX;
        public double WindZ;

        /// <summary>CloudFog's drift, metres, unwrapped.</summary>
        public double FogX;
        public double FogZ;

        /// <summary>CloudFog's swirl phase, 0..1.</summary>
        public float FogBoil;

        /// <summary>
        /// CloudFog's smoothed amount, 0..1. It eases towards the game's fog after a load; with
        /// this, a foggy city opens foggy instead of growing its fog back in.
        /// </summary>
        public float FogAmount;

        /// <summary>
        /// CloudRain.FallOffset, metres, already wrapped: how far the rain has fallen. The
        /// curtains' pattern slides down by it, so without it a rainy city would open with its
        /// curtains jumped.
        /// </summary>
        public float RainFallX;
        public float RainFallY;
        public float RainFallZ;

        public byte[] ToBytes()
        {
            using (MemoryStream stream = new MemoryStream(Version1Length))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(CurrentVersion);
                writer.Write(FieldSeed);
                writer.Write(NoiseSeed);
                writer.Write(WindX);
                writer.Write(WindZ);
                writer.Write(FogX);
                writer.Write(FogZ);
                writer.Write(FogBoil);
                writer.Write(FogAmount);
                writer.Write(RainFallX);
                writer.Write(RainFallY);
                writer.Write(RainFallZ);
                writer.Flush();
                return stream.ToArray();
            }
        }

        /// <summary>
        /// Reads a record. False, with the reason in <paramref name="note"/>, for anything that
        /// is not one; true for a record of this version or a newer one (whose extra fields are
        /// ignored, and <paramref name="note"/> says so). Never throws.
        /// </summary>
        public static bool TryRead(byte[] data, out SkyState state, out string note)
        {
            state = default(SkyState);
            note = null;

            if (data == null || data.Length == 0)
            {
                note = "empty";
                return false;
            }

            byte version = data[0];
            if (version == 0)
            {
                note = "version 0, which no version of the mod writes";
                return false;
            }

            if (data.Length < Version1Length)
            {
                note = data.Length + " bytes, fewer than the " + Version1Length + " of a version-1 record";
                return false;
            }

            try
            {
                using (BinaryReader reader = new BinaryReader(new MemoryStream(data, false)))
                {
                    reader.ReadByte();
                    state.FieldSeed = reader.ReadInt32();
                    state.NoiseSeed = reader.ReadInt32();
                    state.WindX = reader.ReadDouble();
                    state.WindZ = reader.ReadDouble();
                    state.FogX = reader.ReadDouble();
                    state.FogZ = reader.ReadDouble();
                    state.FogBoil = reader.ReadSingle();
                    state.FogAmount = reader.ReadSingle();
                    state.RainFallX = reader.ReadSingle();
                    state.RainFallY = reader.ReadSingle();
                    state.RainFallZ = reader.ReadSingle();
                }
            }
            catch (Exception e)
            {
                state = default(SkyState);
                note = e.GetType().Name + ": " + e.Message;
                return false;
            }

            if (!Finite(state.WindX) || !Finite(state.WindZ) || !Finite(state.FogX) || !Finite(state.FogZ)
                || !Finite(state.FogBoil) || !Finite(state.FogAmount)
                || !Finite(state.RainFallX) || !Finite(state.RainFallY) || !Finite(state.RainFallZ))
            {
                state = default(SkyState);
                note = "a drift that is not a number";
                return false;
            }

            if (version > CurrentVersion)
                note = "version " + version + ", newer than this mod's " + CurrentVersion + "; its version-1 part was read";

            return true;
        }

        /// <summary>One line for the log, the same in every language.</summary>
        public string Describe()
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            return "pattern seeds " + FieldSeed.ToString(c) + "/" + NoiseSeed.ToString(c) +
                   ", wind drift (" + WindX.ToString("F0", c) + ", " + WindZ.ToString("F0", c) + ") m" +
                   ", fog drift (" + FogX.ToString("F0", c) + ", " + FogZ.ToString("F0", c) + ") m" +
                   ", swirl " + FogBoil.ToString("F3", c) +
                   ", fog amount " + (FogAmount * 100f).ToString("F0", c) + "%" +
                   ", rain fallen " + (-RainFallY).ToString("F0", c) + " m";
        }

        private static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
