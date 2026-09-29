using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectF.Infrastructure;
using ProjectF.Infrastructure.Blockchain;
using ProjectF.Infrastructure.Network;
using ProjectF.Infrastructure.UI;
using UnityEngine;
using TMPro;
using TMPro;

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
        private TMP_Text nameLabel = default!;

        [SerializeField]
        private TMP_Text staminaLabel = default!;

        [SerializeField]
        private Image staminaBar = default!;

        [SerializeField]
        private TMP_Text goldLabel = default!;

        [SerializeField]
        private TMP_Text levelLabel = default!;

        [SerializeField]
        private Image fishingExpBar = default!;

        [SerializeField]
        private Image cookingExpBar = default!;

        [SerializeField]
        private TMP_Text sceneLabel = default!;

        [SerializeField]
        private TMP_Text tipLabel = default!;

        [SerializeField]
        private Image chainStatusDot = default!;

        [SerializeField]
        private Image presenceStatusDot = default!;

        /// <summary>Stage 11: kiosk-free sync line (top center) — shows
        /// "Syncing N / M" ONLY while actually catching up; hidden otherwise
        /// so it can never crowd the HUD at rest.</summary>
        [SerializeField]
        private TMP_Text syncLabel = default!;

        /// <summary>Stage 11: pending-action badge ("…n" while actions are
        /// staged, a spinning dot while queued). Zero logic — presenter-driven.</summary>
        [SerializeField]
        private TMP_Text pendingLabel = default!;

        public TMP_Text NameLabel => nameLabel;
        public TMP_Text StaminaLabel => staminaLabel;
        public Image StaminaBar => staminaBar;
        public TMP_Text GoldLabel => goldLabel;
        public TMP_Text LevelLabel => levelLabel;
        public Image FishingExpBar => fishingExpBar;
        public Image CookingExpBar => cookingExpBar;
        public TMP_Text SceneLabel => sceneLabel;
        public TMP_Text TipLabel => tipLabel;
        public Image ChainStatusDot => chainStatusDot;
        public Image PresenceStatusDot => presenceStatusDot;
        public TMP_Text SyncLabel => syncLabel;

        public TMP_Text PendingLabel => pendingLabel;
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
        private static readonly Color StalledColor = new(0.95f, 0.55f, 0.15f);
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
        private readonly ILibplanetClient chain;
        private readonly ActionQueue actions;
        private readonly ChainConnectionMonitor connection;

        private StateWatcher? watcher;
        private bool bound;
        private CancellationTokenSource? syncCts;

        public HudPresenter(
            HudView view,
            IPresenceClient presence,
            LocalizationService loc,
            ILibplanetClient chain,
            ActionQueue actions,
            ChainConnectionMonitor connection)
        {
            this.view = view;
            this.presence = presence;
            this.loc = loc;
            this.chain = chain;
            this.actions = actions;
            this.connection = connection;
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
            actions.PendingChanged += RenderPending;
            connection.StatusChanged += OnConnectionStatus;

            RenderPending();
            OnConnectionStatus(connection.Status);

            if (watcher.Current is { } snapshot)
            {
                OnAvatar(snapshot);
            }

            OnPresenceStatus(presence.Status);

            // Stage 11: non-blocking catch-up line. Pure polling display —
            // no input gating, gameplay never waits on sync (spec 9.4).
            if (syncCts is null && view.SyncLabel is { })
            {
                syncCts = new CancellationTokenSource();
                SyncLoopAsync(syncCts.Token).Forget();
            }
        }

        private async UniTaskVoid SyncLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await UniTask.Delay(500, cancellationToken: ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                RenderSync();
            }
        }

        /// <summary>Renders the catch-up line + keeps the chain dot honest
        /// (nothing else calls SetChainStatus today). The line shows ONLY
        /// while a peer-tip target is known and ahead of us — at rest it
        /// disappears, bootstrapping without a target degrades to "Syncing".</summary>
        private void RenderSync()
        {
            SetChainStatus(chain.Status);

            TMP_Text? label = view.SyncLabel;
            if (label is null)
            {
                return;
            }

            SyncProgress sp = chain.SyncProgress;
            if (chain.Status != ChainStatus.Offline && sp.HasTarget)
            {
                label.gameObject.SetActive(true);
                label.text = loc.Get("UI_SYNC_PROGRESS")
                    .Replace("{0}", sp.Tip.ToString())
                    .Replace("{1}", sp.TargetTip.ToString());
            }
            else if (chain.Status == ChainStatus.Bootstrapping)
            {
                label.gameObject.SetActive(true);
                label.text = loc.Get("UI_CHAIN_SYNCING");
            }
            else
            {
                label.gameObject.SetActive(false);
            }
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

        /// <summary>Stage 11: pending-action indicator — "…n" while actions
        /// are queued/staged, blank when idle. Display only; the queue owns
        /// the state machine.</summary>
        private void RenderPending()
        {
            if (view.PendingLabel is { })
            {
                int count = actions.Pending.Count;
                view.PendingLabel.gameObject.SetActive(count > 0);
                if (count > 0)
                {
                    view.PendingLabel.text = actions.HasStaged
                        ? $"…{count}"
                        : $"○{count}";
                }
            }
        }

        private void OnConnectionStatus(ChainStatus status)
        {
            SetChainStatus(status);
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

        /// <summary>Called by <see cref="ChainConnectionMonitor"/> (subscribed
        /// in <see cref="Bind"/>) when the monitored status changes. Stalled
        /// shows amber-red — distinct from offline red and syncing yellow.</summary>
        public void SetChainStatus(ChainStatus status)
        {
            if (view.ChainStatusDot is { })
            {
                view.ChainStatusDot.color = status switch
                {
                    ChainStatus.Synced => SyncedColor,
                    ChainStatus.Syncing or ChainStatus.Bootstrapping => SyncingColor,
                    ChainStatus.Stalled => StalledColor,
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
            actions.PendingChanged -= RenderPending;
            connection.StatusChanged -= OnConnectionStatus;
            syncCts?.Cancel();
            syncCts?.Dispose();
            syncCts = null;
        }
    }
}
