using System.Collections.Generic;
using UnityEngine;

// ReSharper disable CheckNamespace
namespace ProjectF.Presentation.Common
{
    /// <summary>Plain-C# view over the baked <see cref="SpriteRegistryAsset"/>
    /// (registered Singleton on the root scope; windows/presenters inject it
    /// normally). The asset + the scene-side carrier live in their own files —
    /// Unity requires MonoBehaviour/ScriptableObject class name == file name.</summary>
    public sealed class SpriteRegistry
    {
        private readonly SpriteRegistryAsset asset;
        private readonly Dictionary<int, Sprite> icons;

        public SpriteRegistry(SpriteRegistryAsset asset)
        {
            this.asset = asset;

            icons = new Dictionary<int, Sprite>(asset.ItemIconIds.Length);
            for (int i = 0; i < asset.ItemIconIds.Length &&
                            i < asset.ItemIconSprites.Length; i++)
            {
                icons[asset.ItemIconIds[i]] = asset.ItemIconSprites[i];
            }
        }

        /// <summary>1x1 white sprite for dots/bars/fills (HUD bars, toast icons).</summary>
        public Sprite? WhiteSquare => asset.WhiteSquareSprite;

        /// <summary>9-slice panel frame sprite (windows, tooltip, toasts).</summary>
        public Sprite? Panel => asset.PanelSprite;

        /// <summary>Item icon by item id; unknown ids fall back to the white
        /// square (tinted by the caller) — never a hard failure.</summary>
        public Sprite? GetItemIcon(int itemId) =>
            icons.TryGetValue(itemId, out Sprite? sprite) ? sprite : asset.WhiteSquareSprite;

        /// <summary>Whether a baked icon exists for the id (InventoryWindow
        /// uses the TbItem table for the grid; this only answers "is there art").</summary>
        public bool HasIcon(int itemId) => icons.ContainsKey(itemId);
    }
}
