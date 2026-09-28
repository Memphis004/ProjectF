using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

// ReSharper disable CheckNamespace
namespace ProjectF.Editor
{
    /// <summary>
    /// Stage 8 placeholder art (knowledge.md "Editor-generated content"):
    /// deterministic flat-colour PNGs under Assets/Main/Art/Placeholder/.
    /// Real pixel art later drops in over these files with ZERO code changes
    /// (same paths, same sprite names, same PPU).
    ///
    /// Importer contract for every generated texture: Point filter, no
    /// compression, 16 pixels-per-unit, sprite textures. The player sheet is
    /// Multiple mode with a proper 4-direction × 4-frame slice.
    /// </summary>
    public static class PlaceholderSpriteGenerator
    {
        private const string TilesDir = EditorPaths.ArtRoot + "/Tiles";
        private const string ItemsDir = EditorPaths.ArtRoot + "/Items";

        /// <summary>Player sheet row order, TOP to BOTTOM of the PNG —
        /// matching ProjectF.Shared.Presence.Direction (Down=0, Up=1,
        /// Left=2, Right=3) so sprite index = direction*4 + frame.</summary>
        private static readonly string[] PlayerRows = { "Down", "Up", "Left", "Right" };

        public static void GenerateAll()
        {
            EnsureFolders();
            GenerateTiles();
            GeneratePlayerSheet();
            GenerateUiSprites();
            GenerateItemIcons();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[sprites] placeholder art generated.");
        }

        /// <summary>Path of the generated tile sprite for a logical name.</summary>
        public static string TileSpritePath(string tileName) => $"{TilesDir}/{tileName}.png";

        /// <summary>Path of the generated icon sprite for an item id.</summary>
        public static string ItemIconPath(int itemId) => $"{ItemsDir}/Item_{itemId}.png";

        /// <summary>Loads (or null) the generated TileBase asset for a logical name.</summary>
        public static TileBase? GetTileAsset(string tileName) =>
            AssetDatabase.LoadAssetAtPath<TileBase>($"{TilesDir}/{tileName}.asset");

        /// <summary>Slice name of the player sheet frame.</summary>
        public static string PlayerSpriteName(string row, int frame) => $"Player_{row}_{frame}";

        // ------------------------------------------------------------------
        // Tiles: 16x16 flat colours + Tile assets the scene generator uses.
        // ------------------------------------------------------------------

        public static void GenerateTiles()
        {
            EnsureFolders();
            WritePng(TileSpritePath("Grass"), EditorPaths.TileSize, EditorPaths.TileSize,
                (_, __) => EditorPaths.Grass);
            WritePng(TileSpritePath("Water"), EditorPaths.TileSize, EditorPaths.TileSize,
                (_, __) => EditorPaths.Water);
            WritePng(TileSpritePath("Soil"), EditorPaths.TileSize, EditorPaths.TileSize,
                (_, __) => EditorPaths.Soil);
            WritePng(TileSpritePath("WoodFloor"), EditorPaths.TileSize, EditorPaths.TileSize,
                (_, __) => EditorPaths.WoodFloor);
            WritePng(TileSpritePath("Path"), EditorPaths.TileSize, EditorPaths.TileSize,
                (_, __) => EditorPaths.Path);

            foreach (string name in new[] { "Grass", "Water", "Soil", "WoodFloor", "Path" })
            {
                ImportSprite(TileSpritePath(name), SpriteImportMode.Single);
                CreateTileAsset(name);
            }
        }

        private static void CreateTileAsset(string name)
        {
            string path = $"{TilesDir}/{name}.asset";
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(TileSpritePath(name));
            Tile tile = AssetDatabase.LoadAssetAtPath<Tile>(path);
            if (tile is null)
            {
                tile = ScriptableObject.CreateInstance<Tile>();
                AssetDatabase.CreateAsset(tile, path);
            }

            tile.sprite = sprite;
            EditorUtility.SetDirty(tile);
        }

        // ------------------------------------------------------------------
        // Player sheet: 64x128 PNG — 4 rows (Down/Left/Right/Up) × 4 frames.
        // ------------------------------------------------------------------

        public static void GeneratePlayerSheet()
        {
            EnsureFolders();
            string path = $"{EditorPaths.ArtRoot}/Player.png";
            int w = EditorPaths.PlayerSheetW * EditorPaths.PlayerFrames;
            int h = EditorPaths.PlayerSheetH * PlayerRows.Length;

            WritePng(path, w, h, (x, y) =>
            {
                // Texture row 0 = BOTTOM of the PNG; invert so the sheet reads
                // visually top-to-bottom as Down/Up/Left/Right — matching the
                // slice rects below (rect.y measured from the bottom).
                int row = (PlayerRows.Length - 1) - (y / EditorPaths.PlayerSheetH);
                int frame = x / EditorPaths.PlayerSheetW;            // 0..3
                int py = y % EditorPaths.PlayerSheetH;               // pixel within frame
                Color baseColor = row switch
                {
                    0 => EditorPaths.Player,                         // Down — brightest (facing viewer)
                    1 => EditorPaths.Player * 0.75f,                 // Up   — darkest (back)
                    _ => EditorPaths.Player * 0.88f,                 // Left/Right
                };

                // Body occupies rows 8..27 of the 32px cell; legs bob on odd
                // frames so the walk is visible even in flat colour.
                bool inBody = py >= 8 && py < 28;
                bool inLegs = py >= 28 && (frame % 2 == 1 ? py < 31 : py < 30);
                return inBody || inLegs ? baseColor : Color.clear;
            });

            var slices = new List<SpriteMetaData>();
            for (int r = 0; r < PlayerRows.Length; r++)
            {
                for (int f = 0; f < EditorPaths.PlayerFrames; f++)
                {
                    slices.Add(new SpriteMetaData
                    {
                        name = PlayerSpriteName(PlayerRows[r], f),
                        rect = new Rect(
                            f * EditorPaths.PlayerSheetW,
                            (PlayerRows.Length - 1 - r) * EditorPaths.PlayerSheetH,
                            EditorPaths.PlayerSheetW,
                            EditorPaths.PlayerSheetH),
                        alignment = (int)SpriteAlignment.Center,
                        pivot = new Vector2(0.5f, 0.5f),
                        border = Vector4.zero,
                    });
                }
            }

            ImportSprite(path, SpriteImportMode.Multiple, slices.ToArray());
        }

        // ------------------------------------------------------------------
        // UI: 32x32 9-slice panel + a white square for dots/fills/buttons.
        // ------------------------------------------------------------------

        public static void GenerateUiSprites()
        {
            EnsureFolders();
            string panelPath = $"{EditorPaths.UiRoot}/Panel.png";
            WritePng(panelPath, EditorPaths.PanelSize, EditorPaths.PanelSize, (x, y) =>
            {
                int b = 8; // border thickness → 9-slice ring
                bool onRing = x < b || y < b || x >= EditorPaths.PanelSize - b || y >= EditorPaths.PanelSize - b;
                return onRing ? EditorPaths.PanelFrame : Color.clear;
            });
            ImportSprite(panelPath, SpriteImportMode.Single, null, new Vector4(8f, 8f, 8f, 8f));

            string whitePath = $"{EditorPaths.UiRoot}/WhiteSquare.png";
            WritePng(whitePath, EditorPaths.TileSize, EditorPaths.TileSize, (_, __) => Color.white);
            ImportSprite(whitePath, SpriteImportMode.Single);
        }

        // ------------------------------------------------------------------
        // Item icons: one 16x16 PNG per item id in the Luban item table.
        // Colour keyed by category (data/item.csv), tinted per id.
        // ------------------------------------------------------------------

        public static void GenerateItemIcons()
        {
            EnsureFolders();
            foreach ((int id, string category) in ReadItemTable())
            {
                WritePng(ItemIconPath(id), EditorPaths.TileSize, EditorPaths.TileSize, (_, __) =>
                {
                    Color hue = EditorPaths.HueFor(category);
                    // Small deterministic per-id brightness variation so items
                    // inside one category are distinguishable at a glance.
                    float factor = 0.82f + ((id * 37) % 100) / 100f * 0.30f;
                    return hue * factor;
                });
                ImportSprite(ItemIconPath(id), SpriteImportMode.Single);
            }
        }

        /// <summary>
        /// Parses data/item.csv (the Luban table source — single source of
        /// truth; the generated C# tables are not synced into the editor
        /// assembly). Returns (id, category) pairs in file order.
        /// </summary>
        public static List<(int Id, string Category)> ReadItemTable()
        {
            var rows = new List<(int, string)>();
            // Application.dataPath = <repo>/UnityProject/Assets → repo root two up.
            string csvPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "data", "item.csv"));
            if (!File.Exists(csvPath))
            {
                Debug.LogError($"[sprites] item table not found: {csvPath}");
                return rows;
            }

            foreach (string raw in File.ReadAllLines(csvPath))
            {
                string line = raw.TrimStart('\uFEFF');
                if (line.Length == 0 || line.StartsWith("##"))
                {
                    continue;
                }

                string[] fields = line.Split(',');
                if (fields.Length < 4
                    || !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
                {
                    continue;
                }

                rows.Add((id, fields[3].Trim()));
            }

            return rows;
        }

        // ------------------------------------------------------------------
        // Shared helpers
        // ------------------------------------------------------------------

        internal static void EnsureFolders()
        {
            EnsureFolder("Assets/Main");
            EnsureFolder("Assets/Main/Art");
            EnsureFolder(EditorPaths.ArtRoot);
            EnsureFolder(TilesDir);
            EnsureFolder(ItemsDir);
            EnsureFolder(EditorPaths.UiRoot);
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string parent = Path.GetDirectoryName(folder)!.Replace('\\', '/');
            string leaf = Path.GetFileName(folder);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        internal static void WritePng(string path, int width, int height, System.Func<int, int, Color> pixel)
        {
            string dir = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(dir);

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                var pixels = new Color[width * height];
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        pixels[(y * width) + x] = pixel(x, y);
                    }
                }

                texture.SetPixels(pixels);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
                // Make the DB aware of NEW files immediately, so the very
                // first run can configure the importer (GetAtPath would
                // otherwise return null until a manual refresh).
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        internal static TextureImporter ImportSprite(
            string path,
            SpriteImportMode mode,
            SpriteMetaData[]? slices = null,
            Vector4? border = null)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer is null)
            {
                Debug.LogError($"[sprites] no TextureImporter for {path}");
                throw new IOException($"Expected a texture at {path}");
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = mode;
            importer.spritePixelsPerUnit = EditorPaths.PixelsPerUnit;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            if (slices is { Length: > 0 })
            {
#pragma warning disable 618 // legacy spritesheet API — still the portable way in 2022.3
                importer.spritesheet = slices;
#pragma warning restore 618
            }

            if (border.HasValue)
            {
                importer.spriteBorder = border.Value;
            }

            importer.SaveAndReimport();
            return importer;
        }
    }
}
