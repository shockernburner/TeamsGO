using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectFossil.Player
{
    // Relays menu-type actions from PlayerInput (SendMessages) as C# events so the UI
    // doesn't need to know about the input system.
    public class PlayerMenuInput : MonoBehaviour
    {
        public event Action ShopToggled;
        public event Action UseItemPressed;

        public void OnShop(InputValue v)
        {
            if (v.isPressed) ShopToggled?.Invoke();
        }

        public void OnUseItem(InputValue v)
        {
            if (v.isPressed) UseItemPressed?.Invoke();
        }
    }
}
