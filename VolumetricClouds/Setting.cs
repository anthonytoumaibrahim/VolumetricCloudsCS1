using System.Collections.Generic;
using UnityEngine;

namespace VolumetricClouds
{
    /// <summary>
    /// One saved setting: a value held in memory and written to VolumetricClouds.xml by
    /// <see cref="SettingsXml"/>. These replaced the game's SavedFloat / SavedBool / SavedInt,
    /// which live in its binary .cgs format; the lower-case <c>value</c> and <c>name</c> are
    /// theirs, kept so that no call site had to change.
    /// </summary>
    /// <remarks>
    /// Like a Saved*, a setting does not remember its default: the constructor's value is only
    /// what it holds until the file is read. <see cref="Settings.Defaults"/> and the catalog
    /// are what can put a default back.
    ///
    /// A value is read from any thread (the rain patch runs on the simulation thread) and
    /// written on the main thread only -- by the UIs, a reset, a preset, or a reload of the
    /// file. A plain field is all that needs.
    /// </remarks>
    public abstract class Setting
    {
        private static readonly List<Setting> Registry = new List<Setting>();

        protected Setting(string name, string legacyKey)
        {
            this.name = name;
            LegacyKey = legacyKey ?? name;
            Registry.Add(this);
        }

        /// <summary>The element name in VolumetricClouds.xml (and, in 1.1, in a profile).</summary>
        public string name { get; private set; }

        /// <summary>
        /// The key it had in the retired VolumetricClouds.cgs, read once by the import. Differs
        /// from <see cref="name"/> only where the old key had outlived its meaning.
        /// </summary>
        public string LegacyKey { get; private set; }

        /// <summary>Every setting ever constructed, so the file can say which one has no catalog row.</summary>
        public static IList<Setting> All
        {
            get { return Registry; }
        }

        protected static void Changed()
        {
            SettingsXml.MarkDirty();
        }
    }

    public sealed class FloatSetting : Setting
    {
        private float _value;

        public FloatSetting(string name, float value, string legacyKey = null)
            : base(name, legacyKey)
        {
            _value = value;
        }

        public float value
        {
            get { return _value; }
            set
            {
                if (_value.Equals(value))
                    return;

                _value = value;
                Changed();
            }
        }
    }

    public sealed class BoolSetting : Setting
    {
        private bool _value;

        public BoolSetting(string name, bool value, string legacyKey = null)
            : base(name, legacyKey)
        {
            _value = value;
        }

        public bool value
        {
            get { return _value; }
            set
            {
                if (_value == value)
                    return;

                _value = value;
                Changed();
            }
        }
    }

    public sealed class IntSetting : Setting
    {
        private int _value;

        public IntSetting(string name, int value, string legacyKey = null)
            : base(name, legacyKey)
        {
            _value = value;
        }

        public int value
        {
            get { return _value; }
            set
            {
                if (_value == value)
                    return;

                _value = value;
                Changed();
            }
        }
    }

    /// <summary>
    /// A colour as the player picked it: sRGB, 0..255 a channel, always opaque. Written to the
    /// file as #RRGGBB (<see cref="ColorText"/>). What it DOES to the picture is its user's
    /// business -- the cloud colours go through <see cref="Sky.CloudTint"/>.
    /// </summary>
    public sealed class ColorSetting : Setting
    {
        private Color32 _value;

        public ColorSetting(string name, Color32 value)
            : base(name, null)
        {
            _value = Opaque(value);
        }

        public Color32 value
        {
            get { return _value; }
            set
            {
                value = Opaque(value);
                if (ColorText.Same(_value, value))
                    return;

                _value = value;
                Changed();
            }
        }

        private static Color32 Opaque(Color32 colour)
        {
            return new Color32(colour.r, colour.g, colour.b, 255);
        }
    }
}
