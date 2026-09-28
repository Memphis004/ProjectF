using Bencodex.Types;
using Libplanet.Action;
using Libplanet.Action.State;
using Libplanet.Crypto;
using ProjectF.Lib.Exceptions;
using ProjectF.Lib.States;
using TablesClass = ProjectF.Tables.Tables;

namespace ProjectF.Lib.Actions;

/// <summary>
/// fishing_v1 — the core loop. Everything skill-or-economy relevant happens
/// on-chain with context.GetRandom(); the client mini-game is cosmetic only.
///
/// Spec flow: require an active pond slot → validate rod owned + bait
/// consumed → SyncStamina then spend max(1, 5 - rod.StaminaDiscount) →
/// hit chance = min(95, pond.BaseHitRate + rod.HitBonus + bait.HitBonus +
/// level*2) → on miss grant 1 exp; on hit run a weighted fish roll (rare
/// weight boosted by bait.RareBonus + rod.RareBonus when fish.Rarity >= 3),
/// roll a size, grant item + exp.
/// </summary>
[ActionType("fishing_v1")]
public sealed class FishingAction : ActionBase
{
    private int _pondId;
    private int _baitItemId;

    public int PondId => _pondId;
    public int BaitItemId => _baitItemId;

    public FishingAction()
    {
    }

    public FishingAction(int pondId, int baitItemId)
    {
        _pondId = pondId;
        _baitItemId = baitItemId;
    }

    public override string TypeId => "fishing_v1";

    protected override IValue EncodePayload() => new Dictionary(new Dictionary<IKey, IValue>
    {
        [(Text)"pond_id"] = (Integer)_pondId,
        [(Text)"bait_item_id"] = (Integer)_baitItemId,
    });

    protected override void DecodePayload(IValue payload)
    {
        if (payload is not Dictionary dict
            || !dict.TryGetValue((Text)"pond_id", out var pondValue)
            || pondValue is not Integer pondId
            || !dict.TryGetValue((Text)"bait_item_id", out var baitValue)
            || baitValue is not Integer baitItemId)
        {
            throw new FailedLoadStateException(
                "FishingAction payload must be a Dictionary with Integer " +
                "\"pond_id\" and \"bait_item_id\".");
        }

        _pondId = (int)pondId;
        _baitItemId = (int)baitItemId;
    }

    protected override IWorld ExecuteInternal(IActionContext context)
    {
        IWorld world = context.PreviousState;
        Address signer = context.Signer;
        long blockIndex = context.BlockIndex;
        var tables = GameTables.Instance;

        var pond = tables.TbPond.GetOrDefault(_pondId)
            ?? throw new FailedLoadStateException($"Pond {_pondId} is not in the game tables.");

        // 1. Active pond slot (knowledge.md rule 3 sweep first).
        IAccount pondAccount = GetOrCreateAccount(world, Addresses.Pond);
        Address pondKey = Addresses.PondKey(_pondId);
        if (pondAccount.GetState(pondKey) is not Dictionary encoded)
        {
            throw new FailedLoadStateException($"Pond {_pondId} has no occupancy state yet.");
        }

        var ownership = new PondOwnershipState(encoded);
        ownership.ReleaseExpired(blockIndex);
        if (!ownership.HasActiveSlot(signer, blockIndex))
        {
            throw new InvalidOperationException(
                $"Signer {signer} has no active slot at pond {_pondId}; occupy_pond_v1 first.");
        }

        // 2. Avatar + rod + bait validation.
        IAccount avatarAccount = GetOrCreateAccount(world, Addresses.Avatar);
        if (avatarAccount.GetState(signer) is not Dictionary avatarEncoded)
        {
            throw new FailedLoadStateException($"Avatar {signer} does not exist.");
        }

        var avatar = new AvatarState(avatarEncoded);
        // knowledge.md rule 6: explicit signer == avatar.Address check.
        EnsureOwner(context, avatar.Address);

        IAccount inventoryAccount = GetOrCreateAccount(world, Addresses.Inventory);
        if (inventoryAccount.GetState(signer) is not Dictionary inventoryEncoded)
        {
            throw new FailedLoadStateException($"Inventory for {signer} does not exist.");
        }

        var inventory = new Inventory(inventoryEncoded);

        var rod = FindRod(inventory, tables)
            ?? throw new ItemNotFoundException(
                $"Signer {signer} owns no rod; fishing requires one.");

        Tables.Bait? bait = null;
        foreach (Tables.Bait candidate in tables.TbBait.DataList)
        {
            if (candidate.ItemId == _baitItemId)
            {
                bait = candidate;
                break;
            }
        }

        if (bait is null)
        {
            throw new FailedLoadStateException(
                $"Item {_baitItemId} is not a bait in the game tables.");
        }

        // 3. Stamina: sync from block height, then spend (rod discount).
        avatar.SyncStamina(blockIndex);
        avatar.SpendStamina(Math.Max(1, 5 - rod.StaminaDiscount));

        // 4. Consume the bait (after stamina passed — spend order per spec).
        inventory.RemoveOrThrow(_baitItemId, 1);

        // 5. Deterministic rolls via context.GetRandom() only.
        Libplanet.Action.IRandom random = context.GetRandom();
        int fishingLevel = avatar.FishingLevel;
        int hitChance = Math.Min(
            95,
            pond.BaseHitRate + rod.HitBonus + bait.HitBonus + fishingLevel * 2);

        bool hit = random.Next(100) < hitChance;
        if (!hit)
        {
            // Miss: 1 exp pity, no item.
            avatar.AddFishingExp(1);
            return SaveAll(world, avatarAccount, avatar, inventoryAccount, inventory);
        }

        // 6. Weighted fish roll over the pond's fish pool.
        List<(Tables.Fish fish, int weight)> pool = new();
        foreach (Tables.PondFish pondFish in tables.TbPondFish.DataList)
        {
            if (pondFish.PondId != _pondId)
            {
                continue;
            }

            var fish = tables.TbFish.Get(pondFish.FishId);
            int weight = fish.BaseWeight;
            if (fish.Rarity >= 3)
            {
                weight += bait.RareBonus + rod.RareBonus;
            }

            pool.Add((fish, weight));
        }

        int totalWeight = 0;
        foreach ((_, int weight) in pool)
        {
            totalWeight += weight;
        }

        if (totalWeight <= 0)
        {
            throw new FailedLoadStateException(
                $"Pond {_pondId} fish pool has no positive weight.");
        }

        int roll = random.Next(totalWeight);
        Tables.Fish caught = pool[0].fish;
        foreach ((Tables.Fish fish, int weight) in pool)
        {
            if (roll < weight)
            {
                caught = fish;
                break;
            }

            roll -= weight;
        }

        // 7. Size roll + rewards. Size is cosmetic today (no per-catch record
        // exists yet in stage 4); the roll is still performed so the RNG
        // stream consumed per catch stays stable for future features.
        _ = random.Next(
            (int)Math.Round(caught.MinSize * 100),
            (int)Math.Round(caught.MaxSize * 100) + 1);
        inventory.Add(caught.ItemId, 1);
        avatar.AddFishingExp(caught.ExpReward);

        return SaveAll(world, avatarAccount, avatar, inventoryAccount, inventory);
    }

    private static Tables.Rod? FindRod(Inventory inventory, TablesClass tables)
    {
        foreach (Tables.Rod rod in tables.TbRod.DataList)
        {
            if (inventory.GetCount(rod.ItemId) > 0)
            {
                return rod;
            }
        }

        return null;
    }

    private static IWorld SaveAll(
        IWorld world,
        IAccount avatarAccount,
        AvatarState avatar,
        IAccount inventoryAccount,
        Inventory inventory)
    {
        return world
            .SetAccount(Addresses.Avatar, avatarAccount.SetState(avatar.Address, avatar.Bencoded))
            .SetAccount(
                Addresses.Inventory,
                inventoryAccount.SetState(avatar.Address, inventory.Bencoded));
    }
}
