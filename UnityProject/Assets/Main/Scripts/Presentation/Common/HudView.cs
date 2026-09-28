using System;
using ProjectF.Infrastructure.Blockchain;
using ProjectF.Infrastructure.Network;
using ProjectF.Infrastructure.UI;
using UnityEngine;
using UnityEngine.UI;

// ReSharper disable CheckNamespace
namespace ProjectF.Presentation.Common
{
    /// <summary>HUD VIEW — zero logic, fields written by <see cref="HudPresenter"/>.
    /// Stage 9 (spec 9.4): stamina BAR (fill + text) with regen ETA, gold,
    /// fishing/cooking level + exp bars, scene name, tip label, chain dot
    /// (Synced/Syncing/Offline) and presence dot (Online/Offline).
    /// The per-scene Hud prefab instance lives INSIDE each gameplay scene
    /// (Stage 8 lesson: scene files cannot cross-reference); the UIRoot HUD
    /// layer in Persistent stays empty.</summary>
    public sealed class HudView : MonoBehaviour
    {
        [SerializeField]
        private Text nameLabel = default!;

        [SerializeField]
        private Text staminaLabel = default!;

        [SerializeField]
        private Image staminaBar = default!;

        [SerializeField]
        private Text goldLabel = default!;

        [SerializeField]
        private Text levelLabel = default!;

        [SerializeField]
        private Image fishingExpBar = default!;

        [SerializeField]
        private Image cookingExpBar = default!;

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
        public Image StaminaBar => staminaBar;
        public Text GoldLabel => goldLabel;
        public Text LevelLabel => levelLabel;
        public Image FishingExpBar => fishingExpBar;
        public Image CookingExpBar => cookingExpBar;
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

        /// <summary>Mirror of ProjectF.Lib AvatarState.LevelExpThresholds
        /// (data/level_exp.csv, cumulative-to-reach-level). PRIVATE on the
        /// chain type — duplicating 10 ints beats widening the chain API for
        /// a progress bar; the ProjectValidator checks the two arrays match
        /// (public: the editor assembly has no internals visibility).</summary>
        public static readonly int[] LevelExpThresholds =
            { 0, 50, 140, 300, 560, 950, 1500, 2300, 3400, 5000 };

        private readonly HudView view;
        private readonly IPresenceClient presence;
        private readonly LocalizationService loc;

        private StateWatcher? watcher;
        private bool bound;

        public HudPresenter(HudView view, IPresenceClient presence, LocalizationService loc)
        {
            this.view = view;
            this.presence = presence;
            this.loc = loc;
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
            // Every field is optional: the minimal HUD is an empty GameObject
            // until the Stage 9 generator builds the real canvas.
            if (view.NameLabel is { })
            {
                view.NameLabel.text = string.IsNullOrEmpty(snapshot.Name)
                    ? "…"
                    : snapshot.Name;
            }

            if (view.StaminaLabel is { })
            {
                // Spec 9.4: "current/max + regen ETA in seconds derived from
                // block interval". 1 stamina/block (GDD 2.5) at the 2s target
                // interval → ETA seconds == missing stamina × 2 (only while
                // the chain is producing blocks; fine to show unconditionally
                // since an offline chain also freezes the displayed stamina).
                long missing = snapshot.MaxStamina - snapshot.Stamina;
                string eta = missing > 0
                    ? $" (+{missing * ProjectF.Lib.Policy.BlockPolicySource.TargetBlockIntervalMs / 1000}s)"
                    : string.Empty;
                view.StaminaLabel.text =
                    $"{loc.Get("UI_STAMINA")} {snapshot.Stamina}/{snapshot.MaxStamina}{eta}";
            }

            if (view.StaminaBar is { })
            {
                view.StaminaBar.fillAmount = snapshot.MaxStamina > 0
                    ? Mathf.Clamp01(snapshot.Stamina / (float)snapshot.MaxStamina)
                    : 0f;
            }

            if (view.GoldLabel is { })
            {
                view.GoldLabel.text = $"{loc.Get("UI_GOLD")} {snapshot.Gold}";
            }

            if (view.LevelLabel is { })
            {
                view.LevelLabel.text =
                    $"F{snapshot.FishingLevel} · C{snapshot.CookingLevel}";
            }

            if (view.FishingExpBar is { })
            {
                view.FishingExpBar.fillAmount = ExpFraction(
                    snapshot.FishingLevel, snapshot.FishingExp);
            }

            if (view.CookingExpBar is { })
            {
                view.CookingExpBar.fillAmount = ExpFraction(
                    snapshot.CookingLevel, snapshot.CookingExp);
            }
        }

        /// <summary>Exp progress within the CURRENT level. Thresholds are
        /// cumulative (level N reached at Thresholds[N-1]); LevelFromExp in
        /// ProjectF.Lib mirrors this exact array.</summary>
        public static float ExpFraction(int level, long exp)
        {
            int idx = Math.Min(Math.Max(level, 1), LevelExpThresholds.Length);
            long prev = LevelExpThresholds[idx - 1];
            long need = idx < LevelExpThresholds.Length
                ? LevelExpThresholds[idx]
                : prev; // max level: bar stays full
            return need > prev
                ? Mathf.Clamp01((exp - prev) / (float)(need - prev))
                : 1f;
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
