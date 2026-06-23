using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace Poker
{
    /// <summary>Helpers that build runtime UI and generate the table / chip textures from code.</summary>
    public static class PokerUi
    {
        private static Font _font;
        private static Sprite _white;

        public static Font Font
        {
            get
            {
                if (_font == null)
                {
                    // Project font: Space Grotesk (Assets/Resources/Fonts/SpaceGrotesk.ttf).
                    _font = Resources.Load<Font>("Fonts/SpaceGrotesk");
                    if (_font == null)
                    {
                        Debug.LogWarning("[PokerUi] SpaceGrotesk font not found in Resources/Fonts — falling back.");
                        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    }
                    if (_font == null)
                        _font = Font.CreateDynamicFontFromOSFont(
                            new[] { "Arial", "Helvetica", "Liberation Sans", "DejaVu Sans" }, 16);
                }
                return _font;
            }
        }

        public static Sprite White
        {
            get
            {
                if (_white == null)
                {
                    var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                    var px = new Color32[16];
                    for (int i = 0; i < 16; i++) px[i] = new Color32(255, 255, 255, 255);
                    tex.SetPixels32(px); tex.Apply();
                    _white = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4);
                }
                return _white;
            }
        }

        public static RectTransform NewRect(Transform parent, string name, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static Canvas CreateOverlayCanvas(out RectTransform rect)
        {
            var go = new GameObject("PokerCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            rect = (RectTransform)go.transform;
            EnsureEventSystem();
            return canvas;
        }

        public static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem));
            var module = go.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions(); // wire up point/click via the new Input System
        }

        public static Text Label(Transform parent, string name, Vector2 pos, Vector2 size,
                                 int fontSize, TextAnchor anchor, Color color)
        {
            var rt = NewRect(parent, name, pos, size);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.fontSize = fontSize;
            t.alignment = anchor;
            t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.supportRichText = true;
            ApplyTextEffect(t);
            return t;
        }

        // A dark shadowed outline so text stays legible over the felt, cards and chips.
        public static void ApplyTextEffect(Graphic g)
        {
            var outline = g.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(1.6f, -1.6f);
            outline.useGraphicAlpha = true;

            var shadow = g.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.55f);
            shadow.effectDistance = new Vector2(2.5f, -2.5f);
            shadow.useGraphicAlpha = true;
        }

        public static Button Btn(Transform parent, string name, Vector2 pos, Vector2 size,
                                 string text, Color bg, out Text label)
        {
            var rt = NewRect(parent, name, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = White;
            img.type = Image.Type.Simple;
            img.color = bg;
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = Color.Lerp(bg, Color.white, 0.2f);
            colors.pressedColor = Color.Lerp(bg, Color.black, 0.2f);
            colors.disabledColor = new Color(bg.r, bg.g, bg.b, 0.35f);
            colors.fadeDuration = 0.05f;
            btn.colors = colors;

            label = Label(rt, name + "Label", Vector2.zero, size, 30, TextAnchor.MiddleCenter, Color.white);
            label.fontStyle = FontStyle.Bold;
            return btn;
        }

        public static Slider HSlider(Transform parent, Vector2 pos, Vector2 size)
        {
            var rootRt = NewRect(parent, "RaiseSlider", pos, size);
            var slider = rootRt.gameObject.AddComponent<Slider>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.wholeNumbers = true;

            // Background bar.
            var bg = NewRect(rootRt, "Background", Vector2.zero, Vector2.zero);
            Stretch(bg);
            var bgImg = bg.gameObject.AddComponent<Image>();
            bgImg.sprite = White; bgImg.color = new Color(0f, 0f, 0f, 0.5f);

            // Fill.
            var fillArea = NewRect(rootRt, "Fill Area", Vector2.zero, Vector2.zero);
            Stretch(fillArea);
            var fill = NewRect(fillArea, "Fill", Vector2.zero, new Vector2(10, 0));
            fill.anchorMin = new Vector2(0, 0); fill.anchorMax = new Vector2(0, 1);
            fill.pivot = new Vector2(0, 0.5f); fill.offsetMin = Vector2.zero; fill.offsetMax = new Vector2(10, 0);
            var fillImg = fill.gameObject.AddComponent<Image>();
            fillImg.sprite = White; fillImg.color = new Color(0.78f, 0.56f, 0.16f, 1f);

            // Handle.
            var handleArea = NewRect(rootRt, "Handle Slide Area", Vector2.zero, Vector2.zero);
            Stretch(handleArea);
            var handle = NewRect(handleArea, "Handle", Vector2.zero, new Vector2(18, size.y + 8));
            handle.anchorMin = new Vector2(0, 0.5f); handle.anchorMax = new Vector2(0, 0.5f);
            var handleImg = handle.gameObject.AddComponent<Image>();
            handleImg.sprite = White; handleImg.color = Color.white;

            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handleImg;
            return slider;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        // ---- Procedural table felt (oval with a wood rail) ----
        public static Sprite MakeTableSprite()
        {
            const int W = 560, H = 320;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color32[W * H];

            Color32 clear = new Color32(0, 0, 0, 0);
            Color32 rail = new Color32(74, 47, 28, 255);     // wood
            Color32 railDark = new Color32(46, 29, 17, 255);
            Color32 feltOuter = new Color32(20, 96, 55, 255);
            Color32 feltInner = new Color32(28, 120, 70, 255);
            Color32 trim = new Color32(232, 200, 120, 255);   // gold line

            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    float nx = (x + 0.5f) / W * 2f - 1f;
                    float ny = (y + 0.5f) / H * 2f - 1f;
                    float r = nx * nx + ny * ny; // unit-ellipse test in normalized space
                    Color32 c;
                    if (r > 1.0f) c = clear;
                    else if (r > 0.93f) c = railDark;
                    else if (r > 0.82f) c = rail;
                    else if (r > 0.80f) c = trim;
                    else if (r > 0.45f) c = feltOuter;
                    else c = feltInner;
                    px[y * W + x] = c;
                }
            }
            tex.SetPixels32(px); tex.Apply();
            // pixelsPerUnit chosen so the table is ~14 world units wide.
            return Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), W / 14f);
        }

        // ---- Small dealer-button disc (white, tinted at use) ----
        public static Sprite MakeDiscSprite()
        {
            const int D = 64;
            var tex = new Texture2D(D, D, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color32[D * D];
            Color32 white = new Color32(245, 245, 245, 255);
            Color32 ring = new Color32(60, 60, 60, 255);
            Color32 clear = new Color32(0, 0, 0, 0);
            for (int y = 0; y < D; y++)
                for (int x = 0; x < D; x++)
                {
                    float nx = (x + 0.5f) / D * 2f - 1f;
                    float ny = (y + 0.5f) / D * 2f - 1f;
                    float r = nx * nx + ny * ny;
                    px[y * D + x] = r > 1f ? clear : (r > 0.74f ? ring : white);
                }
            tex.SetPixels32(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, D, D), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
