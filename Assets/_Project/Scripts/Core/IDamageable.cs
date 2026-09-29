using UnityEngine;

namespace ProjectFossil.Core
{
    public struct DamageInfo
    {
        public float      Amount;
        public GameObject Source; // who dealt it (player, dinosaur, hazard); may be null
        public Vector3    Point;

        public DamageInfo(float amount, GameObject source, Vector3 point)
        {
            Amount = amount;
            Source = source;
            Point  = point;
        }
    }

    public interface IDamageable
    {
        bool IsAlive { get; }
        void TakeDamage(DamageInfo info);
    }

    // A teammate: dinosaurs can hurt it, the team's own weapons skip it.
    public interface IFriendly { }
}
