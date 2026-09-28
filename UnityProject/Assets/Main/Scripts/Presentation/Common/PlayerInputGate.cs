using System;
using System.Collections.Generic;

// ReSharper disable CheckNamespace
namespace ProjectF.Presentation.Common
{
    /// <summary>Why the world currently has (or has not) input focus.</summary>
    public enum WorldInputState
    {
        /// <summary>No UI owns input — the player walks.</summary>
        None = 0,

        /// <summary>A regular window is open: world input blocked, a click
        /// anywhere closes the top window (spec 9.4 "click-outside to close").</summary>
        WindowOpen = 1,

        /// <summary>A modal owns input: world input fully blocked.</summary>
        ModalOpen = 2,
    }

    /// <summary>
    /// World-input focus stack driven by the UI (Stage 9): Modal = walking
    /// blocked, Window = blocked until the first click ("tap to dismiss"),
    /// None = world receives input. PlayerInputController reads
    /// <see cref="AllowsMovement"/>; WindowService pushes Modal on open and
    /// pops on close; UiInputDriver pushes/pops WindowOpen around the
    /// WindowService stack being non-empty.
    ///
    /// Plain C# + constructor-free (state holder) — registered Singleton on
    /// the ROOT container so every scene's controller sees the same state.
    /// </summary>
    public sealed class PlayerInputGate
    {
        private readonly List<WorldInputState> stack = new() { WorldInputState.None };

        /// <summary>Fired whenever the effective state changes (UiInputDriver).</summary>
        public event Action<WorldInputState>? StateChanged;

        public WorldInputState Current => stack[^1];

        public bool AllowsMovement => Current == WorldInputState.None;

        /// <summary>True exactly when a click should dismiss the top window
        /// (a regular window is open and no modal covers it).</summary>
        public bool WindowDismissOnClick =>
            Current == WorldInputState.WindowOpen;

        public void Push(WorldInputState state)
        {
            stack.Add(state);
            NotifyIfChanged();
        }

        public void Pop(WorldInputState state)
        {
            // Search from the top; unmatched pops (scene died mid-window)
            // must not corrupt the stack — remove OUR entry only.
            for (int i = stack.Count - 1; i >= 1; i--)
            {
                if (stack[i] == state)
                {
                    stack.RemoveAt(i);
                    NotifyIfChanged();
                    return;
                }
            }
        }

        /// <summary>Whether the given level currently sits anywhere in the
        /// stack (WindowService uses this to avoid double-pushing the window
        /// level when a modal closes above still-open regular windows).</summary>
        public bool Contains(WorldInputState state) => stack.Contains(state);

        private void NotifyIfChanged()
        {
            StateChanged?.Invoke(Current);
        }
    }
}
