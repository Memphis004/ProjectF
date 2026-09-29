using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectF.Infrastructure;
using ProjectF.Infrastructure.Blockchain;
using ProjectF.Infrastructure.DataTables;
using ProjectF.Infrastructure.UI;
using ProjectF.Lib.Actions;
using ProjectF.Presentation.Common;
using UnityEngine;
using TMPro;
using TMPro;

using UnityEngine.UI;
using GeneratedTables = ProjectF.Tables.Tables;
using TaskRow = ProjectF.Tables.Task;

// ReSharper disable CheckNamespace
namespace ProjectF.Presentation.Village
{
    /// <summary>TaskBoardWindow VIEW — zero logic (rows cloned by presenter).</summary>
    public sealed class TaskBoardWindow : UIWindow<EmptyWindowParam, NoWindowResult>
    {
        [SerializeField]
        private RectTransform rowTemplate = default!;

        [SerializeField]
        private RectTransform rowList = default!;

        [SerializeField]
        private TMP_Text countdownLabel = default!;

        public RectTransform RowTemplate => rowTemplate;
        public RectTransform RowList => rowList;
        public TMP_Text CountdownLabel => countdownLabel;

        public override bool IsModal => false;

        public override UniTask OnOpenAsync(CancellationToken ct) => UniTask.CompletedTask;

        public override UniTask OnCloseAsync() => UniTask.CompletedTask;
    }

    /// <summary>Pure Stage-10 taskboard math — EditMode-testable.</summary>
    public static class TaskBoardLogic
    {
        /// <summary>Mirror of TaskBoardState.RerollPeriodBlocks (display only).</summary>
        public const int RerollPeriodBlocks = 600;

        /// <summary>Estimated seconds per block (the seed node's 2s target).</summary>
        public const int SecondsPerBlock = 2;

        /// <summary>Progress of a task against the live inventory ("2/3").</summary>
        public static (long Have, long Need) Progress(
            IReadOnlyDictionary<int, long> inventory, int targetItemId, int targetCount)
        {
            long have = inventory.TryGetValue(targetItemId, out long count) ? count : 0;
            return (Math.Min(have, targetCount), targetCount);
        }

        /// <summary>Submit is enabled only when the requirement is fully met.</summary>
        public static bool CanSubmit(
            IReadOnlyDictionary<int, long> inventory, int targetItemId, int targetCount) =>
            inventory.TryGetValue(targetItemId, out long count) && count >= targetCount;

        /// <summary>Countdown to the next reroll in blocks (never negative).</summary>
        public static long BlocksToReroll(long blockIndex, long lastRerolledAt) =>
            Math.Max(0, RerollPeriodBlocks - (blockIndex - lastRerolledAt));

        /// <summary>Countdown expressed in estimated minutes (ceil — a partial
        /// minute still shows as "1").</summary>
        public static long MinutesToReroll(long blockIndex, long lastRerolledAt) =>
            (BlocksToReroll(blockIndex, lastRerolledAt) * SecondsPerBlock + 59) / 60;
    }

    /// <summary>
    /// TaskBoard PRESENTER (spec 10.3): today's 3 tasks from TaskBoardState,
    /// progress computed against the LIVE inventory, Submit enabled only when
    /// requirements are met, rewards previewed. Submit → SubmitTaskAction;
    /// on confirm a reward popup lists gold/exp/items. Countdown to the next
    /// reroll in blocks AND estimated minutes.
    /// </summary>
    public sealed class TaskBoardPresenter : IDisposable
    {
        private readonly ActionQueue actions;
        private readonly StateWatcher state;
        private readonly UnityTableService tables;
        private readonly LocalizationService loc;
        private readonly IWindowService windows;
        private readonly IToastService toasts;
        private readonly OptimisticState optimistic;
        private readonly ChainConnectionMonitor connection;

        private TaskBoardWindow? view;
        private StateWatcher? boundWatcher;
        private bool isSubmitting;

        public TaskBoardPresenter(
            ActionQueue actionQueue,
            StateWatcher stateWatcher,
            UnityTableService tables,
            LocalizationService loc,
            IWindowService windowService,
            IToastService toasts,
            OptimisticState optimistic,
            ChainConnectionMonitor connection)
        {
            actions = actionQueue;
            state = stateWatcher;
            this.tables = tables;
            this.loc = loc;
            windows = windowService;
            this.toasts = toasts;
            this.optimistic = optimistic;
            this.connection = connection;
        }

        public void Attach(TaskBoardWindow window)
        {
            if (ReferenceEquals(view, window))
            {
                return;
            }

            Detach();
            view = window;
            boundWatcher = state;
            boundWatcher.AvatarUpdated += OnAvatarUpdated;
            optimistic.MergedUpdated += OnAvatarUpdated;

            // First paint (pooled window: later opens rely on AvatarUpdated —
            // without an initial rebuild the rows/overlay would keep the
            // state of the previous open).
            Rebuild();
        }

        public void Detach()
        {
            if (boundWatcher is { })
            {
                boundWatcher.AvatarUpdated -= OnAvatarUpdated;
                boundWatcher = null;
            }

            optimistic.MergedUpdated -= OnAvatarUpdated;
            view = null;
        }

        private void OnAvatarUpdated(AvatarSnapshot snapshot)
        {
            if (view is { IsOpen: true })
            {
                Rebuild();
            }
        }

        public void Rebuild()
        {
            if (view is null || !tables.IsLoaded)
            {
                return;
            }

            GeneratedTables table = tables.Tables;
            AvatarSnapshot? snap = optimistic.Current;
            ClearRows();

            if (snap is null)
            {
                view.CountdownLabel.text = string.Empty;
                return;
            }

            foreach (KeyValuePair<int, bool> kv in snap.Tasks)
            {
                TaskRow? task = table.TbTask.GetOrDefault(kv.Key);
                if (task is null)
                {
                    continue;
                }

                bool completed = kv.Value;
                (long have, long need) = TaskBoardLogic.Progress(
                    snap.Inventory, task.TargetItemId, task.TargetCount);

                RectTransform row = UnityEngine.Object.Instantiate(view.RowTemplate, view.RowList);
                row.gameObject.SetActive(true);

                row.Find("Title")!.GetComponent<TMP_Text>().text =
                    $"{loc.Get("TASK_DELIVER")} {task.TargetCount}x {loc.Get(ItemName(table, task.TargetItemId))}";
                row.Find("Progress")!.GetComponent<TMP_Text>().text =
                    completed ? loc.Get("TASK_DONE") : $"{have}/{need}";

                string reward = loc.Get("TASK_REWARD")
                    .Replace("{0}", task.RewardGold.ToString())
                    .Replace("{1}", task.RewardExp.ToString());
                if (task.RewardItemId > 0 && task.RewardItemCount > 0)
                {
                    reward += $" + {task.RewardItemCount}x {loc.Get(ItemName(table, task.RewardItemId))}";
                }

                row.Find("Reward")!.GetComponent<TMP_Text>().text = reward;

                Button submit = row.Find("Submit")!.GetComponent<Button>();
                TMP_Text submitLabel = submit.GetComponentInChildren<TMP_Text>();
                submitLabel.text = loc.Get("TASK_SUBMIT");
                submit.interactable = !completed && !isSubmitting
                    && TaskBoardLogic.CanSubmit(snap.Inventory, task.TargetItemId, task.TargetCount);
                int taskId = kv.Key;
                submit.onClick.AddListener(() => SubmitAsync(taskId).Forget());
            }

            long blocks = TaskBoardLogic.BlocksToReroll(snap.BlockIndex, snap.TasksLastRerolledAt);
            long minutes = TaskBoardLogic.MinutesToReroll(snap.BlockIndex, snap.TasksLastRerolledAt);
            view.CountdownLabel.text = loc.Get("TASK_REROLL")
                .Replace("{0}", blocks.ToString())
                .Replace("{1}", minutes.ToString());
        }

        private async UniTaskVoid SubmitAsync(int taskId)
        {
            if (isSubmitting)
            {
                return;
            }

            if (!connection.CanSubmit)
            {
                toasts.Warning(connection.BlockedReason());
                return;
            }

            isSubmitting = true;
            GeneratedTables table = tables.Tables;
            AvatarSnapshot? before = state.Current;
            TaskRow? task = table.TbTask.GetOrDefault(taskId);
            try
            {
                (bool ok, string reason) = await actions.SubmitWithGuessAsync(
                    new SubmitTaskAction(taskId),
                    guess: g =>
                    {
                        if (task is { })
                        {
                            g.Item(task.TargetItemId, -task.TargetCount);
                            g.Gold(task.RewardGold);
                            g.FishingExp(task.RewardExp);
                            if (task.RewardItemId > 0 && task.RewardItemCount > 0)
                            {
                                g.Item(task.RewardItemId, task.RewardItemCount);
                            }
                        }
                    });

                if (ok)
                {
                    // Reward popup: diff the confirmed inventory before/after
                    // and list gold/exp/items (spec 10.3).
                    await ShowRewardPopupAsync(table, taskId, before);
                }
                else
                {
                    // Guess already rolled back + toast raised by
                    // OptimisticState.OnRolledBack.
                    toasts.Error(ErrorMapper.Localize(reason));
                }
            }
            finally
            {
                isSubmitting = false;
                Rebuild();
            }
        }

        private async UniTask ShowRewardPopupAsync(
            GeneratedTables table, int taskId, AvatarSnapshot? before)
        {
            TaskRow? task = table.TbTask.GetOrDefault(taskId);
            if (task is null)
            {
                return;
            }

            var lines = new List<string>
            {
                $"{loc.Get("TASK_REWARD_GOLD")} +{task.RewardGold}",
                $"{loc.Get("TASK_REWARD_EXP")} +{task.RewardExp}",
            };

            if (task.RewardItemId > 0 && task.RewardItemCount > 0)
            {
                lines.Add($"+{task.RewardItemCount} {loc.Get(ItemName(table, task.RewardItemId))}");
            }

            await windows.OpenAsync<ConfirmDialog, ConfirmDialogParam, bool>(
                new ConfirmDialogParam(loc.Get("TASK_REWARD_TITLE"), string.Join("\n", lines)),
                CancellationToken.None);
        }

        private static string ItemName(GeneratedTables table, int itemId) =>
            table.TbItem.GetOrDefault(itemId)?.NameKey ?? $"ITEM_{itemId}";

        private void ClearRows()
        {
            for (int i = view!.RowList.childCount - 1; i >= 0; i--)
            {
                Transform child = view.RowList.GetChild(i);
                if (child.gameObject != view.RowTemplate.gameObject)
                {
                    UnityEngine.Object.Destroy(child.gameObject);
                }
            }
        }

        public void Dispose() => Detach();
    }
}
