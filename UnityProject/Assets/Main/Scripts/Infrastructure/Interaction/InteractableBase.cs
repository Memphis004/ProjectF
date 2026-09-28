// Stage 10 concrete interactables — base class.
//
// MonoScript name rule (verified by the E2E interaction test): every
// MonoBehaviour that can be attached to a GameObject MUST live in a .cs file
// named exactly after the class. Multi-class files break Unity's MonoScript
// resolution (MonoScript.FromMonoBehaviour → nameLen=0, m_Script →
// {fileID: 0} on serialize), which silently strips the component from
// prefabs/scenes on reload. Hence: one class per file in this folder.
using Cysharp.Threading.Tasks;
using UnityEngine;

// ReSharper disable CheckNamespace
public abstract class InteractableBase : MonoBehaviour, ProjectF.Infrastructure.Interaction.IInteractable
{
    [SerializeField]
    private string promptKey = string.Empty;

    public abstract ProjectF.Infrastructure.Interaction.InteractionKind Kind { get; }

    public Transform Anchor => transform;

    public virtual bool CanInteract => true;

    public string PromptKey => promptKey;

    /// <summary>Localized verb phrase (resolved through the static root
    /// accessor — set up by UiBootstrapper before any scene loads).</summary>
    public string Prompt => ProjectF.Infrastructure.UI.RootLocalization.Get(promptKey);

    public Cysharp.Threading.Tasks.UniTask InteractAsync() => Cysharp.Threading.Tasks.UniTask.CompletedTask;

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
