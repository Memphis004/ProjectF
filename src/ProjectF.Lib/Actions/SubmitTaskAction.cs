using Bencodex.Types;
using Libplanet.Action;
using Libplanet.Action.State;
using Libplanet.Crypto;
using ProjectF.Lib.Exceptions;
using ProjectF.Lib.States;

namespace ProjectF.Lib.Actions;

/// <summary>
/// submit_task_v1 — deliver items at the village Taskboard.
///
/// Spec flow: verify the task is on the board and not yet completed → verify
/// + consume the required items atomically → pay gold/exp (+ bonus item if
/// the task defines one) → mark the task completed.
/// </summary>
[ActionType("submit_task_v1")]
public sealed class SubmitTaskAction : ActionBase
{
    private int _taskId;

    public int TaskId => _taskId;

    public SubmitTaskAction()
    {
    }

    public SubmitTaskAction(int taskId)
    {
        _taskId = taskId;
    }

    public override string TypeId => "submit_task_v1";

    protected override IValue EncodePayload() => new Dictionary(new Dictionary<IKey, IValue>
    {
        [(Text)"task_id"] = (Integer)_taskId,
    });

    protected override void DecodePayload(IValue payload)
    {
        if (payload is not Dictionary dict
            || !dict.TryGetValue((Text)"task_id", out var taskValue)
            || taskValue is not Integer taskId)
        {
            throw new FailedLoadStateException(
                "SubmitTaskAction payload must be a Dictionary with an Integer \"task_id\".");
        }

        _taskId = (int)taskId;
    }

    protected override IWorld ExecuteInternal(IActionContext context)
    {
        IWorld world = context.PreviousState;
        Address signer = context.Signer;
        long blockIndex = context.BlockIndex;
        var tables = GameTables.Instance;

        var task = tables.TbTask.GetOrDefault(_taskId)
            ?? throw new FailedLoadStateException(
                $"Task {_taskId} is not in the game tables.");

        IAccount taskBoardAccount = GetOrCreateAccount(world, Addresses.TaskBoard);
        if (taskBoardAccount.GetState(signer) is not Dictionary boardEncoded)
        {
            throw new FailedLoadStateException(
                $"Taskboard for {signer} does not exist (create_avatar_v1 first).");
        }

        var taskBoard = new TaskBoardState(boardEncoded);
        if (!taskBoard.HasTask(_taskId))
        {
            throw new InvalidOperationException(
                $"Task {_taskId} is not on the taskboard of {signer}.");
        }

        if (taskBoard.IsCompleted(_taskId))
        {
            throw new InvalidOperationException($"Task {_taskId} was already completed.");
        }

        IAccount avatarAccount = GetOrCreateAccount(world, Addresses.Avatar);
        if (avatarAccount.GetState(signer) is not Dictionary avatarEncoded)
        {
            throw new FailedLoadStateException($"Avatar {signer} does not exist.");
        }

        var avatar = new AvatarState(avatarEncoded);
        if (avatar.FishingLevel < task.RequiredLevel && avatar.CookingLevel < task.RequiredLevel)
        {
            throw new InvalidOperationException(
                $"Task {_taskId} requires level {task.RequiredLevel}; avatar has " +
                $"fishing {avatar.FishingLevel}/cooking {avatar.CookingLevel}.");
        }

        IAccount inventoryAccount = GetOrCreateAccount(world, Addresses.Inventory);
        if (inventoryAccount.GetState(signer) is not Dictionary inventoryEncoded)
        {
            throw new FailedLoadStateException($"Inventory for {signer} does not exist.");
        }

        var inventory = new Inventory(inventoryEncoded);

        // Verify stock FIRST — atomic submission, nothing mutates on failure.
        if (inventory.GetCount(task.TargetItemId) < task.TargetCount)
        {
            throw new ItemNotFoundException(
                $"Task {_taskId} needs {task.TargetCount}x item {task.TargetItemId}; " +
                $"inventory holds {inventory.GetCount(task.TargetItemId)}.");
        }

        // Mutate: consume → reward → mark.
        inventory.RemoveOrThrow(task.TargetItemId, task.TargetCount);
        avatar.AddGold(task.RewardGold);
        avatar.AddFishingExp(task.RewardExp);
        if (task.RewardItemId > 0 && task.RewardItemCount > 0)
        {
            inventory.Add(task.RewardItemId, task.RewardItemCount);
        }

        taskBoard.MarkCompleted(_taskId);
        avatar.SyncStamina(blockIndex); // stamp regen; no stamina is spent here

        return world
            .SetAccount(Addresses.Avatar, avatarAccount.SetState(signer, avatar.Bencoded))
            .SetAccount(
                Addresses.Inventory, inventoryAccount.SetState(signer, inventory.Bencoded))
            .SetAccount(
                Addresses.TaskBoard,
                taskBoardAccount.SetState(signer, taskBoard.Bencoded));
    }
}
