using System.Collections.Generic;
using UnityEngine;
using ProjectFossil.Core;

namespace ProjectFossil.Economy
{
    // A searchable cache. Contents are rolled up front from the match seed, so the same seed gives the same loot.
    public class LootContainer : MonoBehaviour, IInteractable
    {
        public const string BeaconName = "Beacon";

        public string containerName = "Supply cache";

        public bool IsEmpty => _contents.Count == 0;
        public IReadOnlyList<LootDrop> Contents => _contents;

        public string Prompt => IsEmpty ? null : $"Search {containerName}";

        private readonly List<LootDrop> _contents = new List<LootDrop>();
        private Renderer _renderer;

        public void Fill(IEnumerable<LootDrop> drops)
        {
            _contents.Clear();
            _contents.AddRange(drops);
            RefreshVisual();
        }

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
        }

        public bool CanInteract(GameObject interactor) =>
            !IsEmpty && interactor.GetComponent<PlayerInventory>() != null;

        public void Interact(GameObject interactor)
        {
            var inventory = interactor.GetComponent<PlayerInventory>();
            if (inventory == null) return;

            // Whatever doesn't fit stays in the cache for later.
            for (int i = _contents.Count - 1; i >= 0; i--)
            {
                var drop = _contents[i];
                int leftover = inventory.Receive(drop);
                if (leftover == 0) _contents.RemoveAt(i);
                else _contents[i] = new LootDrop { Item = drop.Item, Amount = leftover };
            }

            RefreshVisual();
        }

        private void RefreshVisual()
        {
            if (_renderer == null) _renderer = GetComponent<Renderer>();
            if (_renderer != null)
                _renderer.material.color = IsEmpty ? new Color(0.35f, 0.3f, 0.25f) : new Color(1f, 0.8f, 0.2f);

            var beacon = transform.Find(BeaconName);
            if (beacon != null) beacon.gameObject.SetActive(!IsEmpty);
        }
    }
}
