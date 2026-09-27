using System;
using System.Collections.Generic;
using System.Linq;
using Bencodex.Types;
using Libplanet.Crypto;
using ProjectF.Lib.Exceptions;

namespace ProjectF.Lib.States;

/// <summary>
/// Per-avatar daily task list for the village Taskboard. Lives in the
/// taskboard account space (Addresses.TaskBoard) at the owner's Address.
///
/// knowledge.md rule 3: rerolls are lazy — LastRerolledAt stores the block
/// index of the last reroll, and callers compare blockIndex - LastRerolledAt
/// against the reroll period (N blocks) to decide when a fresh board is due.
/// 3 tasks/day per the GDD; the per-day random selection itself happens in
/// actions via context.GetRandom() and lands here as plain ids+flags.
/// </summary>
public sealed class TaskBoardState
{
    /// <summary>3 quests per day (GDD section 2.5 Taskboard).</summary>
    public const int TasksPerDay = 3;

    /// <summary>
    /// A game "day" for the lazy reroll gate (knowledge.md rule 3): 600
    /// blocks ≈ 20 minutes at the 2s target block interval — one average
    /// session per the GDD. Balance knob, not a protocol constant.
    /// </summary>
    public const int RerollPeriodBlocks = 600;

    private const string KeyAvatar = "avatar";
    private const string KeyTasks = "tasks";
    private const string KeyLastRerolledAt = "last_rerolled_at";

    private readonly SortedDictionary<int, bool> _tasks;

    public TaskBoardState(Address avatarAddress)
    {
        AvatarAddress = avatarAddress;
        _tasks = new SortedDictionary<int, bool>();
        LastRerolledAt = 0;
    }

    public TaskBoardState(IValue bencoded)
    {
        if (bencoded is not Dictionary dict
            || !dict.TryGetValue((Text)KeyAvatar, out IValue? avatarValue)
            || avatarValue is not Binary avatarBytes)
        {
            throw new FailedLoadStateException(
                "TaskBoardState bencoded value must be a Bencodex Dictionary " +
                "with a Binary \"avatar\" address.");
        }

        if (!dict.TryGetValue((Text)KeyTasks, out IValue? tasksValue)
            || tasksValue is not List tasksList)
        {
            throw new FailedLoadStateException(
                "TaskBoardState dictionary is missing the List \"tasks\".");
        }

        if (!dict.TryGetValue((Text)KeyLastRerolledAt, out IValue? rerolledValue)
            || rerolledValue is not Integer lastRerolledAt)
        {
            throw new FailedLoadStateException(
                "TaskBoardState dictionary is missing the Integer \"last_rerolled_at\".");
        }

        AvatarAddress = new Address(avatarBytes.ToByteArray());
        LastRerolledAt = (long)lastRerolledAt;
        _tasks = new SortedDictionary<int, bool>();
        foreach (IValue entry in tasksList)
        {
            if (entry is not List pair
                || pair.Count != 2
                || pair[0] is not Integer taskId
                || pair[1] is not Bencodex.Types.Boolean completed)
            {
                throw new FailedLoadStateException(
                    "TaskBoardState \"tasks\" entries must be " +
                    "[Integer taskId, Boolean completed] pairs.");
            }

            _tasks[(int)taskId] = completed;
        }
    }

    public Address AvatarAddress { get; }        /// <summary>taskId → completed flag, ascending by taskId (deterministic iteration).</summary>
    public IReadOnlyDictionary<int, bool> Tasks => _tasks;

    /// <summary>Block index of the most recent reroll (knowledge.md rule 3).</summary>
    public long LastRerolledAt { get; private set; }

    /// <summary>Current daily tasks, ascending by taskId.</summary>
    public IEnumerable<int> TaskIds => _tasks.Keys;

    /// <summary>Replaces the whole board (a reroll or first assignment).</summary>
    public void SetTasks(IEnumerable<int> taskIds, long rerolledAtBlockIndex)
    {
        if (taskIds is null)
        {
            throw new ArgumentNullException(nameof(taskIds));
        }

        _tasks.Clear();
        foreach (int taskId in taskIds)
        {
            _tasks[taskId] = false;
        }

        LastRerolledAt = rerolledAtBlockIndex;
    }

    public bool HasTask(int taskId) => _tasks.ContainsKey(taskId);

    public bool IsCompleted(int taskId) =>
        _tasks.TryGetValue(taskId, out bool completed) && completed;

    /// <summary>
    /// Marks a task done.
    /// </summary>
    /// <exception cref="InvalidOperationException">Task not on the board.</exception>
    public void MarkCompleted(int taskId)
    {
        if (!_tasks.ContainsKey(taskId))
        {
            throw new InvalidOperationException(
                $"Task {taskId} is not on the taskboard of {AvatarAddress}.");
        }

        _tasks[taskId] = true;
    }

    public bool AllCompleted => _tasks.Count > 0 && _tasks.Values.All(completed => completed);

    public IValue Bencoded => new Dictionary(new Dictionary<IKey, IValue>
    {
        [(Text)KeyAvatar] = new Binary(AvatarAddress.ToByteArray()),
        // Deterministic ordering: SortedDictionary iterates ascending by taskId.
        [(Text)KeyTasks] = new List(_tasks.Select(pair => new List(
            new IValue[] { (Integer)pair.Key, new Bencodex.Types.Boolean(pair.Value) }))),
        [(Text)KeyLastRerolledAt] = (Integer)LastRerolledAt,
    });
}
