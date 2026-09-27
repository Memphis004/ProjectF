using System;
using System.Collections.Generic;
using System.Linq;
using Bencodex.Types;
using ProjectF.Lib.Exceptions;

namespace ProjectF.Lib.States;

/// <summary>
/// Flat item bag: itemId → count. Lives in the inventory account space
/// (Addresses.Inventory) at the owner's Address.
///
/// Deliberately a single flat int→int map (no slot model) — the spec table
/// defines it that way and the Luban item ids are globally unique across
/// categories, so fish/seeds/materials all live in one space.
///
/// knowledge.md rule 2: keys are iterated in sorted (ascending itemId)
/// order when encoding, so Bencodex bytes never depend on insertion order.
/// </summary>
public sealed class Inventory
{
    private const string KeyItems = "items";

    private readonly SortedDictionary<int, long> _items;

    public Inventory()
    {
        _items = new SortedDictionary<int, long>();
    }

    public Inventory(IValue bencoded)
        : this()
    {
        if (bencoded is not Dictionary dict
            || !dict.TryGetValue((Text)KeyItems, out IValue? itemsValue)
            || itemsValue is not List itemsList)
        {
            throw new FailedLoadStateException(
                "Inventory bencoded value must be a Bencodex Dictionary with a List \"items\".");
        }

        foreach (IValue entry in itemsList)
        {
            if (entry is not List pair
                || pair.Count != 2
                || pair[0] is not Integer itemId
                || pair[1] is not Integer count)
            {
                throw new FailedLoadStateException(
                    "Inventory \"items\" entries must be [Integer itemId, Integer count] pairs.");
            }

            _items[(int)itemId] = (long)count;
        }
    }

    /// <summary>Number of distinct item ids held.</summary>
    public int Count => _items.Count;

    /// <summary>Total number of items held across all stacks.</summary>
    public long TotalCount => _items.Values.Sum();

    public long GetCount(int itemId) =>
        _items.TryGetValue(itemId, out long count) ? count : 0L;

    public void Add(int itemId, long count = 1)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Count must be positive.");
        }

        _items[itemId] = GetCount(itemId) + count;
    }

    /// <summary>Removes part of a stack; silently no-ops when nothing is held.</summary>
    public bool TryRemove(int itemId, long count = 1)
    {
        long held = GetCount(itemId);
        if (held < count)
        {
            return false;
        }

        if (held == count)
        {
            _items.Remove(itemId);
        }
        else
        {
            _items[itemId] = held - count;
        }

        return true;
    }

    /// <exception cref="ItemNotFoundException">Item missing or count too small.</exception>
    public void RemoveOrThrow(int itemId, long count = 1)
    {
        if (!TryRemove(itemId, count))
        {
            throw new ItemNotFoundException(
                $"Inventory holds {GetCount(itemId)} of item {itemId}, needed {count}.");
        }
    }

    /// <summary>All (itemId, count) pairs, ascending by itemId.</summary>
    public IEnumerable<KeyValuePair<int, long>> All => _items;

    /// <summary>Independent deep copy (mutation of either side is invisible to the other).</summary>
    public Inventory Clone()
    {
        var clone = new Inventory();
        foreach (KeyValuePair<int, long> pair in _items)
        {
            clone._items[pair.Key] = pair.Value;
        }

        return clone;
    }

    /// <summary>
    /// Returns positive deltas of <paramref name="other"/> relative to this
    /// inventory (what "other gained"). Intended for the Unity client's
    /// inventory-diff-before/after trick to pick catch animations.
    /// </summary>
    public SortedDictionary<int, long> DiffAgainst(Inventory other)
    {
        var diff = new SortedDictionary<int, long>();
        foreach (int itemId in _items.Keys.Union(other._items.Keys))
        {
            long delta = other.GetCount(itemId) - GetCount(itemId);
            if (delta != 0)
            {
                diff[itemId] = delta;
            }
        }

        return diff;
    }

    public IValue Bencoded => new Dictionary(new Dictionary<IKey, IValue>
    {
        // Deterministic ordering: entries are already ascending by itemId
        // (SortedDictionary), and Bencodex keys are unique, so the encoded
        // bytes are stable regardless of mutation history.
        [(Text)KeyItems] = new List(_items.Select(pair => new List(
            new IValue[] { (Integer)pair.Key, (Integer)pair.Value }))),
    });
}
