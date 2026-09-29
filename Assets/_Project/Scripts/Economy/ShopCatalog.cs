using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectFossil.Economy
{
    public enum ShopOfferKind { Item, InventorySlots }

    [CreateAssetMenu(menuName = "Project Fossil/Shop Catalog", fileName = "Shop_New")]
    public class ShopCatalog : ScriptableObject
    {
        [Serializable]
        public class Offer
        {
            public string         offerId     = "offer";
            public string         displayName = "Offer";
            public ShopOfferKind  kind        = ShopOfferKind.Item;
            public int            price       = 10;
            public ItemDefinition item;           // for kind = Item
            public int            amount      = 1; // items, or extra slots
        }

        public List<Offer> offers = new List<Offer>();

        public Offer Find(string offerId) => offers.Find(o => o.offerId == offerId);
    }
}
