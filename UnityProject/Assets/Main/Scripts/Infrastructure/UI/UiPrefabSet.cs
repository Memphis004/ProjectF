using UnityEngine;

// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure.UI
{
    /// <summary>
    /// Typed bundle of the three UI prefabs (Toast row, InventoryWindow,
    /// ConfirmDialog). Exists because VContainer's RegisterInstance keys by
    /// the IMPLEMENTATION type — three raw RectTransform instances would
    /// collide ("Conflict implementation type: Registration RectTransform");
    /// one composite registration keeps the generator wiring simple and the
    /// container conflict-free. Services pull the single field they need.
    /// </summary>
    public sealed class UiPrefabSet
    {
        public RectTransform Toast = default!;

        public RectTransform InventoryWindow = default!;

        public RectTransform ConfirmDialog = default!;
    }
}
