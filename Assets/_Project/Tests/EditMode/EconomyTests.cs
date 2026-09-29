using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ProjectFossil.Core;
using ProjectFossil.Economy;

namespace ProjectFossil.Tests.EditMode
{
    public class EconomyTests
    {
        private static ItemDefinition MakeItem(string id, int maxStack = 1,
                                               ItemCategory category = ItemCategory.Resource, float damage = 0f, float heal = 0f)
        {
            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            item.itemId      = id;
            item.displayName = id;
            item.maxStack    = maxStack;
            item.category    = category;
            item.damage      = damage;
            item.healAmount  = heal;
            return item;
        }

        // ── Currency ─────────────────────────────────────────────────────────

        [Test]
        public void Currency_SpendAndEarn_TrackTotals()
        {
            var wallet = new CurrencySystem(10);
            wallet.Earn(15);
            Assert.IsFalse(wallet.TrySpend(30));
            Assert.IsTrue(wallet.TrySpend(20));
            Assert.AreEqual(5, wallet.Balance);
            Assert.AreEqual(15, wallet.TotalEarned);
            Assert.AreEqual(20, wallet.TotalSpent);
        }

        // ── Inventory ────────────────────────────────────────────────────────

        [Test]
        public void Inventory_Add_FillsStacksThenSlots_AndReturnsOverflow()
        {
            var inv  = new Inventory(2, 4);
            var ammo = MakeItem("ammo", maxStack: 10);

            Assert.AreEqual(0, inv.Add(ammo, 15));
            Assert.AreEqual(2, inv.Stacks.Count);
            Assert.AreEqual(5, inv.Add(ammo, 10), "only 5 more fit in the second stack");
            Assert.AreEqual(20, inv.Count(ammo));
            Assert.AreEqual(0, inv.FreeSlots);
        }

        [Test]
        public void Inventory_Remove_TakesFromStacks_AndFreesSlots()
        {
            var inv  = new Inventory(3, 3);
            var herb = MakeItem("herb", maxStack: 5);
            inv.Add(herb, 7);

            Assert.IsFalse(inv.Remove(herb, 8));
            Assert.IsTrue(inv.Remove(herb, 3));
            Assert.AreEqual(4, inv.Count(herb));
            Assert.AreEqual(1, inv.Stacks.Count);
        }

        [Test]
        public void Inventory_UpgradeSlots_RespectsMax()
        {
            var inv = new Inventory(4, 6);
            Assert.IsTrue(inv.TryUpgradeSlots(2));
            Assert.IsFalse(inv.TryUpgradeSlots(1));
            Assert.AreEqual(6, inv.SlotCount);
        }

        [Test]
        public void Inventory_BestWeapon_PicksHighestDamage()
        {
            var inv   = new Inventory(4, 4);
            var club  = MakeItem("club",  category: ItemCategory.Weapon, damage: 15f);
            var spear = MakeItem("spear", category: ItemCategory.Weapon, damage: 30f);
            inv.Add(club, 1);
            inv.Add(spear, 1);
            Assert.AreEqual(spear, inv.BestWeapon());
        }

        // ── Shop ─────────────────────────────────────────────────────────────

        [Test]
        public void Shop_TryBuy_ChargesOnlyOnSuccess()
        {
            var medkit = MakeItem("medkit", maxStack: 3, category: ItemCategory.Consumable, heal: 50f);
            var offer  = new ShopCatalog.Offer { offerId = "medkit", kind = ShopOfferKind.Item, price = 30, item = medkit, amount = 1 };
            var wallet = new CurrencySystem(50);
            var inv    = new Inventory(1, 1);

            Assert.AreEqual(ShopResult.Success, ShopService.TryBuy(offer, wallet, inv));
            Assert.AreEqual(20, wallet.Balance);
            Assert.AreEqual(ShopResult.CannotAfford, ShopService.TryBuy(offer, wallet, inv));
            Assert.AreEqual(20, wallet.Balance);
            Assert.AreEqual(1, inv.Count(medkit));
        }

        [Test]
        public void Shop_TryBuy_FullInventory_DoesNotCharge()
        {
            var rock   = MakeItem("rock");
            var spear  = MakeItem("spear", category: ItemCategory.Weapon, damage: 30f);
            var offer  = new ShopCatalog.Offer { offerId = "spear", kind = ShopOfferKind.Item, price = 10, item = spear, amount = 1 };
            var wallet = new CurrencySystem(100);
            var inv    = new Inventory(1, 1);
            inv.Add(rock, 1);

            Assert.AreEqual(ShopResult.InventoryFull, ShopService.TryBuy(offer, wallet, inv));
            Assert.AreEqual(100, wallet.Balance);
        }

        [Test]
        public void Shop_WeaponAlreadyCarried_IsNotSoldAgain()
        {
            var axe    = MakeItem("axe", category: ItemCategory.Weapon, damage: 25f);
            var bandage = MakeItem("bandage", maxStack: 5, category: ItemCategory.Consumable, heal: 20f);
            var axeOffer     = new ShopCatalog.Offer { offerId = "axe", kind = ShopOfferKind.Item, price = 10, item = axe, amount = 1 };
            var bandageOffer = new ShopCatalog.Offer { offerId = "bandage", kind = ShopOfferKind.Item, price = 5, item = bandage, amount = 1 };
            var wallet = new CurrencySystem(100);
            var inv    = new Inventory(4, 4);

            Assert.AreEqual(ShopResult.Success, ShopService.TryBuy(axeOffer, wallet, inv));
            Assert.AreEqual(ShopResult.AlreadyOwned, ShopService.TryBuy(axeOffer, wallet, inv));
            Assert.AreEqual(90, wallet.Balance);
            Assert.AreEqual(1, inv.Count(axe));

            // Consumables still stack.
            Assert.AreEqual(ShopResult.Success, ShopService.TryBuy(bandageOffer, wallet, inv));
            Assert.AreEqual(ShopResult.Success, ShopService.TryBuy(bandageOffer, wallet, inv));
            Assert.AreEqual(2, inv.Count(bandage));
        }

        [Test]
        public void Shop_SlotUpgrade_StopsAtMax()
        {
            var offer  = new ShopCatalog.Offer { offerId = "slots", kind = ShopOfferKind.InventorySlots, price = 10, amount = 2 };
            var wallet = new CurrencySystem(100);
            var inv    = new Inventory(4, 6);

            Assert.AreEqual(ShopResult.Success, ShopService.TryBuy(offer, wallet, inv));
            Assert.AreEqual(ShopResult.MaxSlotsReached, ShopService.TryBuy(offer, wallet, inv));
            Assert.AreEqual(90, wallet.Balance);
            Assert.AreEqual(6, inv.SlotCount);
        }

        // ── Loot ─────────────────────────────────────────────────────────────

        private static LootTable MakeTable()
        {
            var table = ScriptableObject.CreateInstance<LootTable>();
            table.guaranteedCurrencyMin = 5;
            table.guaranteedCurrencyMax = 10;
            table.rolls = 3;
            table.emptyRollChance = 0.2f;
            table.entries = new List<LootTable.Entry>
            {
                new LootTable.Entry { item = MakeItem("medkit", 3), minAmount = 1, maxAmount = 2, weight = 1f },
                new LootTable.Entry { item = null, minAmount = 5, maxAmount = 20, weight = 2f },
            };
            return table;
        }

        [Test]
        public void LootTable_SameSeed_SameDrops()
        {
            var table = MakeTable();
            for (int seed = 0; seed < 20; seed++)
            {
                var a = table.Roll(new RNGService(seed));
                var b = table.Roll(new RNGService(seed));
                Assert.AreEqual(a.Count, b.Count, $"seed {seed}");
                for (int i = 0; i < a.Count; i++)
                {
                    Assert.AreEqual(a[i].Item, b[i].Item, $"seed {seed} drop {i}");
                    Assert.AreEqual(a[i].Amount, b[i].Amount, $"seed {seed} drop {i}");
                }
            }
        }

        [Test]
        public void LootTable_AlwaysDropsGuaranteedCurrency_AsFirstEntry()
        {
            var table = MakeTable();
            for (int seed = 0; seed < 50; seed++)
            {
                var drops = table.Roll(new RNGService(seed));
                Assert.IsTrue(drops.Count > 0 && drops[0].IsCurrency, $"seed {seed}");
                Assert.GreaterOrEqual(drops[0].Amount, table.guaranteedCurrencyMin, $"seed {seed}");
                for (int i = 1; i < drops.Count; i++)
                    Assert.IsFalse(drops[i].IsCurrency, "currency drops are merged into the first entry");
            }
        }
    }
}
