using System;
using System.Collections.Generic;

namespace ProjectFossil.Economy
{
    public class ItemStack
    {
        public ItemDefinition Item   { get; }
        public int            Amount { get; internal set; }

        public ItemStack(ItemDefinition item, int amount)
        {
            Item   = item;
            Amount = amount;
        }
    }

    // Pure slot-based inventory. Each slot holds one stack of up to item.maxStack.
    public class Inventory
    {
        public int SlotCount    { get; private set; }
        public int MaxSlotCount { get; }
        public IReadOnlyList<ItemStack> Stacks => _stacks;
        public int FreeSlots => SlotCount - _stacks.Count;

        public event Action Changed;

        private readonly List<ItemStack> _stacks = new List<ItemStack>();

        public Inventory(int slotCount, int maxSlotCount)
        {
            if (slotCount <= 0) throw new ArgumentOutOfRangeException(nameof(slotCount));
            if (maxSlotCount < slotCount) throw new ArgumentOutOfRangeException(nameof(maxSlotCount));
            SlotCount    = slotCount;
            MaxSlotCount = maxSlotCount;
        }

        public int Count(ItemDefinition item)
        {
            int total = 0;
            foreach (var s in _stacks)
                if (s.Item == item) total += s.Amount;
            return total;
        }

        // How many of this item could be added right now.
        public int SpaceFor(ItemDefinition item)
        {
            int stackSize = Math.Max(1, item.maxStack);
            int space = FreeSlots * stackSize;
            foreach (var s in _stacks)
                if (s.Item == item) space += stackSize - s.Amount;
            return space;
        }

        public bool CanAdd(ItemDefinition item, int amount) => amount > 0 && SpaceFor(item) >= amount;

        // Adds as many as fit. Returns the amount that did NOT fit.
        public int Add(ItemDefinition item, int amount)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (amount <= 0) return 0;

            int stackSize = Math.Max(1, item.maxStack);
            int remaining = amount;

            foreach (var s in _stacks)
            {
                if (remaining == 0) break;
                if (s.Item != item || s.Amount >= stackSize) continue;
                int moved = Math.Min(remaining, stackSize - s.Amount);
                s.Amount  += moved;
                remaining -= moved;
            }

            while (remaining > 0 && FreeSlots > 0)
            {
                int moved = Math.Min(remaining, stackSize);
                _stacks.Add(new ItemStack(item, moved));
                remaining -= moved;
            }

            if (remaining != amount) Changed?.Invoke();
            return remaining;
        }

        public bool Remove(ItemDefinition item, int amount)
        {
            if (amount <= 0 || Count(item) < amount) return false;

            for (int i = _stacks.Count - 1; i >= 0 && amount > 0; i--)
            {
                var s = _stacks[i];
                if (s.Item != item) continue;
                int taken = Math.Min(amount, s.Amount);
                s.Amount -= taken;
                amount   -= taken;
                if (s.Amount == 0) _stacks.RemoveAt(i);
            }

            Changed?.Invoke();
            return true;
        }

        public bool TryUpgradeSlots(int extraSlots)
        {
            if (extraSlots <= 0 || SlotCount + extraSlots > MaxSlotCount) return false;
            SlotCount += extraSlots;
            Changed?.Invoke();
            return true;
        }

        // Highest-damage weapon carried, or null.
        public ItemDefinition BestWeapon()
        {
            ItemDefinition best = null;
            foreach (var s in _stacks)
                if (s.Item.IsWeapon && (best == null || s.Item.damage > best.damage))
                    best = s.Item;
            return best;
        }

        // First consumable that heals, or null.
        public ItemDefinition FirstHealingItem()
        {
            foreach (var s in _stacks)
                if (s.Item.IsConsumable && s.Item.healAmount > 0f)
                    return s.Item;
            return null;
        }
    }
}
