using UnityEngine;
using ProjectFossil.Core;
using ProjectFossil.Director;
using ProjectFossil.Economy;

namespace ProjectFossil.Match
{
    // Everything a match needs, in one asset. Loaded from Resources/GameContent when not assigned.
    [CreateAssetMenu(menuName = "Project Fossil/Game Content", fileName = "GameContent")]
    public class GameContent : ScriptableObject
    {
        public const string ResourcePath = "GameContent";

        public MatchRules       matchRules;
        public DirectorSettings directorSettings;
        public ThreatCatalog    threatCatalog;
        public ShopCatalog      shopCatalog;
        public LootTable        cacheLoot;
        public LootTable        ruinsLoot;
        public ModelDefinition  playerModel; // empty = placeholder capsule

        public static GameContent LoadDefault() => Resources.Load<GameContent>(ResourcePath);
    }
}
