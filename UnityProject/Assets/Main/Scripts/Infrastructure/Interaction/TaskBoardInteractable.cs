// Stage 10 interactable — the village taskboard (scene 1).
// Filename must equal the class name (MonoScript name rule, see InteractableBase.cs).
// ReSharper disable CheckNamespace
public sealed class TaskBoardInteractable : InteractableBase
{
    public override ProjectF.Infrastructure.Interaction.InteractionKind Kind =>
        ProjectF.Infrastructure.Interaction.InteractionKind.TaskBoard;
}
