namespace ProjectFossil.Economy
{
    public class CurrencySystem
    {
        public int Balance { get; private set; }

        public bool TrySpend(int amount)
        {
            if (Balance < amount) return false;
            Balance -= amount;
            return true;
        }

        public void Earn(int amount) => Balance += amount;
    }
}
