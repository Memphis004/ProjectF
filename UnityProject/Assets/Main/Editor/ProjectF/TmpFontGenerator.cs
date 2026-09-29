using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using TMPro;

// ReSharper disable CheckNamespace
namespace ProjectF.Editor
{
    /// <summary>
    /// Stage 16 TMP migration — Thai text support.
    ///
    /// Creates a Dynamic-population TMP_FontAsset from the Sarabun TTF (OFL,
    /// Assets/Main/Fonts/Sarabun-Regular.ttf) and wires it as the TMP default +
    /// LiberationSans fallback, so every generated UI text renders Thai.
    ///
    /// TMP 3.0.9 quirk: TryAddCharacters is unreliable on a font asset loaded
    /// from disk — it can return false after some domain reloads even with
    /// population=Dynamic and a valid source font — while the fresh-create
    /// path is stable. Generate() therefore proves Thai coverage on the
    /// existing asset first and, when coverage cannot be proven (or can be
    /// recovered via ReadFontAssetDefinition), deletes the asset and rebuilds
    /// it from scratch instead of aborting the whole setup run.
    /// </summary>
    public static class TmpFontGenerator
    {
        private const string FontTtfPath = "Assets/Main/Fonts/Sarabun-Regular.ttf";
        private const string FontAssetDir = "Assets/Main/Fonts";
        private const string FontAssetPath = FontAssetDir + "/Sarabun SDF.asset";
        private const string DefaultFontAssetName = "LiberationSans SDF";

        /// <summary>Sample string the validator renders — Thai glyphs that
        /// MUST be present after this generator runs.</summary>
        public const string ThaiSample = "แรง ทอง ตกปลา";

        [MenuItem("ProjectF/Setup/Generate Thai TMP Font Asset", priority = 21)]
        public static void GenerateFromMenu() => Generate();

        /// <summary>Idempotent: reuses the existing asset when Thai coverage
        /// can be proven; otherwise deletes and recreates it fresh (the fresh
        /// path is the only one proven reliable on TMP 3.0.9 reloads).</summary>
        public static TMP_FontAsset Generate()
        {
            if (!File.Exists(FontTtfPath))
            {
                throw new InvalidOperationException(
                    "Missing " + FontTtfPath + " — download Sarabun-Regular.ttf " +
                    "(SIL OFL 1.1) from https://github.com/google/fonts/tree/main/ofl/sarabun " +
                    "and place it there (same pattern as Luban.dll).");
            }

            TMP_FontAsset? existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (existing is { } && TryEnsureThaiCoverage(existing))
            {
                WireDefaults(existing);
                Debug.Log("[tmp-font] reused existing " + FontAssetPath);
                return existing;
            }

            if (existing is { })
            {
                // Loaded-asset glyph population lost Thai coverage (known TMP
                // 3.0.9 reload quirk). Rebuild from scratch rather than abort.
                Debug.LogWarning(
                    "[tmp-font] existing Sarabun SDF cannot prove Thai coverage — deleting and recreating fresh");
                if (!AssetDatabase.DeleteAsset(FontAssetPath))
                {
                    throw new InvalidOperationException("Failed to delete broken " + FontAssetPath);
                }
            }

            TMP_FontAsset fontAsset = CreateFreshFontAsset();
            if (!TryEnsureThaiCoverage(fontAsset))
            {
                throw new InvalidOperationException(
                    "Sarabun SDF could not add Thai glyphs for sample: " + ThaiSample);
            }

            WireDefaults(fontAsset);
            return fontAsset;
        }

        /// <summary>Builds the Dynamic font asset from the TTF and persists it
        /// with its atlas texture + material as sub-assets (without the
        /// sub-asset registration the saved asset reloads with
        /// m_AtlasTextures unassigned).</summary>
        private static TMP_FontAsset CreateFreshFontAsset()
        {
            Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(FontTtfPath);
            if (sourceFont is null)
            {
                AssetDatabase.ImportAsset(FontTtfPath);
                sourceFont = AssetDatabase.LoadAssetAtPath<Font>(FontTtfPath);
            }

            if (sourceFont is null)
            {
                throw new InvalidOperationException("Cannot import " + FontTtfPath + " as a Unity Font.");
            }

            // Dynamic font asset: atlas populated on demand (Thai OK), point
            // size 46 (crisp at 320x180 with x4+ integer scaling), atlas 512².
            TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(
                sourceFont,
                46,
                9,
                GlyphRenderMode.SDFAA,
                512,
                512,
                AtlasPopulationMode.Dynamic);
            fontAsset.name = "Sarabun SDF";

            AssetDatabase.CreateAsset(fontAsset, FontAssetPath);

            if (fontAsset.atlasTextures is { Length: > 0 } && fontAsset.atlasTextures[0] is { } atlas)
            {
                atlas.name = fontAsset.name + " Atlas";
                AssetDatabase.AddObjectToAsset(atlas, fontAsset);
            }

            if (fontAsset.material is { } material)
            {
                material.name = fontAsset.name + " Material";
                AssetDatabase.AddObjectToAsset(material, fontAsset);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[tmp-font] created " + FontAssetPath);
            return fontAsset;
        }

        /// <summary>Proves the asset can render the Thai sample: rebuilds its
        /// font definition (lookup tables are runtime-only and rebuilt on
        /// demand), adds any MISSING sample glyphs, and verifies every
        /// character resolves. Never throws — returns false so callers can
        /// rebuild the asset from scratch.</summary>
        private static bool TryEnsureThaiCoverage(TMP_FontAsset fontAsset)
        {
            if (fontAsset is null)
            {
                return false;
            }

            if (fontAsset.atlasPopulationMode != AtlasPopulationMode.Dynamic)
            {
                return false;
            }

            try
            {
                fontAsset.ReadFontAssetDefinition();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[tmp-font] ReadFontAssetDefinition failed: " + e.Message);
                return false;
            }

            // TMP 3.0.9: TryAddCharacters returns FALSE both when it failed
            // AND when every requested glyph is already present ("nothing to
            // add"), so its return value cannot distinguish a healthy asset
            // from a broken one. Probe real coverage via HasCharacter and
            // only rasterize when something is actually missing.
            if (!HasAllSampleCharacters(fontAsset))
            {
                try
                {
                    fontAsset.TryAddCharacters(ThaiSample);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[tmp-font] TryAddCharacters threw: " + e.Message);
                }

                if (AssetDatabase.Contains(fontAsset))
                {
                    AssetDatabase.SaveAssets();
                }
            }

            return HasAllSampleCharacters(fontAsset);
        }

        private static bool HasAllSampleCharacters(TMP_FontAsset fontAsset)
        {
            foreach (char ch in ThaiSample)
            {
                if (char.IsWhiteSpace(ch))
                {
                    continue;
                }

                if (!fontAsset.HasCharacter(ch, false))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Makes the Sarabun asset THE default (TMP Settings + the
        /// LiberationSans fallback chain below it) and persists everything.
        /// Coverage is proven by the caller (TryEnsureThaiCoverage); safe to
        /// call repeatedly (idempotent).</summary>
        private static void WireDefaults(TMP_FontAsset fontAsset)
        {
            TMP_Settings settings = Resources.Load<TMP_Settings>("TMP Settings");
            if (settings is null)
            {
                throw new InvalidOperationException(
                    "TMP Settings.asset not found in any Resources folder — " +
                    "import TMP Essential Resources first (Window > TextMeshPro > Import).");
            }

            SerializedObject settingsSo = new SerializedObject(settings);
            SerializedProperty defaultProp = settingsSo.FindProperty("m_defaultFontAsset");
            if (defaultProp is { } && defaultProp.objectReferenceValue != fontAsset)
            {
                defaultProp.objectReferenceValue = fontAsset;
                settingsSo.ApplyModifiedProperties();
                Debug.Log("[tmp-font] TMP Settings default font -> Sarabun SDF");
            }

            // Fallback: keep LiberationSans as a chain below Sarabun for any
            // glyph Sarabun lacks (Latin punctuation edge cases).
            TMP_FontAsset liberation =
                Resources.Load<TMP_FontAsset>(DefaultFontAssetName);
            if (liberation is { } && !fontAsset.fallbackFontAssetTable.Contains(liberation))
            {
                fontAsset.fallbackFontAssetTable.Add(liberation);
                EditorUtility.SetDirty(fontAsset);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }
}
