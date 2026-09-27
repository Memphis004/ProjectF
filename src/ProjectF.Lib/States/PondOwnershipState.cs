using System;
using System.Collections.Generic;
using System.Linq;
using Bencodex.Types;
using Libplanet.Crypto;
using ProjectF.Lib.Exceptions;

namespace ProjectF.Lib.States;

/// <summary>
/// Who holds which slot of one pond. Lives in the pond account space
/// (Addresses.Pond) at Addresses.PondKey(pondId).
///
/// knowledge.md rule 3: occupancy expires by block height. Expired slots are
/// cleaned lazily — ReleaseExpired(blockIndex) is called by the next action
/// that touches the pond, never by a scheduler. The village pond has two
/// slots (data/pond.csv SlotCount = 2); slot capacity itself lives in the
/// table, not in this state, so a balance patch needs no migration.
/// </summary>
public sealed class PondOwnershipState
{
    private const string KeyPondId = "pond_id";
    private const string KeySlots = "slots";

    private readonly SortedDictionary<Address, long> _slots;

    public PondOwnershipState(int pondId)
    {
        PondId = pondId;
        _slots = new SortedDictionary<Address, long>();
    }

    public PondOwnershipState(IValue bencoded)
    {
        if (bencoded is not Dictionary dict
            || !dict.TryGetValue((Text)KeyPondId, out IValue? pondIdValue)
            || pondIdValue is not Integer pondId)
        {
            throw new FailedLoadStateException(
                "PondOwnershipState bencoded value must be a Bencodex Dictionary " +
                "with an Integer \"pond_id\".");
        }

        if (!dict.TryGetValue((Text)KeySlots, out IValue? slotsValue)
            || slotsValue is not List slotsList)
        {
            throw new FailedLoadStateException(
                "PondOwnershipState dictionary is missing the List \"slots\".");
        }

        PondId = (int)pondId;
        _slots = new SortedDictionary<Address, long>();
        foreach (IValue entry in slotsList)
        {
            if (entry is not List pair
                || pair.Count != 2
                || pair[0] is not Binary addressBytes
                || pair[1] is not Integer expiresAtBlock)
            {
                throw new FailedLoadStateException(
                    "PondOwnershipState \"slots\" entries must be " +
                    "[Binary address, Integer expiresAtBlock] pairs.");
            }

            _slots[new Address(addressBytes.ToByteArray())] = (long)expiresAtBlock;
        }
    }

    public int PondId { get; }

    /// <summary>All occupied slots, ascending by holder address (deterministic iteration).</summary>
    public IReadOnlyDictionary<Address, long> Slots => _slots;

    /// <summary>
    /// True when <paramref name="holder"/> currently occupies a slot that has
    /// not yet expired at <paramref name="blockIndex"/>. The slot is left in
    /// place if expired — lazy cleanup happens through ReleaseExpired().
    /// </summary>
    public bool HasActiveSlot(Address holder, long blockIndex) =>
        _slots.TryGetValue(holder, out long expiresAtBlock) && expiresAtBlock > blockIndex;

    /// <summary>Number of unexpired slots at <paramref name="blockIndex"/>.</summary>
    public int ActiveCount(long blockIndex) =>
        _slots.Count(pair => pair.Value > blockIndex);

    /// <summary>Total occupied slots, expired or not (for capacity checks).</summary>
    public int OccupiedCount => _slots.Count;

    /// <summary>
    /// Grants <paramref name="holder"/> a slot expiring at
    /// <paramref name="expiresAtBlock"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Holder already occupies a slot.</exception>
    public void Occupy(Address holder, long expiresAtBlock)
    {
        if (_slots.ContainsKey(holder))
        {
            throw new InvalidOperationException(
                $"Address {holder} already occupies a slot in pond {PondId}; renew instead.");
        }

        _slots[holder] = expiresAtBlock;
    }

    /// <summary>Extends (or sets) the expiry of an existing slot.</summary>
    /// <exception cref="InvalidOperationException">Holder has no slot.</exception>
    public void Renew(Address holder, long expiresAtBlock)
    {
        if (!_slots.ContainsKey(holder))
        {
            throw new InvalidOperationException(
                $"Address {holder} has no slot in pond {PondId} to renew.");
        }

        _slots[holder] = expiresAtBlock;
    }

    /// <summary>Voluntary release; returns false when the holder had no slot.</summary>
    public bool Release(Address holder) => _slots.Remove(holder);

    /// <summary>
    /// Lazy expiry sweep (knowledge.md rule 3): drops every slot whose expiry
    /// is at or before <paramref name="blockIndex"/>. Called by the next
    /// action that touches this pond.
    /// </summary>
    public IReadOnlyList<Address> ReleaseExpired(long blockIndex)
    {
        List<Address> released = _slots
            .Where(pair => pair.Value <= blockIndex)
            .Select(pair => pair.Key)
            .ToList();

        foreach (Address holder in released)
        {
            _slots.Remove(holder);
        }

        return released;
    }

    public IValue Bencoded => new Dictionary(new Dictionary<IKey, IValue>
    {
        [(Text)KeyPondId] = (Integer)PondId,
        // Deterministic ordering: SortedDictionary iterates ascending by
        // Address bytes, so encoding is stable regardless of occupy order.
        [(Text)KeySlots] = new List(_slots.Select(pair => new List(
            new IValue[] { new Binary(pair.Key.ToByteArray()), (Integer)pair.Value }))),
    });
}
