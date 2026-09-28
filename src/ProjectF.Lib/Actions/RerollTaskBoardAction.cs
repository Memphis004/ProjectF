using System;
using System.Collections.Generic;
using Bencodex.Types;
using Libplanet.Action;
using Libplanet.Action.State;
using Libplanet.Crypto;
using ProjectF.Lib.Exceptions;
using ProjectF.Lib.States;

namespace ProjectF.Lib.Actions;

/// <summary>
/// reroll_taskboard_v1 — refresh the signer's daily taskboard.
///
/// GDD: 3 quests per day, drawn from the task pool by player level.
/// knowledge.md rule 3: there is no scheduler on-chain — rerolling happens
/// lazily when the player asks, and is gated by block height:
/// blockIndex - LastRerolledAt must be >= TaskBoardState.RerollPeriodBlocks
/// (a never-seeded board, LastRerolledAt == 0, is always due).
///
/// knowledge.md rule 2: the 3-of-pool draw is a partial Fisher-Yates over an
/// ascending-id pool driven exclusively by context.GetRandom().
/// </summary>
[ActionType("reroll_taskboard_v1")]
public sealed class RerollTaskBoardAction : ActionBase
{
    public override string TypeId => "reroll_taskboard_v1";

    protected override IValue EncodePayload() => new Dictionary(new Dictionary<IKey, IValue>());

    protected override void DecodePayload(IValue payload)
    {
        if (payload is not Dictionary)
        {
            throw new FailedLoadStateException(
                "RerollTaskBoardAction payload must be a (possibly empty) Bencodex Dictionary.");
        }
    }

    protected override IWorld ExecuteInternal(IActionContext context)
    {
        IWorld world = context.PreviousState;
        Address signer = context.Signer;
        long blockIndex = context.BlockIndex;
        var tables = GameTables.Instance;

        // The board reflects its owner's level, so the avatar must exist.
        IAccount avatarAccount = GetOrCreateAccount(world, Addresses.Avatar);
        if (avatarAccount.GetState(signer) is not Dictionary avatarEncoded)
        {
            throw new FailedLoadStateException($"Avatar {signer} does not exist.");
        }

        var avatar = new AvatarState(avatarEncoded);
        // knowledge.md rule 6: explicit signer == avatar.Address check.
        EnsureOwner(context, avatar.Address);
        int level = Math.Max(avatar.FishingLevel, avatar.CookingLevel);

        IAccount boardAccount = GetOrCreateAccount(world, Addresses.TaskBoard);
        var board = boardAccount.GetState(signer) is Dictionary encoded
            ? new TaskBoardState(encoded)
            : new TaskBoardState(signer);

        // knowledge.md rule 3: the reroll gate.
        if (board.LastRerolledAt != 0
            && blockIndex - board.LastRerolledAt < TaskBoardState.RerollPeriodBlocks)
        {
            throw new InvalidOperationException(
                $"Taskboard reroll not due until block " +
                $"{board.LastRerolledAt + TaskBoardState.RerollPeriodBlocks} (now {blockIndex}).");
        }

        // Eligible pool: tasks the avatar's level may receive, ascending by id.
        List<int> eligible = new();
        foreach (Tables.Task task in tables.TbTask.DataList)
        {
            if (task.RequiredLevel <= level)
            {
                eligible.Add(task.Id);
            }
        }

        eligible.Sort();
        if (eligible.Count < TaskBoardState.TasksPerDay)
        {
            throw new FailedLoadStateException(
                $"Task pool has {eligible.Count} tasks eligible at level {level}; " +
                $"need at least {TaskBoardState.TasksPerDay}.");
        }

        // Partial Fisher-Yates: draw TasksPerDay distinct ids deterministically.
        IRandom random = context.GetRandom();
        List<int> pool = new(eligible);
        int remaining = pool.Count;
        List<int> chosen = new(TaskBoardState.TasksPerDay);
        for (int i = 0; i < TaskBoardState.TasksPerDay; i++)
        {
            int pick = random.Next(remaining);
            chosen.Add(pool[pick]);
            pool[pick] = pool[remaining - 1];
            remaining--;
        }

        board.SetTasks(chosen, blockIndex);

        return world.SetAccount(
            Addresses.TaskBoard, boardAccount.SetState(signer, board.Bencoded));
    }
}
