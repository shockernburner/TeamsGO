using System;
using UnityEngine;
using ProjectFossil.Core;

namespace ProjectFossil.Economy
{
    // Scene-side holder for a player's wallet and inventory. Also supplies the equipped weapon to combat.
    public class PlayerInventory : MonoBehaviour, IWeaponProvider
    {
        public int startingSlots  = 4;
        public int maxSlots       = 12;
        public int startingCoins  = 0;

        public CurrencySystem Wallet    { get; private set; }
        public Inventory      Inventory { get; private set; }

        // (item or null for currency, amount)
        public event Action<ItemDefinition, int> PickedUp;

        private void Awake()
        {
            Wallet    = new CurrencySystem(startingCoins);
            Inventory = new Inventory(startingSlots, maxSlots);
        }

        // Returns the amount that did not fit (always 0 for currency).
        public int Receive(LootDrop drop)
        {
            if (drop.Amount <= 0) return 0;

            if (drop.IsCurrency)
            {
                Wallet.Earn(drop.Amount);
                PickedUp?.Invoke(null, drop.Amount);
                return 0;
            }

            int leftover = Inventory.Add(drop.Item, drop.Amount);
            int taken    = drop.Amount - leftover;
            if (taken > 0) PickedUp?.Invoke(drop.Item, taken);
            return leftover;
        }

        public bool TryGetEquippedWeapon(out WeaponStats weapon)
        {
            var best = Inventory?.BestWeapon();
            if (best == null)
            {
                weapon = default;
                return false;
            }
            weapon = new WeaponStats(best.displayName, best.damage, best.range, best.cooldown, best.heldLook);
            return true;
        }

        // Uses the first healing consumable if it would actually heal. Returns true if one was used.
        public bool TryUseHealingItem(Health health)
        {
            if (health == null || !health.IsAlive || health.Current >= health.Max) return false;

            var item = Inventory.FirstHealingItem();
            if (item == null) return false;

            Inventory.Remove(item, 1);
            health.Heal(item.healAmount);
            return true;
        }
    }
}
