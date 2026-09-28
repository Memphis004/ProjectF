using UnityEngine;

// ReSharper disable CheckNamespace
namespace ProjectF.Presentation.Common
{
    /// <summary>
    /// Scene-side carrier of the baked white-square sprite on the UIRoot
    /// GameObject. Exists so RUNTIME-created graphics (the modal raycast
    /// blocker) can grab the sprite without a service round-trip; the
    /// generator assigns it by serialized name like every other view field.
    /// Own file: MonoBehaviour script name must match the file name too
    /// (same Unity rule as the ScriptableObject above).
    /// </summary>
    public sealed class SpriteRegistrySource : MonoBehaviour
    {
        [SerializeField]
        private Sprite whiteSquare = default!;

        public Sprite WhiteSquare => whiteSquare;
    }
}
