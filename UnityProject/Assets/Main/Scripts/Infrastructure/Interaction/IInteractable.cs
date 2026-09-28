using Cysharp.Threading.Tasks;
using UnityEngine;

// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure.Interaction
{
    /// <summary>What kind of interaction target this is (drives the window
    /// the InteractionPromptDriver opens).</summary>
    public enum InteractionKind
    {
        Shopkeeper = 0,
        TaskBoard = 1,
        Kitchen = 2,
    }

    /// <summary>
    /// Stage 10 interaction framework (spec 10.1): anything the player can
    /// press [E] on. Prompt is the localized player-facing line
    /// ("[E] Talk to the shopkeeper" — the "[E]" prefix itself is rendered by
    /// the prompt view, implementations return only the verb phrase).
    /// Implementations are MonoBehaviour components placed by the generators
    /// (NpcShopkeeper / TaskBoard / kitchen point) and discovered by
    /// <see cref="InteractionDetector"/> via Physics2D overlap.
    /// </summary>
    public interface IInteractable
    {
        /// <summary>Which window this target opens.</summary>
        InteractionKind Kind { get; }

        /// <summary>Transform the prompt hovers above (usually the collider's
        /// own transform; the view offsets it one tile up).</summary>
        Transform Anchor { get; }

        /// <summary>Localized verb phrase, e.g. "Talk to the shopkeeper".</summary>
        string Prompt { get; }

        /// <summary>False disables [E] (the prompt then renders greyed or
        /// hidden — driver decision). Targets whose window shows its own
        /// locked state return true here.</summary>
        bool CanInteract { get; }

        /// <summary>Runs the interaction. Window opening is orchestrated by
        /// the InteractionPromptDriver (the only component holding
        /// IWindowService) — implementations complete quickly.</summary>
        UniTask InteractAsync();
    }
}
