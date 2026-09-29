namespace ProjectFossil.Economy
{
    // Pays a little for every hit instead of only for the kill. Keeps the fractions between hits, so
    // twelve 1.8-coin punches pay the same as one 21.6-coin spear thrust (give or take one coin).
    public class CoinTrickle
    {
        public float CoinsPerDamage { get; }
        private float _remainder;

        public CoinTrickle(float coinsPerDamage) => CoinsPerDamage = coinsPerDamage > 0f ? coinsPerDamage : 0f;

        // Returns the whole coins earned by this hit.
        public int AddDamage(float damage)
        {
            if (damage <= 0f || CoinsPerDamage <= 0f) return 0;
            _remainder += damage * CoinsPerDamage;
            int whole = (int)_remainder;
            _remainder -= whole;
            return whole;
        }
    }
}
