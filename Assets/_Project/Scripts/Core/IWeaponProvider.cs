namespace ProjectFossil.Core
{
    public struct WeaponStats
    {
        public string Name;
        public float  Damage;
        public float  Range;
        public float  Cooldown;

        public WeaponStats(string name, float damage, float range, float cooldown)
        {
            Name     = name;
            Damage   = damage;
            Range    = range;
            Cooldown = cooldown;
        }
    }

    // Lets combat code ask "what am I holding?" without depending on the economy/inventory assembly.
    public interface IWeaponProvider
    {
        bool TryGetEquippedWeapon(out WeaponStats weapon);
    }
}
