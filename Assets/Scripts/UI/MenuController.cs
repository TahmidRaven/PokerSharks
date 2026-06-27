using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Poker
{
    /// <summary>
    /// Drives the main-menu scene: makes the authored New Game / Continue / Quit sprites clickable
    /// (with a pressed-sprite swap) and draws a glassmorphic panel behind them. Auto-spawned when
    /// the "Menu" scene is the active scene, so no scene wiring is required.
    /// </summary>
    public sealed class MenuController : MonoBehaviour
    {
        const string GameScene = "game";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoBoot()
        {
            if (SceneManager.GetActiveScene().name != "Menu") return;
            if (FindFirstObjectByType<MenuController>() != null) return;
            new GameObject("MenuController").AddComponent<MenuController>();
        }

        Camera _cam;

        void Start()
        {
            Time.timeScale = 1f; // in case we returned here after a pause
            _cam = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();

            var newGame  = FindButton("New Game");
            var cont     = FindButton("Continue");
            var quit     = FindButton("Quit");

            BuildBackground();
            BuildBackdrop(new[] { newGame, cont, quit });

            Wire(newGame, "newgame_pressed", OnNewGame);
            Wire(cont,    "continue_pressed", OnContinue);
            Wire(quit,    "quit_pressed",  OnQuit);
        }

        // --- actions ---

        void OnNewGame()
        {
            GameSession.StartNew();
            SceneManager.LoadScene(GameScene);
        }

        void OnContinue()
        {
            GameSession.RequestResume(); // PokerGame restores saved stacks if a save exists
            SceneManager.LoadScene(GameScene);
        }

        void OnQuit() => QuitApp();

        public static void QuitApp()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // --- setup helpers ---

        void Wire(SpriteRenderer sr, string pressedResource, Action onClick)
        {
            if (sr == null) return;
            sr.sortingOrder = 52; // above the backdrop
            var btn = sr.gameObject.AddComponent<SpriteButton>();
            btn.Init(_cam, sr.sprite, MenuArt.LoadButton(pressedResource), onClick);
        }

        // Show the real game background behind the menu instead of the flat default-blue camera clear.
        void BuildBackground()
        {
            if (_cam == null) return;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.03f, 0.04f, 0.06f, 1f); // dark fallback behind the BG

            var bg = MenuArt.LoadButton("PokerTable"); // Resources/Menu/PokerTable
            if (bg == null) return;

            var sr = MakeSprite("MenuBG", bg, _cam.transform.position.x, _cam.transform.position.y, Color.white, 40);
            float viewH = _cam.orthographicSize * 2f, viewW = viewH * _cam.aspect;
            Vector2 size = bg.bounds.size;
            if (size.x > 0.001f && size.y > 0.001f)
            {
                float scale = Mathf.Max(viewW / size.x, viewH / size.y) * 1.02f; // cover the view
                sr.transform.localScale = new Vector3(scale, scale, 1f);
            }
        }

        // Frosted panel sized to the buttons, plus a light full-screen scrim behind it.
        void BuildBackdrop(SpriteRenderer[] buttons)
        {
            Bounds? b = null;
            foreach (var sr in buttons)
            {
                if (sr == null) continue;
                if (b == null) b = sr.bounds;
                else { var bb = b.Value; bb.Encapsulate(sr.bounds); b = bb; }
            }
            Vector3 center = b?.center ?? (_cam != null ? _cam.transform.position : Vector3.zero);
            center.z = 0f;

            if (_cam != null)
            {
                float h = _cam.orthographicSize * 2f, w = h * _cam.aspect;
                var scrim = MakeSprite("MenuScrim", MenuArt.Scrim(), _cam.transform.position.x, _cam.transform.position.y,
                                       new Color(0.02f, 0.03f, 0.05f, 0.25f), 50);
                scrim.transform.localScale = new Vector3(w * 1.3f, h * 1.3f, 1f);
            }

            if (b != null)
            {
                var panel = MakeSprite("MenuPanel", MenuArt.Panel(), center.x, center.y, Color.white, 51);
                panel.drawMode = SpriteDrawMode.Sliced;
                panel.size = new Vector2(b.Value.size.x + 1.1f, b.Value.size.y + 1.0f);
            }
        }

        static SpriteRenderer MakeSprite(string name, Sprite sprite, float x, float y, Color color, int order)
        {
            var go = new GameObject(name);
            go.transform.position = new Vector3(x, y, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
            return sr;
        }

        static SpriteRenderer FindButton(string name)
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == name)
                    {
                        var sr = t.GetComponent<SpriteRenderer>();
                        if (sr != null) return sr;
                    }
            Debug.LogWarning($"[MenuController] Button '{name}' (with a SpriteRenderer) not found in the Menu scene.");
            return null;
        }
    }
}
