using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ProjectF.Infrastructure.DataTables;
using ProjectF.Tables;
using UnityEngine;

// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure.UI
{
    /// <summary>Supported UI languages (spec 9.5: th first, en second).</summary>
    public enum Language
    {
        Th = 0,
        En = 1,
    }

    /// <summary>
    /// Stage 9 localization (spec 9.5): resolves the Luban name_key columns
    /// (TbItem.NameKey, TbPond.NameKey, TbRecipe.NameKey) to display strings
    /// from Resources/Localization/{lang}.csv.
    ///
    /// Contract: a MISSING key returns the key itself and logs a warning —
    /// never throws (a localization gap must not kill gameplay). Thai is the
    /// default language; en.csv ships alongside for the en toggle.
    /// </summary>
    public sealed class LocalizationService
    {
        private const string ResourceRoot = "Localization/";

        private readonly UnityTableService tableService;
        private readonly Dictionary<string, string> table = new(StringComparer.Ordinal);

        public Language Current { get; private set; } = Language.Th;

        public LocalizationService(UnityTableService tableService)
        {
            this.tableService = tableService;
        }

        /// <summary>Loads a language CSV (Resources/Localization/th.csv /
        /// en.csv). Re-loadable at runtime (Stage 14 settings toggle); the
        /// table is replaced wholesale so a failed load leaves the previous
        /// language intact.</summary>
        public void Load(Language language)
        {
            TextAsset asset = Resources.Load<TextAsset>(ResourceRoot + language.ToString().ToLowerInvariant())
                ?? throw new InvalidOperationException(
                    $"Localization CSV missing: Resources/{ResourceRoot}{language}.csv " +
                    "(ship th.csv and en.csv — see spec 9.5).");

            var loaded = ParseCsv(asset.text);
            if (loaded.Count == 0)
            {
                Debug.LogWarning(
                    $"[loc] {language} CSV parsed to ZERO rows — keeping previous table.");
                return;
            }

            table.Clear();
            foreach (KeyValuePair<string, string> kv in loaded)
            {
                table[kv.Key] = kv.Value;
            }

            Current = language;
            Debug.Log($"[loc] loaded {language}: {table.Count} keys.");
        }

        /// <summary>Resolves a name_key to a display string. Missing key →
        /// the key itself + a warning (never throws, spec 9.5).</summary>
        public string Get(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            if (table.TryGetValue(key, out string? value))
            {
                return value;
            }

            Debug.LogWarning($"[loc] missing key '{key}' (lang {Current}).");
            return key;
        }

        /// <summary>Item display name from the TbItem name_key.</summary>
        public string ItemName(int itemId)
        {
            Item? item = tableService.IsLoaded ? tableService.Tables.TbItem.GetOrDefault(itemId) : null;
            return item is null ? itemId.ToString() : Get(item.NameKey);
        }

        /// <summary>Pond display name from the TbPond name_key.</summary>
        public string PondName(int pondId)
        {
            Pond? pond = tableService.IsLoaded ? tableService.Tables.TbPond.GetOrDefault(pondId) : null;
            return pond is null ? pondId.ToString() : Get(pond.NameKey);
        }

        /// <summary>Recipe display name from the TbRecipe name_key.</summary>
        public string RecipeName(int recipeId)
        {
            Recipe? recipe = tableService.IsLoaded
                ? tableService.Tables.TbRecipe.GetOrDefault(recipeId)
                : null;
            return recipe is null ? recipeId.ToString() : Get(recipe.NameKey);
        }

        /// <summary>Formats a UI template whose value slots are themselves
        /// localized (e.g. "Stamina 12/100 · +1 in 4s" style lines stay in
        /// code; item names inside templates resolve here).</summary>
        public string Format(string template, params object[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] is string key && IsKeyLike(key))
                {
                    args[i] = Get(key);
                }
            }

            return string.Format(CultureInfo.CurrentUICulture, template, args);
        }

        private static bool IsKeyLike(string s) =>
            s.Length > 3 && s == s.ToUpperInvariant() && s.Contains('_');

        // ------------------------------------------------------------------
        // CSV parsing: key,value rows; # comments; utf-8 (Thai needs it).
        // ------------------------------------------------------------------

        private static Dictionary<string, string> ParseCsv(string content)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            using var reader = new StringReader(content.TrimStart('\uFEFF'));
            while (reader.ReadLine() is { } line)
            {
                if (line.Length == 0 || line.StartsWith("#"))
                {
                    continue;
                }

                int comma = line.IndexOf(',');
                if (comma <= 0)
                {
                    continue;
                }

                string key = line[..comma].Trim();
                string value = line[(comma + 1)..].Trim();
                if (key.Length > 0 && !result.ContainsKey(key))
                {
                    result[key] = value;
                }
            }

            return result;
        }
    }
}
