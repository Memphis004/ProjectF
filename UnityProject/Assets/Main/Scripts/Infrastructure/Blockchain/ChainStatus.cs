// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure.Blockchain
{
    /// <summary>Local node lifecycle, surfaced verbatim on the HUD chain dot
    /// (Stage 9 adds the dot; Stage 7 ships the base states). Stage 11 adds
    /// Stalled — driven by <see cref="ChainConnectionMonitor"/>.</summary>
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
        /// <summary>Stage 11: was connected but no new tip for 3× the target
        /// block interval — submissions are refused with a clear message
        /// instead of silently timing out.</summary>
        Stalled = 4,
    }
}
