using System;
using ProjectF.Infrastructure.Blockchain;
using ProjectF.Infrastructure.Network;
using UnityEngine;
using UnityEngine.UI;

// ReSharper disable CheckNamespace
namespace ProjectF.Presentation.Common
{
    /// <summary>HUD VIEW — zero logic, fields written by <see cref="HudPresenter"/>.</summary>
    public sealed class HudView : MonoBehaviour
    {
        [SerializeField]
        private Text nameLabel = default!;

        [SerializeField]
        private Text staminaLabel = default!;

        [SerializeField]
        private Text goldLabel = default!;

        [SerializeField]
        private Text levelLabel = default!;

        [SerializeField]
        private Text sceneLabel = default!;

        [SerializeField]
        private Text tipLabel = default!;

        [SerializeField]
        private Image chainStatusDot = default!;

        [SerializeField]
        private Image presenceStatusDot = default!;

        public Text NameLabel => nameLabel;
        public Text StaminaLabel => staminaLabel;
        public Text GoldLabel => goldLabel;
        public Text LevelLabel => levelLabel;
        public Text SceneLabel => sceneLabel;
        public Text TipLabel => tipLabel;
        public Image ChainStatusDot => chainStatusDot;
        public Image PresenceStatusDot => presenceStatusDot;
    }

    /// <summary>HUD PRESENTER — binds <see cref="StateWatcher"/> and presence
    /// status to the view. Plain C#, constructor-injected only (knowledge.md).
    /// Registered Transient on the root container: every scene's startup
    /// re-binds the SAME presenter instance to the live StateWatcher.</summary>
    public sealed class HudPresenter : IDisposable
    {
        private static readonly Color SyncedColor = new(0.36f, 0.78f, 0.35f);
        private static readonly Color SyncingColor = new(0.95f, 0.80f, 0.25f);
        private static readonly Color OfflineColor = new(0.85f, 0.30f, 0.25f);
        private static readonly Color OnlineColor = new(0.36f, 0.78f, 0.35f);

        private readonly HudView view;
        private readonly IPresenceClient presence;

        private StateWatcher? watcher;
        private bool bound;

        public HudPresenter(HudView view, IPresenceClient presence)
        {
            this.view = view;
            this.presence = presence;
        }

        /// <summary>Re-binds to the state watcher (called on every scene start).</summary>
        public void Bind(StateWatcher stateWatcher)
        {
            if (bound && watcher is { })
            {
                watcher.AvatarUpdated -= OnAvatar;
                watcher.TipChanged -= OnTip;
            }

            watcher = stateWatcher;
            bound = true;

            watcher.AvatarUpdated += OnAvatar;
            watcher.TipChanged += OnTip;
            presence.StatusChanged += OnPresenceStatus;

            if (watcher.Current is { } snapshot)
            {
                OnAvatar(snapshot);
            }

            OnPresenceStatus(presence.Status);
        }

        private void OnAvatar(AvatarSnapshot snapshot)
        {
            // Every field is optional: SETUP.md's minimal HUD is an empty
            // GameObject until Stage 9 builds the real canvas.
            if (view.NameLabel is { })
            {
                view.NameLabel.text = snapshot.Name;
            }

            if (view.StaminaLabel is { })
            {
                view.StaminaLabel.text = $"Stamina {snapshot.Stamina}/{snapshot.MaxStamina}";
            }

            if (view.GoldLabel is { })
            {
                view.GoldLabel.text = $"G {snapshot.Gold}";
            }

            if (view.LevelLabel is { })
            {
                view.LevelLabel.text =
                    $"Fishing {snapshot.FishingLevel} · Cooking {snapshot.CookingLevel}";
            }
        }

        private void OnTip(long tip, string hash)
        {
            if (view.TipLabel is { })
            {
                view.TipLabel.text = $"#{tip}";
            }
        }

        private void OnPresenceStatus(PresenceStatus status)
        {
            if (view.PresenceStatusDot is { })
            {
                view.PresenceStatusDot.color = status == PresenceStatus.Online
                    ? OnlineColor
                    : OfflineColor;
            }
        }

        /// <summary>Called by the bootstrap / a chain monitor (Stage 11) when
        /// the chain status changes.</summary>
        public void SetChainStatus(ChainStatus status)
        {
            if (view.ChainStatusDot is { })
            {
                view.ChainStatusDot.color = status switch
                {
                    ChainStatus.Synced => SyncedColor,
                    ChainStatus.Syncing or ChainStatus.Bootstrapping => SyncingColor,
                    _ => OfflineColor,
                };
            }
        }

        public void SetSceneName(string sceneName)
        {
            if (view.SceneLabel is { })
            {
                view.SceneLabel.text = sceneName;
            }
        }

        public void Dispose()
        {
            if (watcher is { })
            {
                watcher.AvatarUpdated -= OnAvatar;
                watcher.TipChanged -= OnTip;
            }

            presence.StatusChanged -= OnPresenceStatus;
        }
    }
}
