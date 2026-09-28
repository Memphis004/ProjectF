using UnityEngine;

// ReSharper disable CheckNamespace
namespace ProjectF.Presentation.Common
{
    /// <summary>
    /// Baked sprite table asset for the UI (spec 9.4). The UiPrefabGenerator
    /// bakes AssetDatabase references into one instance (saved at
    /// Settings/SpriteRegistry.asset) and RootLifetimeScope serializes it —
    /// so the runtime needs NO editor APIs and NO Resources folder for art.
    ///
    /// FILE-NAME RULE: this ScriptableObject class lives in a file named
    /// SpriteRegistryAsset.cs — Unity resolves a ScriptableObject asset's
    /// MonoScript by CLASS NAME == FILE NAME; putting it beside SpriteRegistry
    /// made the generated asset script-less ("No script asset for
    /// SpriteRegistryAsset") and every field reference null.
    /// </summary>
    [CreateAssetMenu(
        fileName = "SpriteRegistry",
        menuName = "ProjectF/Sprite Registry",
        order = 1)]
    public sealed class SpriteRegistryAsset : ScriptableObject
    {
        public Sprite? WhiteSquareSprite;

        public Sprite? PanelSprite;

        public int[] ItemIconIds = System.Array.Empty<int>();

        public Sprite[] ItemIconSprites = System.Array.Empty<Sprite>();
    }
}
