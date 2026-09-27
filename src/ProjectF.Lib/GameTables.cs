using System;
using System.IO;
using System.Reflection;
using Bencodex.Types;
using Luban;
using ProjectF.Lib.Exceptions;
using ProjectF.Tables;
// namespace `ProjectF.Tables` wins unqualified `Tables` resolution here, so
// alias the generated manager class explicitly (same trick as the tests).
using TablesClass = ProjectF.Tables.Tables;

namespace ProjectF.Lib;

/// <summary>
/// Static access to the Luban game tables for on-chain actions.
///
/// The Luban binaries (tools/gen.ps1 → UnityProject/Assets/StreamingAssets/
/// Tables/*.bytes) are EMBEDDED in this assembly at build time. That is what
/// makes genesis execution and every node's action re-execution deterministic
/// with zero file I/O (knowledge.md rule 2) — and it means Unity needs no
/// StreamingAssets round-trip to evaluate actions.
///
/// Table changes reach the chain exclusively through migration actions (GDD
/// risk note), so a static per-process snapshot is exactly right. Tests can
/// replace the instance via Reset().
/// </summary>
public static class GameTables
{
    private static TablesClass? _instance;

    public static TablesClass Instance => _instance ??= Load();

    /// <summary>Test seam: inject a custom Tables instance (also re-initializable).</summary>
    public static void Reset(TablesClass? tables = null)
    {
        _instance = tables;
    }

    private static TablesClass Load()
    {
        Assembly assembly = typeof(GameTables).Assembly;
        return new TablesClass(name =>
        {
            // Generated Tables.cs requests the lowercase table name ("tbitem").
            string resourceName = $"ProjectF.Lib.Resources.{name}.bytes";
            using Stream? stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                throw new FailedLoadStateException(
                    $"Embedded Luban table '{resourceName}' is missing — run " +
                    "tools/gen.ps1 and rebuild ProjectF.Lib.");
            }

            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return new ByteBuf(buffer.ToArray());
        });
    }
}
