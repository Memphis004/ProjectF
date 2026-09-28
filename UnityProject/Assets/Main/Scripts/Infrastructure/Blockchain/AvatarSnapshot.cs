using System.Collections.Generic;
using System.Linq;

// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure.Blockchain
{
    /// <summary>
    /// Read-only view of the confirmed on-chain world for UI binding (C# 9
    /// init-only props — compiled against the IsExternalInit polyfill in
    /// Infrastructure/SystemRuntimeCompatibility.cs).
    /// Built from the Bencodex states read by <see cref="StateWatcher"/> —
    /// never mutated client-side, never used to decide legality (that is the
    /// chain's job; Stage 11 adds the display-only optimistic overlay).
    /// </summary>
    public sealed class AvatarSnapshot
    {
        public string Name { get; init; } = string.Empty;

        public long BlockIndex { get; init; }

        public long Stamina { get; init; }

        public long MaxStamina { get; init; }

        public long Gold { get; init; }

        public int FishingLevel { get; init; } = 1;

        public long FishingExp { get; init; }

        public int CookingLevel { get; init; } = 1;

        public long CookingExp { get; init; }

        public bool KitchenUnlocked { get; init; }

        /// <summary>Confirmed inventory (itemId → count) at this tip.</summary>
        public IReadOnlyDictionary<int, long> Inventory { get; init; }
            = new Dictionary<int, long>();

        public long GetItemCount(int itemId) =>
            Inventory.TryGetValue(itemId, out long count) ? count : 0L;

        public static AvatarSnapshot FromStates(
            string name,
            long blockIndex,
            long stamina,
            long maxStamina,
            long gold,
            int fishingLevel, long fishingExp,
            int cookingLevel, long cookingExp,
            bool kitchenUnlocked,
            IEnumerable<KeyValuePair<int, long>> inventory)
        {
            return new AvatarSnapshot
            {
                Name = name,
                BlockIndex = blockIndex,
                Stamina = stamina,
                MaxStamina = maxStamina,
                Gold = gold,
                FishingLevel = fishingLevel,
                FishingExp = fishingExp,
                CookingLevel = cookingLevel,
                CookingExp = cookingExp,
                KitchenUnlocked = kitchenUnlocked,
                Inventory = inventory.ToDictionary(kv => kv.Key, kv => kv.Value),
            };
        }
    }
}
