namespace ProjectFossil.Core
{
    // What the character visibly holds for a weapon. Built from simple shapes until real weapon models exist.
    public enum HeldLook { None, Spear, Club, Axe }

    public struct WeaponStats
    {
        public string   Name;
        public float    Damage;
        public float    Range;
        public float    Cooldown;
        public HeldLook Look;

        public WeaponStats(string name, float damage, float range, float cooldown, HeldLook look = HeldLook.None)
        {
            Name     = name;
            Damage   = damage;
            Range    = range;
            Cooldown = cooldown;
            Look     = look;
        }
    }

    // Lets combat code ask "what am I holding?" without depending on the economy/inventory assembly.
    public interface IWeaponProvider
    {
        bool TryGetEquippedWeapon(out WeaponStats weapon);
    }
}
