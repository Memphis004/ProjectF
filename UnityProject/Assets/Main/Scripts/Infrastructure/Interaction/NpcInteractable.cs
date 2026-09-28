// Stage 10 interactable — the shop NPC (scene 2).
// Filename must equal the class name (MonoScript name rule, see InteractableBase.cs).
// ReSharper disable CheckNamespace
public sealed class NpcInteractable : InteractableBase
{
    public override ProjectF.Infrastructure.Interaction.InteractionKind Kind =>
        ProjectF.Infrastructure.Interaction.InteractionKind.Shopkeeper;
}
