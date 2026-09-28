using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Luban;
using UnityEngine;
// 'ProjectF.Tables' contains BOTH the namespace 'ProjectF.Tables.Tables'
// collision (the generated manager class is literally named Tables, and so
// is this file's folder namespace ProjectF.Infrastructure.DataTables' child
// namespace Tables in ProjectF.Tables) — alias the generated class.
using GeneratedTables = ProjectF.Tables.Tables;

// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure.DataTables
{
    /// <summary>
    /// Unity-side table access: loads the Luban binaries from
    /// StreamingAssets/Tables (tools/gen.ps1 output) into the generated
    /// <see cref="GeneratedTables"/> class.
    ///
    /// NOTE: the on-chain actions do NOT read this — ProjectF.Lib embeds the
    /// same binaries as resources (GameTables.cs), keeping chain execution
    /// deterministic with zero file I/O. This service exists for client-only
    /// presentation needs (item names/icons, shop UI, taskboard display).
    /// </summary>
    public sealed class UnityTableService
    {
        private GeneratedTables? _tables;

        /// <summary>True once LoadAsync has succeeded.</summary>
        public bool IsLoaded => _tables is { };

        public GeneratedTables Tables => _tables
            ?? throw new InvalidOperationException(
                "Tables not loaded yet — await LoadAsync first (AppBootstrapper step 1).");

        /// <summary>Reads every tb*.bytes from StreamingAssets/Tables and builds
        /// the Luban Tables object. Runs on the threadpool (disk I/O).</summary>
        public async UniTask LoadAsync()
        {
            if (_tables is { })
            {
                return;
            }

            string dir = Path.Combine(Application.streamingAssetsPath, "Tables");
            GeneratedTables tables = await Task.Run(() =>
            {
                // The generated Tables requests the LOWERCASE table name
                // ("tbitem"); normalize file names the same way.
                var loader = new Dictionary<string, ByteBuf>(StringComparer.OrdinalIgnoreCase);
                if (Directory.Exists(dir))
                {
                    foreach (string file in Directory.GetFiles(dir, "*.bytes"))
                    {
                        string name = Path.GetFileNameWithoutExtension(file);
                        loader[name.ToLowerInvariant()] = new ByteBuf(File.ReadAllBytes(file));
                    }
                }

                return new GeneratedTables(name =>
                {
                    if (loader.TryGetValue(name, out ByteBuf buf))
                    {
                        return buf;
                    }

                    throw new FailedLoadTableException(
                        $"Luban table '{name}' missing from {dir} — run `pwsh tools/gen.ps1`.");
                });
            });

            _tables = tables;
            Debug.Log($"[tables] loaded {dir} (items: {tables.TbItem.DataList.Count}, " +
                      $"ponds: {tables.TbPond.DataList.Count}).");
        }

        /// <summary>Client-side counterpart of FailedLoadStateException for UX.</summary>
        public sealed class FailedLoadTableException : Exception
        {
            public FailedLoadTableException(string message) : base(message)
            {
            }
        }
    }
}
