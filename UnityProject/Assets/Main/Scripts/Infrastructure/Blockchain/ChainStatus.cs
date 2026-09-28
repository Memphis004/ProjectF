// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure.Blockchain
{
    /// <summary>Local node lifecycle, surfaced verbatim on the HUD chain dot
    /// (Stage 9 will add Stalled detection; Stage 7 ships the base states).</summary>
    public enum ChainStatus
    {
        /// <summary>Swarm starting / genesis loading / initial preload running.</summary>
        Bootstrapping = 0,
        /// <summary>Connected, catching up on blocks.</summary>
        Syncing = 1,
        /// <summary>Connected and at (or keeping pace with) the network tip.</summary>
        Synced = 2,
        /// <summary>No reachable peer / bootstrap failed. The game stays
        /// playable in read-only mode and says so (UX contract).</summary>
        Offline = 3,
    }
}
