using System.Collections;
using UnityEngine;

namespace Poker
{
    /// <summary>A single rendered card that can show its face or its back, and slide to a position.</summary>
    public sealed class CardView : MonoBehaviour
    {
        private SpriteRenderer _sr;
        private Sprite _face;
        private Sprite _back;
        public bool FaceUp { get; private set; }

        public static CardView Create(Transform parent, Sprite back, int sortingOrder)
        {
            var go = new GameObject("Card");
            go.transform.SetParent(parent, false);
            var cv = go.AddComponent<CardView>();
            cv._sr = go.AddComponent<SpriteRenderer>();
            cv._sr.sortingOrder = sortingOrder;
            cv._back = back;
            cv._sr.sprite = back;
            cv.FaceUp = false;
            return cv;
        }

        public void SetFace(Sprite face) => _face = face;
        public void SetBack(Sprite back) { _back = back; if (!FaceUp) _sr.sprite = back; }

        public int SortingOrder { get => _sr.sortingOrder; set => _sr.sortingOrder = value; }
        public Color Color { get => _sr.color; set => _sr.color = value; }

        public void ShowFace(bool up)
        {
            FaceUp = up;
            _sr.sprite = up ? _face : _back;
        }

        /// <summary>Flip with a quick horizontal squash so the swap reads as a turn.</summary>
        public IEnumerator FlipTo(bool up, float duration = 0.18f)
        {
            Vector3 baseScale = transform.localScale;
            float half = duration * 0.5f;
            for (float t = 0; t < half; t += Time.deltaTime)
            {
                float k = 1f - (t / half);
                transform.localScale = new Vector3(baseScale.x * k, baseScale.y, baseScale.z);
                yield return null;
            }
            ShowFace(up);
            for (float t = 0; t < half; t += Time.deltaTime)
            {
                float k = t / half;
                transform.localScale = new Vector3(baseScale.x * k, baseScale.y, baseScale.z);
                yield return null;
            }
            transform.localScale = baseScale;
        }

        public IEnumerator MoveTo(Vector3 target, float duration = 0.18f)
        {
            Vector3 start = transform.localPosition;
            if (duration <= 0f) { transform.localPosition = target; yield break; }
            for (float t = 0; t < duration; t += Time.deltaTime)
            {
                float u = Mathf.SmoothStep(0f, 1f, t / duration);
                transform.localPosition = Vector3.Lerp(start, target, u);
                yield return null;
            }
            transform.localPosition = target;
        }
    }
}
