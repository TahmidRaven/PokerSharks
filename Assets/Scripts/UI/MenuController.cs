using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Poker
{
    /// <summary>
    /// Drives the main-menu scene: makes the authored New Game / Continue / Quit sprites clickable
    /// (with a pressed-sprite swap) and drops the whole menu in from the top with a bounce by
    /// animating the BG node they're parented under. Auto-spawned when the "Menu" scene is active.
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
            if (_cam != null)
            {
                _cam.clearFlags = CameraClearFlags.SolidColor;
                _cam.backgroundColor = new Color(0.03f, 0.04f, 0.06f, 1f); // shows while the BG drops in
            }

            var newGame = FindButton("New Game");
            var cont    = FindButton("Continue");
            var quit    = FindButton("Quit");

            Wire(newGame, "newgame_pressed", OnNewGame);
            Wire(cont,    "continue_pressed", OnContinue);
            Wire(quit,    "quit_pressed",  OnQuit);

            DropMenu(newGame, cont, quit);
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
#if UNITY_WEBGL && !UNITY_EDITOR
            CloseGameWindow();           // close the browser tab/window (see Plugins/WebGL/CloseWindow.jslib)
#elif UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]
        static extern void CloseGameWindow();
#endif

        // --- setup helpers ---

        void Wire(SpriteRenderer sr, string pressedResource, Action onClick)
        {
            if (sr == null) return;
            var btn = sr.gameObject.AddComponent<SpriteButton>();
            btn.Init(_cam, sr.sprite, MenuArt.LoadButton(pressedResource), onClick);
        }

        // The menu scene has no background of its own, so reuse the game's table BG (static), then
        // drop the MenuPanel (the buttons' parent) in from above with a bounce — buttons ride along.
        void DropMenu(params SpriteRenderer[] buttons)
        {
            float dropHeight = _cam != null ? _cam.orthographicSize * 3f : 16f; // clear the screen height

            MenuArt.CoverBackground(_cam, -10); // static table backdrop, behind the panel

            Transform menuPanel = null;
            foreach (var b in buttons)
                if (b != null && b.transform.parent != null) { menuPanel = b.transform.parent; break; }

            var items = new List<(Transform, Vector3)>();
            if (menuPanel != null) items.Add((menuPanel, menuPanel.position));
            else foreach (var b in buttons) if (b != null) items.Add((b.transform, b.transform.position));
            if (items.Count == 0) return;

            StartCoroutine(MenuArt.DropGroup(items, dropHeight, 0.85f, false));
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
