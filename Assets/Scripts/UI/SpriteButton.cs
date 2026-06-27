using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Poker
{
    /// <summary>
    /// A clickable world-space sprite button (the pattern this project already uses for the
    /// fold/call/raise sprites). Swaps to a "pressed" sprite while held and fires onClick when
    /// the press is released over the button. Works while the game is paused (Time.timeScale 0),
    /// since it polls the mouse in Update rather than relying on physics stepping.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class SpriteButton : MonoBehaviour
    {
        public Sprite Normal;
        public Sprite Pressed;
        public Action OnClick;

        SpriteRenderer _sr;
        Collider2D _col;
        Camera _cam;

        public void Init(Camera cam, Sprite normal, Sprite pressed, Action onClick)
        {
            _cam = cam;
            _sr = GetComponent<SpriteRenderer>();
            Normal = normal != null ? normal : _sr.sprite;
            Pressed = pressed;
            OnClick = onClick;
            if (_sr.sprite == null) _sr.sprite = Normal;
            _col = GetComponent<Collider2D>();
            if (_col == null) _col = gameObject.AddComponent<BoxCollider2D>(); // auto-fits the sprite
        }

        void Update()
        {
            var m = Mouse.current;
            if (m == null || _cam == null || _col == null) return;
            if (!m.leftButton.wasPressedThisFrame) return; // single press, fires on press-down

            Vector3 w = _cam.ScreenToWorldPoint(m.position.ReadValue());
            if (_col.OverlapPoint(new Vector2(w.x, w.y)))
            {
                if (Pressed != null) _sr.sprite = Pressed; // swap to the pressed art immediately
                OnClick?.Invoke();                          // then do what it needs to do
            }
        }
    }
}
