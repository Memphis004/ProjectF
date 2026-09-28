// Stage 10 interactable — the kitchen counter (scene 3). CanInteract is ALWAYS true —
// the locked state ("Talk to Auntie first") renders inside the craft
// window so the player can READ why they cannot cook.
// Filename must equal the class name (MonoScript name rule, see InteractableBase.cs).
// ReSharper disable CheckNamespace
public sealed class KitchenInteractable : InteractableBase
{
    public override ProjectF.Infrastructure.Interaction.InteractionKind Kind =>
        ProjectF.Infrastructure.Interaction.InteractionKind.Kitchen;
}
