using UnityEngine;

// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure.UI
{
    /// <summary>Canvas layer stack, bottom to top (spec 9.1). Each value maps to
    /// one child canvas of <see cref="UIRoot"/> with its own sorting order —
    /// the number IS the sorting order (keep the gaps: inserting a layer
    /// between two existing ones must not renumber anything).</summary>
    public enum UILayer
    {
        /// <summary>Under-HUD effects (damage flashes, scene vignettes).</summary>
        World = 0,

        /// <summary>Always-visible HUD: stamina/gold/levels/dots/tip.</summary>
        Hud = 10,

        /// <summary>Regular windows (inventory…). Esc-closable via WindowService.</summary>
        Window = 20,

        /// <summary>Modal dialogs (confirm). Blocks input below with a raycast
        /// blocker for as long as one is open.</summary>
        Modal = 30,

        /// <summary>Toasts + the loading overlay. Always on top.</summary>
        Toast = 40,
    }
}
