namespace ProjectFossil.Economy
{
    public enum ShopResult { Success, UnknownOffer, CannotAfford, InventoryFull, MaxSlotsReached, AlreadyOwned }

    // Pure purchase rules. Checks everything before taking money, so a failed buy never costs anything.
    public static class ShopService
    {
        public static ShopResult Evaluate(ShopCatalog.Offer offer, CurrencySystem wallet, Inventory inventory)
        {
            if (offer == null) return ShopResult.UnknownOffer;

            switch (offer.kind)
            {
                case ShopOfferKind.Item:
                    if (offer.item == null) return ShopResult.UnknownOffer;
                    if (offer.item.IsWeapon && inventory.Count(offer.item) > 0) return ShopResult.AlreadyOwned; // one of each is enough
                    if (!inventory.CanAdd(offer.item, offer.amount)) return ShopResult.InventoryFull;
                    break;
                case ShopOfferKind.InventorySlots:
                    if (inventory.SlotCount + offer.amount > inventory.MaxSlotCount) return ShopResult.MaxSlotsReached;
                    break;
            }

            return wallet.CanAfford(offer.price) ? ShopResult.Success : ShopResult.CannotAfford;
        }

        public static ShopResult TryBuy(ShopCatalog.Offer offer, CurrencySystem wallet, Inventory inventory)
        {
            var result = Evaluate(offer, wallet, inventory);
            if (result != ShopResult.Success) return result;

            wallet.TrySpend(offer.price);
            if (offer.kind == ShopOfferKind.Item) inventory.Add(offer.item, offer.amount);
            else inventory.TryUpgradeSlots(offer.amount);
            return ShopResult.Success;
        }
    }
}
