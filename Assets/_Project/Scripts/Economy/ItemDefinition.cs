using UnityEngine;

namespace ProjectFossil.Economy
{
    public enum ItemCategory { Resource, Consumable, Weapon, Tool }

    [CreateAssetMenu(menuName = "Project Fossil/Item", fileName = "Item_New")]
    public class ItemDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string       itemId      = "item";
        public string       displayName = "Item";
        public ItemCategory category    = ItemCategory.Resource;
        public int          maxStack    = 1;

        [Header("Weapon")]
        public float damage   = 0f;
        public float range    = 2f;
        public float cooldown = 0.6f;

        [Header("Consumable")]
        public float healAmount = 0f;

        public bool IsWeapon     => category == ItemCategory.Weapon;
        public bool IsConsumable => category == ItemCategory.Consumable;
    }
}
