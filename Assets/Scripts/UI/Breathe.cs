using UnityEngine;

namespace Poker
{
    /// <summary>
    /// Gently pulses ("breathes") a transform's scale between its base and base * amount, to draw
    /// the eye. Uses unscaled time so it keeps animating while the game is paused (Time.timeScale 0).
    /// </summary>
    public sealed class Breathe : MonoBehaviour
    {
        public float amount = 1.11f;
        public float speed = 3.5f;

        Vector3 _base;

        void OnEnable() => _base = transform.localScale;

        void Update()
        {
            float t = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * speed);
            transform.localScale = _base * Mathf.Lerp(1f, amount, t);
        }
    }
}
