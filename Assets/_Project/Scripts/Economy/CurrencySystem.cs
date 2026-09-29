using System;

namespace ProjectFossil.Economy
{
    // In-match currency wallet. One per player team, and one for the AI Threat Director.
    // Real money never touches this.
    public class CurrencySystem
    {
        public int Balance     { get; private set; }
        public int TotalEarned { get; private set; }
        public int TotalSpent  { get; private set; }

        // (newBalance, delta)
        public event Action<int, int> BalanceChanged;

        public CurrencySystem(int startingBalance = 0)
        {
            if (startingBalance < 0) throw new ArgumentOutOfRangeException(nameof(startingBalance));
            Balance = startingBalance;
        }

        public bool CanAfford(int amount) => amount >= 0 && Balance >= amount;

        public bool TrySpend(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (Balance < amount) return false;
            Balance    -= amount;
            TotalSpent += amount;
            BalanceChanged?.Invoke(Balance, -amount);
            return true;
        }

        public void Earn(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (amount == 0) return;
            Balance     += amount;
            TotalEarned += amount;
            BalanceChanged?.Invoke(Balance, amount);
        }
    }
}
