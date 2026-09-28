using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectF.Editor
{
    /// <summary>
    /// Shared paths, sizes and category colours for the Stage 8 generators.
    /// knowledge.md "Editor-generated content": everything these constants
    /// point at is produced by editor scripts under Assets/Main/Editor/,
    /// never by hand.
    /// </summary>
    internal static class EditorPaths
    {
        public const string ArtRoot = "Assets/Main/Art/Placeholder";
        public const string PrefabRoot = "Assets/Main/Prefabs";
        public const string SceneRoot = "Assets/Main/Scenes";
        public const string SettingsRoot = "Assets/Main/Settings";
        public const string UiRoot = "Assets/Main/Art/Placeholder/UI";
        public const string ResourcesRoot = "Assets/Main/Resources";
        public const string LocalizationRoot = "Assets/Main/Resources/Localization";

        public const int PixelsPerUnit = 16;
        public const int TileSize = 16;
        public const int PlayerSheetW = 16;
        public const int PlayerSheetH = 32;
        public const int PlayerDirections = 4; // Down, Left, Right, Up rows
        public const int PlayerFrames = 4;     // columns
        public const int PanelSize = 32;

        public static readonly Color Grass = new(0.36f, 0.62f, 0.28f);
        public static readonly Color Water = new(0.25f, 0.45f, 0.75f);
        public static readonly Color Soil = new(0.45f, 0.32f, 0.20f);
        public static readonly Color WoodFloor = new(0.62f, 0.48f, 0.30f);
        public static readonly Color Path = new(0.78f, 0.70f, 0.52f);
        public static readonly Color Player = new(0.90f, 0.75f, 0.60f);
        public static readonly Color NpcShopkeeper = new(0.45f, 0.55f, 0.85f);
        public static readonly Color NpcAuntie = new(0.85f, 0.55f, 0.70f);
        public static readonly Color FishingSpot = new(0.20f, 0.55f, 0.70f);
        public static readonly Color PanelFrame = new(0.85f, 0.82f, 0.75f);

        /// <summary>Item-category hues — flat colour keyed by category so any
        /// item id is visually identifiable before real art lands.</summary>
        public static readonly Dictionary<string, Color> CategoryHues =
            new(StringComparer.Ordinal)
            {
                ["Bait"] = new Color(0.85f, 0.75f, 0.35f),
                ["Rod"] = new Color(0.55f, 0.38f, 0.20f),
                ["Fish"] = new Color(0.30f, 0.60f, 0.80f),
                ["Seed"] = new Color(0.60f, 0.75f, 0.35f),
                ["Crop"] = new Color(0.90f, 0.60f, 0.25f),
                ["Material"] = new Color(0.60f, 0.60f, 0.65f),
                ["Food"] = new Color(0.85f, 0.45f, 0.40f),
            };

        /// <summary>Fallback hue for a category the table does not list.</summary>
        public static Color HueFor(string category) =>
            CategoryHues.TryGetValue(category ?? string.Empty, out Color color)
                ? color
                : new Color(0.70f, 0.70f, 0.70f);
    }
}
