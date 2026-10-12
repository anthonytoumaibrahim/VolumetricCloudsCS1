using ModsLibCS1.UI;

namespace VolumetricClouds.UI
{
    /// <summary>
    /// The panel's look (1.5.0): the "Panel look" row (Settings.PanelSkin) read as a ModsLibCS1
    /// skin, and a Builder for it. The panel, the profile bar, the colour rows and the HUD button
    /// all draw through ModsLibCS1; a skin change rebuilds the panel and the button.
    /// </summary>
    internal static class PanelLook
    {
        private static bool _sinkSet;

        /// <summary>The picked skin; a number this version doesn't know (a newer file) is the default.</summary>
        public static Skin Skin
        {
            get
            {
                int value = Settings.PanelSkin != null ? Settings.PanelSkin.value : Settings.Defaults.PanelSkin;
                switch (value)
                {
                    case (int)Skin.SeeThrough:
                    case (int)Skin.GameBlue:
                    case (int)Skin.Vanilla:
                    case (int)Skin.Frosted:
                        return (Skin)value;
                    default:
                        return (Skin)Settings.Defaults.PanelSkin;
                }
            }
        }

        /// <summary>A Builder for the picked skin: Vanilla's, logged, if the library's sprites can't be had.</summary>
        public static Builder NewBuilder()
        {
            if (!_sinkSet)
            {
                _sinkSet = true;
                LibLog.Sink = Log.Msg;
            }

            Builder builder = Builder.For(Skin);
            Log.Msg("panel look: " + builder.Skin + (builder.Skin != Skin ? " (asked for " + Skin + ")" : ""));
            return builder;
        }
    }
}
