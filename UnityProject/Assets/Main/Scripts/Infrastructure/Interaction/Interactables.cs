using Cysharp.Threading.Tasks;
using UnityEngine;

// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure.Interaction
{
    /// <summary>Shared implementation: Kind + localized key + always-on.</summary>
    public abstract class InteractableBase : MonoBehaviour, IInteractable
    {
        [SerializeField]
        private string promptKey = string.Empty;

        public abstract InteractionKind Kind { get; }

        public Transform Anchor => transform;

        public virtual bool CanInteract => true;

        public string PromptKey => promptKey;

        /// <summary>Localized verb phrase (resolved through the static root
        /// accessor — set up by UiBootstrapper before any scene loads).</summary>
        public string Prompt => UI.RootLocalization.Get(promptKey);

        public UniTask InteractAsync() => UniTask.CompletedTask;

        /// <summary>Generator hook.</summary>
        public void Configure(string newPromptKey) => promptKey = newPromptKey;

        protected virtual void Reset()
        {
            // Default trigger collider for the detector's overlap query —
            // generators size it explicitly; Reset keeps hand-placed ones sane.
            if (TryGetComponent(out Collider2D collider))
            {
                collider.isTrigger = true;
            }
        }
    }

    /// <summary>The shop NPC (scene 2).</summary>
    public sealed class NpcInteractable : InteractableBase
    {
        public override InteractionKind Kind => InteractionKind.Shopkeeper;
    }

    /// <summary>The village taskboard (scene 1).</summary>
    public sealed class TaskBoardInteractable : InteractableBase
    {
        public override InteractionKind Kind => InteractionKind.TaskBoard;
    }

    /// <summary>The kitchen counter (scene 3). CanInteract is ALWAYS true —
    /// the locked state ("Talk to Auntie first") renders inside the craft
    /// window so the player can READ why they cannot cook.</summary>
    public sealed class KitchenInteractable : InteractableBase
    {
        public override InteractionKind Kind => InteractionKind.Kitchen;
    }
}
