using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Poker
{
    /// <summary>
    /// Shared art for the menu / pause overlay: loads the button sprites from Resources/Menu and
    /// builds a frosted, translucent rounded panel (the "glassmorphic" look) procedurally.
    /// </summary>
    public static class MenuArt
    {
        // Button sprites copied into Resources/Menu (newgame, newgame_pressed, continue, ...).
        public static Sprite LoadButton(string name)
        {
            var all = Resources.LoadAll<Sprite>("Menu/" + name);
            if (all != null && all.Length > 0) return all[0];
            return Resources.Load<Sprite>("Menu/" + name);
        }

        static Material _spriteMat;

        // An unlit sprite material so backgrounds render correctly even in a scene with no 2D light
        // (a Lit material with no light shows up black).
        public static Material SpriteMaterial()
        {
            if (_spriteMat == null) _spriteMat = new Material(Shader.Find("Sprites/Default"));
            return _spriteMat;
        }

        // A full-screen, cover-scaled copy of the game's table background (Resources/Menu/PokerTable),
        // rendered unlit. Used as the menu / pause-screen backdrop. Returns its SpriteRenderer.
        public static SpriteRenderer CoverBackground(Camera cam, int sortingOrder)
        {
            if (cam == null) return null;
            var sprite = LoadButton("PokerTable");
            if (sprite == null) return null;

            var go = new GameObject("TableBG");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sharedMaterial = SpriteMaterial();
            sr.color = Color.white;
            sr.sortingOrder = sortingOrder;
            go.transform.position = new Vector3(cam.transform.position.x, cam.transform.position.y, 0f);

            float viewH = cam.orthographicSize * 2f, viewW = viewH * cam.aspect;
            Vector2 sz = sprite.bounds.size;
            if (sz.x > 0.001f && sz.y > 0.001f)
            {
                float s = Mathf.Max(viewW / sz.x, viewH / sz.y) * 1.02f; // uniform cover, no distortion
                go.transform.localScale = new Vector3(s, s, 1f);
            }
            return sr;
        }

        static Sprite _panel, _scrim;

        // A see-through frosted rounded panel: soft cool-white fill, a crisp bright rim and
        // anti-aliased rounded corners. 9-sliced so it scales without distorting the corners.
        public static Sprite Panel()
        {
            if (_panel != null) return _panel;
            const int s = 128, r = 36;
            const float bw = 3f; // bright border width, in px
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var fill = new Color(0.45f, 0.95f, 0.60f, 0.24f); // frosted green glass
            var rim  = new Color(0.70f, 1.00f, 0.80f, 0.85f); // light-green edge highlight
            var clear = new Color(0f, 0f, 0f, 0f);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float d = RoundedBoxDist(x + 0.5f, y + 0.5f, s, s, r); // <=0 inside (px)
                    float cov = Mathf.Clamp01(0.5f - d);                   // ~1px anti-aliased edge
                    if (cov <= 0f) { tex.SetPixel(x, y, clear); continue; }
                    float inside = -d;                                     // px in from the edge
                    Color c = inside < bw ? Color.Lerp(rim, fill, Mathf.Clamp01(inside / bw)) : fill;
                    c.a *= cov;
                    tex.SetPixel(x, y, c);
                }
            tex.Apply();
            _panel = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f, 0,
                                   SpriteMeshType.FullRect, new Vector4(r, r, r, r));
            return _panel;
        }

        // A flat 1x1 sprite used (tinted + scaled) as a full-screen dim behind the panel.
        public static Sprite Scrim()
        {
            if (_scrim != null) return _scrim;
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            _scrim = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            return _scrim;
        }

        // Classic bouncing-ball ease (0..1) for drop-in animations.
        public static float EaseOutBounce(float x)
        {
            const float n1 = 7.5625f, d1 = 2.75f;
            if (x < 1f / d1) return n1 * x * x;
            if (x < 2f / d1) { x -= 1.5f / d1;  return n1 * x * x + 0.75f; }
            if (x < 2.5f / d1) { x -= 2.25f / d1; return n1 * x * x + 0.9375f; }
            x -= 2.625f / d1; return n1 * x * x + 0.984375f;
        }

        // Drop a whole group of transforms in together from above, bouncing to their targets as one
        // unit. Pass unscaled=true when the game is paused (Time.timeScale == 0).
        public static IEnumerator DropGroup(IList<(Transform t, Vector3 target)> items,
                                            float dropHeight, float dur, bool unscaled)
        {
            Vector3 up = Vector3.up * dropHeight;
            foreach (var it in items) it.t.position = it.target + up;

            float e = 0f;
            while (e < dur)
            {
                e += unscaled ? Time.unscaledDeltaTime : Time.deltaTime;
                float b = EaseOutBounce(Mathf.Clamp01(e / dur));
                foreach (var it in items)
                    it.t.position = Vector3.LerpUnclamped(it.target + up, it.target, b);
                yield return null;
            }
            foreach (var it in items) it.t.position = it.target;
        }

        // Signed distance to a centred rounded box (negative inside), in pixels.
        static float RoundedBoxDist(float x, float y, float w, float h, float r)
        {
            float px = x - w * 0.5f, py = y - h * 0.5f;
            float qx = Mathf.Abs(px) - (w * 0.5f - r);
            float qy = Mathf.Abs(py) - (h * 0.5f - r);
            float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
            float inside = Mathf.Min(Mathf.Max(qx, qy), 0f);
            return outside + inside - r;
        }
    }
}
