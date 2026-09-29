using UnityEngine;

namespace ProjectFossil.Core
{
    // Anything the player can use with the Interact action (loot caches, doors, terminals...).
    public interface IInteractable
    {
        string Prompt { get; }
        bool CanInteract(GameObject interactor);
        void Interact(GameObject interactor);
    }
}
