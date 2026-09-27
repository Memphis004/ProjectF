using System;
using System.Collections.Generic;
using System.Linq;
using Bencodex.Types;
using Libplanet.Crypto;
using ProjectF.Lib.Exceptions;
using ProjectF.Tables;

namespace ProjectF.Lib.States;

/// <summary>
/// On-chain avatar: name, levels/exp, stamina and gold. Lives in the
/// avatar account space (Addresses.Avatar) at the owner's Address.
///
/// knowledge.md rule 3: no wall clock — stamina regenerates lazily from
/// block height via SyncStamina(), and exp is never stored per level but
/// accumulated across all levels (AddFishingExp/AddCookingExp walk the
/// TbLevelExp table deterministically).
/// </summary>
public sealed class AvatarState
{
    // Level thresholds come from data/level_exp.csv (level 1-10). Exp is
    // stored as a single lifetime total; level = highest threshold <= exp.
    private static readonly int[] LevelExpThresholds =
        { 0, 50, 140, 300, 560, 950, 1500, 2300, 3400, 5000 };

    public const int StartGold = 300;
    public const int StartStamina = 100;

    private const string KeyName = "name";
    private const string KeyFishingExp = "fishing_exp";
    private const string KeyCookingExp = "cooking_exp";
    private const string KeyStamina = "stamina";
    private const string KeyMaxStamina = "max_stamina";
    private const string KeyStaminaUpdatedAt = "stamina_updated_at";
    private const string KeyGold = "gold";
    private const string KeyKitchenUnlocked = "kitchen_unlocked";

    public AvatarState(string name, Address address)
    {
        Name = name;
        Address = address;
        FishingExp = 0;
        CookingExp = 0;
        Stamina = StartStamina;
        MaxStamina = StartStamina;
        StaminaUpdatedAt = 0;
        Gold = StartGold;
        KitchenUnlocked = false;
    }

    public AvatarState(IValue bencoded)
    {
        if (bencoded is not Dictionary dict)
        {
            throw new FailedLoadStateException(
                "AvatarState bencoded value must be a Bencodex Dictionary.");
        }

        Address = dict.ContainsKey((Text)"address")
            ? new Address(dict.GetValue<Binary>((Text)"address").ToByteArray())
            : throw new FailedLoadStateException(
                "AvatarState dictionary is missing the \"address\" field.");

        Name = dict.GetValue<Text>((Text)KeyName);
        FishingExp = (long)dict.GetValue<Integer>((Text)KeyFishingExp);
        CookingExp = (long)dict.GetValue<Integer>((Text)KeyCookingExp);
        Stamina = (long)dict.GetValue<Integer>((Text)KeyStamina);
        MaxStamina = (long)dict.GetValue<Integer>((Text)KeyMaxStamina);
        StaminaUpdatedAt = (long)dict.GetValue<Integer>((Text)KeyStaminaUpdatedAt);
        Gold = (long)dict.GetValue<Integer>((Text)KeyGold);
        KitchenUnlocked = dict.GetValue<Bencodex.Types.Boolean>((Text)KeyKitchenUnlocked);
    }

    /// <summary>Owner's Libplanet address (also the state key in the avatar space).</summary>
    public Address Address { get; }

    public string Name { get; set; }
    /// <summary>Lifetime fishing exp total (level derived from TbLevelExp thresholds).</summary>
    public long FishingExp { get; private set; }
    /// <summary>Lifetime cooking exp total (level derived from TbLevelExp thresholds).</summary>
    public long CookingExp { get; private set; }
    /// <summary>Current stamina — always read through SyncStamina() first.</summary>
    public long Stamina { get; private set; }
    public long MaxStamina { get; set; }
    /// <summary>Block index of the last stamina mutation (knowledge.md rule 3).</summary>
    public long StaminaUpdatedAt { get; private set; }
    public long Gold { get; private set; }
    public bool KitchenUnlocked { get; set; }

    /// <summary>Fishing level 1-10 derived from the lifetime exp.</summary>
    public int FishingLevel => LevelFromExp(FishingExp);

    /// <summary>Cooking level 1-10 derived from the lifetime exp.</summary>
    public int CookingLevel => LevelFromExp(CookingExp);

    /// <summary>
    /// Applies 1 stamina per elapsed block (capped at MaxStamina) and stamps
    /// StaminaUpdatedAt. knowledge.md rule 3: regeneration happens at read
    /// time; the chain has no scheduler.
    /// </summary>
    public void SyncStamina(long blockIndex, long regenPerBlock = 1)
    {
        if (blockIndex <= StaminaUpdatedAt || regenPerBlock <= 0)
        {
            StaminaUpdatedAt = blockIndex;
            return;
        }

        long elapsedBlocks = blockIndex - StaminaUpdatedAt;
        Stamina = Math.Min(MaxStamina, Stamina + elapsedBlocks * regenPerBlock);
        StaminaUpdatedAt = blockIndex;
    }

    /// <exception cref="NotEnoughStaminaException">Insufficient stamina.</exception>
    public void SpendStamina(long cost)
    {
        if (Stamina < cost)
        {
            throw new NotEnoughStaminaException(
                $"Avatar {Address} needs {cost} stamina but only has {Stamina}.");
        }

        Stamina -= cost;
    }

    /// <summary>Level 1-10 for a lifetime exp total (thresholds from TbLevelExp).</summary>
    public static int LevelFromExp(long exp)
    {
        int level = 1;
        for (int i = 1; i < LevelExpThresholds.Length; i++)
        {
            if (exp >= LevelExpThresholds[i])
            {
                level = i + 1;
            }
        }

        return Math.Min(level, LevelExpThresholds.Length);
    }

    /// <summary>Adds fishing exp (call after reading level-up rewards, if any).</summary>
    public void AddFishingExp(long exp) => FishingExp += exp;

    /// <summary>Adds cooking exp (call after reading level-up rewards, if any).</summary>
    public void AddCookingExp(long exp) => CookingExp += exp;

    /// <exception cref="NotEnoughGoldException">Insufficient gold.</exception>
    public void SpendGold(long amount)
    {
        if (Gold < amount)
        {
            throw new NotEnoughGoldException(
                $"Avatar {Address} needs {amount} gold but only has {Gold}.");
        }

        Gold -= amount;
    }

    public void AddGold(long amount) => Gold += amount;

    /// <summary>Restores stamina (eat_food), capped at MaxStamina.</summary>
    public void RestoreStamina(long amount) =>
        Stamina = Math.Min(MaxStamina, Stamina + amount);

    public IValue Bencoded => new Dictionary(new Dictionary<IKey, IValue>
    {
        [(Text)KeyName] = (Text)Name,
        [(Text)KeyFishingExp] = (Integer)FishingExp,
        [(Text)KeyCookingExp] = (Integer)CookingExp,
        [(Text)KeyStamina] = (Integer)Stamina,
        [(Text)KeyMaxStamina] = (Integer)MaxStamina,
        [(Text)KeyStaminaUpdatedAt] = (Integer)StaminaUpdatedAt,
        [(Text)KeyGold] = (Integer)Gold,
        [(Text)KeyKitchenUnlocked] = new Bencodex.Types.Boolean(KitchenUnlocked),
        // Not part of the original spec's field list, but the state must be
        // self-describing when read back from the avatar account, where the
        // storage key is not always available to the caller.
        [(Text)"address"] = new Binary(Address.ToByteArray()),
    });
}
