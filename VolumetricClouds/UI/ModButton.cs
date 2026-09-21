using ColossalFramework.UI;
using UnityEngine;

namespace VolumetricClouds.UI
{
    /// <summary>
    /// Standalone HUD button, used when Unified UI isn't available or the player has
    /// turned the UUI icon off.
    /// </summary>
    public class ModButton : UIButton
    {
        /// <summary>Matches Unified UI's own button size so the two look consistent.</summary>
        public const float ButtonSize = 40f;

        private const string SpriteName = "VolumetricCloudsIcon";
        private static readonly Vector3 DefaultPosition = new Vector3(10f, 60f);

        public System.Action OnClicked;

        public override void Start()
        {
            base.Start();

            name = "VolumetricCloudsButton";
            size = new Vector2(ButtonSize, ButtonSize);
            tooltip = Mod.DisplayName;
            relativePosition = DefaultPosition;

            Texture2D icon = IconLoader.Load();
            if (icon != null)
            {
                atlas = CreateAtlas(icon);
                normalFgSprite = SpriteName;
            }
            else
            {
                // Without an icon the button would be invisible, so fall back to text.
                text = "VC";
                textScale = 0.8f;
            }

            UITextureAtlas defaultAtlas = UIView.GetAView().defaultAtlas;
            if (defaultAtlas != null && defaultAtlas.spriteNames != null)
            {
                foreach (string candidate in new[] { "RoundBackBigDisabled", "GenericPanel", "ButtonMenu" })
                {
                    if (System.Array.IndexOf(defaultAtlas.spriteNames, candidate) >= 0)
                    {
                        // A separate component so the background atlas doesn't fight the icon atlas.
                        UISprite background = AddUIComponent<UISprite>();
                        background.atlas = defaultAtlas;
                        background.spriteName = candidate;
                        background.size = size;
                        background.relativePosition = Vector3.zero;
                        background.SendToBack();
                        break;
                    }
                }
            }

            eventClick += (component, e) =>
            {
                if (OnClicked != null)
                    OnClicked();
            };
        }

        /// <summary>Wraps a single texture in a one-sprite atlas so a UIButton can show it.</summary>
        private static UITextureAtlas CreateAtlas(Texture2D texture)
        {
            UITextureAtlas atlas = ScriptableObject.CreateInstance<UITextureAtlas>();
            atlas.name = "VolumetricCloudsAtlas";

            Material template = UIView.GetAView().defaultAtlas.material;
            Material material = Object.Instantiate(template);
            material.mainTexture = texture;
            atlas.material = material;

            atlas.AddSprite(new UITextureAtlas.SpriteInfo
            {
                name = SpriteName,
                texture = texture,
                region = new Rect(0f, 0f, 1f, 1f),
            });

            return atlas;
        }
    }
}
