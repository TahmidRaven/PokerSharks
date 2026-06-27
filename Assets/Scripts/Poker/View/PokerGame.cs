using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Poker
{
    /// <summary>
    /// Top-down Texas Hold'em: 1 human vs 4 AI bots. Builds the whole table, cards and UI
    /// from code at runtime, then runs the hand loop. Auto-spawned in the "game" scene.
    /// </summary>
    public sealed class PokerGame : MonoBehaviour
    {
        // --- config ---
        const int NumPlayers = 5;
        const int StartingChips = 1000;
        const int SmallBlind = 5;
        const int BigBlind = 10;

        static readonly string[] BotNames = { "You", "Midari", "Kirari", "Mary", "Ririka" };

        [Header("Scene wiring (optional — left empty, falls back to find-by-name then defaults)")]
        [Tooltip("Your human player node (the \"Me\" object).")]
        [SerializeField] Transform humanSeat;
        [Tooltip("The four bot nodes, in order P1, P2, P3, P4.")]
        [SerializeField] Transform[] botSeats = new Transform[4];

        [Header("Options")]
        [Tooltip("Draw the procedural oval felt. Uncheck if your BG sprite already shows a table.")]
        [SerializeField] bool buildTableFelt = true;
        [Tooltip("Put the pot / community cards at the average of the seat positions.")]
        [SerializeField] bool autoCenterFromSeats = true;
        [Tooltip("Used only when Auto Center is off.")]
        [SerializeField] Vector2 tableCenter = new Vector2(0f, 0.3f);

        [Header("Card sizes (on-screen scale, independent of the seat node's own scale)")]
        [Tooltip("Your hole cards.")]
        [SerializeField] float humanCardScale = 1.0f;
        [Tooltip("A bot's face-down hole cards.")]
        [SerializeField] float botCardScale = 0.5f;
        [Tooltip("Size a bot's cards grow to when shown at showdown.")]
        [SerializeField] float botRevealCardScale = 1.0f;

        [Header("Win / lose effects")]
        [Tooltip("Confetti prefab spawned when you win the pot. Loaded from Resources by default; " +
                 "drop in your own prefab to override.")]
        [SerializeField] GameObject winEffectPrefab;
        [Tooltip("Burst prefab spawned when you lose the hand. Left empty, a burst is built from code.")]
        [SerializeField] GameObject loseEffectPrefab;
        [Tooltip("Scale of the win confetti.")]
        [SerializeField] float winEffectScale = 1.0f;
        [Tooltip("Scale of the lose burst.")]
        [SerializeField] float loseEffectScale = 1.0f;
        [Tooltip("Playback speed of the win effect (1 = the prefab's own speed, 2 = twice as fast).")]
        [SerializeField] float winEffectSpeed = 1.0f;
        [Tooltip("Playback speed of the lose effect (1 = the prefab's own speed, 2 = twice as fast).")]
        [SerializeField] float loseEffectSpeed = 1.0f;

        // Resources path to the confetti prefab imported from Confetti_Particle_Effect.unitypackage.
        const string WinEffectResource = "Poker/Confetti_Particles_Sphere";

        sealed class SeatVisual
        {
            public int Seat;
            public Vector3 Anchor;
            public Vector3 Dir;            // toward table centre
            public Vector3[] HolePos = new Vector3[2];
            public float HoleScale;
            public float RevealScale;      // scale to use when the cards are shown at showdown
            public Vector3[] RevealPos = new Vector3[2]; // spread-out spots for shown cards
            public bool FaceUp;            // human shows cards
            public Transform Avatar;
            public CardView[] Hole = new CardView[2];
            public Text Info;
            public Text Bet;
            public Vector3 InfoWorld;
            public Vector3 BetWorld;
            public string ActionText = "";
        }

        Camera _cam;
        PokerArt _art;
        PokerEngine _engine;
        BotBrain[] _brains;
        SeatVisual[] _seats;
        Transform _tableRoot;
        SpriteRenderer _dealerDisc;
        Transform _dealerCardsRoot;   // community cards parent (the "DealerCards" scene node)
        Transform _potChipsRoot;      // pot chips parent (the "PotChips" scene node)
        SpriteRenderer[] _potChips;

        readonly List<CardView> _board = new List<CardView>(5);
        Sprite _backSprite;
        System.Random _rng;

        GameObject _activeEffect;       // win/lose VFX instance, cleared when the next hand starts
        int _humanChipsAtHandStart;     // human stack at the top of the hand, to tell win from loss
        static Material _fxMaterial;     // URP-safe particle material shared by the effects

        bool _paused;                   // pause menu open
        GameObject _pauseOverlay;       // the in-game pause overlay instance
        const float MenuPanelScale = 1.5f; // matches the authored MenuPanel node's scale

        // juice
        Vector3 _camBasePos;            // camera rest position (shake returns here)
        Coroutine _shakeCo;
        float _shownPot;                // animated pot readout (counts up to the real pot)
        int _actingSeat = -1;
        SpriteRenderer _actingGlow;     // pulsing spotlight under the acting player

        // UI
        RectTransform _canvasRect;
        Text _potText, _statusText, _raiseValueText;
        Slider _raiseSlider;

        // Action buttons authored in the scene under the "Buttons" node (world-space sprites).
        Transform _buttonsNode;
        SpriteRenderer _foldBtnSr, _callBtnSr, _raiseBtnSr;
        Collider2D _foldCol, _callCol, _raiseCol;
        Text _foldText, _callText, _raiseText;
        bool _foldEnabled, _callEnabled, _raiseEnabled;
        static readonly Color BtnOn = Color.white;
        static readonly Color BtnOff = new Color(1f, 1f, 1f, 0.35f);

        readonly List<(RectTransform rt, Vector3 world)> _worldLabels = new List<(RectTransform, Vector3)>();

        // human input state
        bool _awaitingHuman, _actionReady;
        PlayerAction _pending;
        LegalActions _currentLegal;

        Vector3 _tableCenter = new Vector3(0f, 0.3f, 0f);

        static readonly Vector3[] DefaultAnchors =
        {
            new Vector3(0f, -3.3f, 0f),
            new Vector3(5.0f, -1.7f, 0f),
            new Vector3(3.4f, 2.9f, 0f),
            new Vector3(-3.4f, 2.9f, 0f),
            new Vector3(-5.0f, -1.7f, 0f),
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoBoot()
        {
            if (SceneManager.GetActiveScene().name != "game") return;
            if (FindFirstObjectByType<PokerGame>() != null) return;
            new GameObject("PokerGame").AddComponent<PokerGame>();
        }

        void Start()
        {
            _rng = new System.Random();
            _art = new PokerArt();
            _art.Load();
            if (!_art.Loaded)
                Debug.LogError("[PokerGame] Card art failed to load from Resources/Poker.");
            _backSprite = _art.Back(false);

            SetupCamera();
            BuildSeats();
            BuildTable();
            BuildEngine();
            BuildUI();
            WireSceneButtons();
            WireCrossButton();
            SetupAudio();
            StartCoroutine(RunGame());
        }

        void SetupAudio()
        {
            // Play background music from existing AudioManager nodes
            AudioManager.instance.PlayAudio("BGM");
        }

        // ---------------- scene build ----------------

        void SetupCamera()
        {
            _cam = Camera.main;
            if (_cam == null)
            {
                var go = new GameObject("Main Camera", typeof(Camera));
                go.tag = "MainCamera";
                go.transform.position = new Vector3(0, 0, -10);
                _cam = go.GetComponent<Camera>();
            }
            _cam.orthographic = true;
            _cam.orthographicSize = 5.7f;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.05f, 0.16f, 0.10f, 1f);
            _camBasePos = _cam.transform.position;
        }

        void BuildTable()
        {
            _tableRoot = new GameObject("TableRoot").transform;
            _tableRoot.position = Vector3.zero;

            if (buildTableFelt)
            {
                var felt = new GameObject("FeltTable");
                felt.transform.SetParent(_tableRoot, false);
                felt.transform.position = _tableCenter + new Vector3(0f, -0.1f, 0f);
                var sr = felt.AddComponent<SpriteRenderer>();
                sr.sprite = PokerUi.MakeTableSprite();
                sr.sortingOrder = 1;
            }

            // Community cards and pot chips hang off scene nodes you can reposition in the editor.
            // A node left at the origin is auto-placed at the table centre; a node you've moved is
            // left where it is, and its cards/chips follow it.
            _dealerCardsRoot = ResolveAnchor("DealerCards", _tableCenter + new Vector3(0f, 0.45f, 0f));
            _potChipsRoot = ResolveAnchor("PotChips", _tableCenter + new Vector3(0f, -0.8f, 0f));
            BuildPotChips();

            var disc = new GameObject("DealerButton");
            disc.transform.SetParent(_tableRoot, false);
            disc.transform.localScale = Vector3.one * 0.4f;
            _dealerDisc = disc.AddComponent<SpriteRenderer>();
            _dealerDisc.sprite = PokerUi.MakeDiscSprite();
            _dealerDisc.sortingOrder = 4;
            _dealerDisc.enabled = false;

            // Soft pulsing spotlight that sits under whoever is acting (positioned in HighlightActing).
            var glow = new GameObject("ActingGlow");
            glow.transform.SetParent(_tableRoot, false);
            glow.transform.localScale = Vector3.one * 1.8f;
            _actingGlow = glow.AddComponent<SpriteRenderer>();
            _actingGlow.sprite = PokerUi.MakeDiscSprite();
            _actingGlow.color = new Color(1f, 0.85f, 0.2f, 0f);
            _actingGlow.sortingOrder = 2; // above the background, below the cards
            _actingGlow.enabled = false;
        }

        // Find a scene node by name (creating it if absent) to use as a parent/anchor.
        // Auto-places it at the table spot only when it sits at the origin (i.e. untouched).
        static Transform ResolveAnchor(string name, Vector3 autoPos)
        {
            var go = GameObject.Find(name);
            if (go == null) go = new GameObject(name);
            if (go.transform.position.sqrMagnitude < 0.0001f)
                go.transform.position = autoPos;
            return go.transform;
        }

        // A small pile of chips parented under the PotChips node; how many show scales with the pot.
        void BuildPotChips()
        {
            int[] colors = { 1, 3, 2, 5, 4, 6 }; // red, blue, green, yellow, black, orange
            _potChips = new SpriteRenderer[colors.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                var chip = new GameObject("PotChip" + i);
                chip.transform.SetParent(_potChipsRoot, false);
                int col = i % 2, row = i / 2;
                chip.transform.localPosition = new Vector3(col == 0 ? -0.16f : 0.16f, row * 0.07f, 0f);
                chip.transform.localScale = Vector3.one * 0.5f;
                var sr = chip.AddComponent<SpriteRenderer>();
                sr.sprite = _art.Chip(colors[i]);
                sr.sortingOrder = 5 + i; // higher chips draw on top of the pile
                sr.enabled = false;
                _potChips[i] = sr;
            }
        }

        // Reveal a chip for every ~60 in the pot (at least one when there's any pot).
        void ShowPotChips(int pot)
        {
            if (_potChips == null) return;
            int show = pot <= 0 ? 0 : Mathf.Clamp(1 + pot / 60, 1, _potChips.Length);
            for (int i = 0; i < _potChips.Length; i++)
                _potChips[i].enabled = i < show;
        }

        void BuildSeats()
        {
            // Resolve the five seat transforms (index 0 = human "Me", 1..4 = bots P1..P4).
            // Priority: Inspector reference → find-by-name (including nested nodes) → a generated fallback.
            var t = new Transform[NumPlayers];
            t[0] = humanSeat != null ? humanSeat : FindOrCreate("Me", DefaultAnchors[0], "player0", "player");
            for (int i = 1; i < NumPlayers; i++)
            {
                Transform assigned = (botSeats != null && i - 1 < botSeats.Length) ? botSeats[i - 1] : null;
                t[i] = assigned != null ? assigned : FindOrCreate("P" + i, DefaultAnchors[i], "player" + i, "Player" + i);
            }

            // Table centre = where the pot / community cards sit.
            if (autoCenterFromSeats)
            {
                Vector3 sum = Vector3.zero;
                foreach (var tr in t) sum += tr.position;
                _tableCenter = sum / NumPlayers;
                _tableCenter.z = 0f;
            }
            else
            {
                _tableCenter = new Vector3(tableCenter.x, tableCenter.y, 0f);
            }

            _seats = new SeatVisual[NumPlayers];
            for (int i = 0; i < NumPlayers; i++)
                _seats[i] = MakeSeat(i, t[i], i == 0);
        }

        // Build a seat's layout from its node, deriving card/label spots relative to the centre.
        SeatVisual MakeSeat(int index, Transform node, bool human)
        {
            var s = new SeatVisual { Seat = index, Avatar = node };
            s.Anchor = node.position; s.Anchor.z = 0f;

            Vector3 dir = _tableCenter - s.Anchor;
            s.Dir = dir.sqrMagnitude < 0.0001f ? Vector3.up : dir.normalized;
            Vector3 perp = new Vector3(-s.Dir.y, s.Dir.x, 0f);

            if (human)
            {
                s.FaceUp = true;
                s.HoleScale = humanCardScale;
                Vector3 hc = s.Anchor + s.Dir * 1.15f;
                s.HolePos[0] = hc - perp * 0.5f;
                s.HolePos[1] = hc + perp * 0.5f;
                s.InfoWorld = s.Anchor - s.Dir * 1.15f;
                s.BetWorld = s.Anchor + s.Dir * 2.0f;
                // Your cards are full size and face-up from the deal — no separate reveal layout.
                s.RevealScale = s.HoleScale;
                s.RevealPos[0] = s.HolePos[0];
                s.RevealPos[1] = s.HolePos[1];
            }
            else
            {
                s.FaceUp = false;
                s.HoleScale = botCardScale;
                Vector3 hc = s.Anchor + s.Dir * 0.85f;
                s.HolePos[0] = hc - perp * 0.16f;
                s.HolePos[1] = hc + perp * 0.16f;
                s.InfoWorld = s.Anchor - s.Dir * 1.2f;
                s.BetWorld = s.Anchor + s.Dir * 1.7f;
                // When shown at showdown, grow and spread apart so both read clearly.
                s.RevealScale = botRevealCardScale;
                s.RevealPos[0] = hc - perp * 0.5f;
                s.RevealPos[1] = hc + perp * 0.5f;
            }
            return s;
        }

        static Transform FindOrCreate(string name, Vector3 fallback, params string[] aliases)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(name))
            {
                candidates.Add(name);
                candidates.Add(name.ToLowerInvariant());
                candidates.Add(name.ToUpperInvariant());
            }
            if (aliases != null)
            {
                foreach (var alias in aliases)
                {
                    if (string.IsNullOrEmpty(alias)) continue;
                    if (!candidates.Contains(alias)) candidates.Add(alias);
                    if (!candidates.Contains(alias.ToLowerInvariant())) candidates.Add(alias.ToLowerInvariant());
                }
            }

            foreach (var candidate in candidates)
            {
                var found = FindSceneTransform(candidate);
                if (found != null) return found;
            }

            var created = new GameObject("Seat_" + name);
            created.transform.position = fallback;
            return created.transform;
        }

        static Transform FindSceneTransform(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Transform fallback = null;
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (var child in root.GetComponentsInChildren<Transform>(true))
                {
                    if (!child.name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                    // When several nodes share a name (e.g. an empty "Me" group wrapping the
                    // "Me" avatar) prefer the one carrying the avatar sprite, not the parent.
                    if (child.GetComponent<SpriteRenderer>() != null) return child;
                    fallback ??= child;
                }
            }
            return fallback;
        }

        void BuildEngine()
        {
            _engine = new PokerEngine(_rng) { SmallBlind = SmallBlind, BigBlind = BigBlind };
            _brains = new BotBrain[NumPlayers];
            for (int i = 0; i < NumPlayers; i++)
            {
                var p = new SeatPlayer
                {
                    Seat = i,
                    Name = BotNames[i % BotNames.Length],
                    IsHuman = (i == 0),
                    Chips = StartingChips
                };
                _engine.Players.Add(p);
                if (i != 0) _brains[i] = BotBrain.CreateRandom(_rng);
            }
            _engine.ButtonIndex = NumPlayers - 1; // so first hand's button moves to seat 0

            // Continue: restore the previous session's stacks (New Game cleared the save).
            if (GameSession.ResumeRequested && GameSession.TryLoad(out var savedChips, out int savedButton))
            {
                for (int i = 0; i < NumPlayers && i < savedChips.Length; i++)
                    _engine.Players[i].Chips = savedChips[i];
                _engine.ButtonIndex = savedButton;
            }
            GameSession.ResumeRequested = false;
        }

        void SaveSession()
        {
            var chips = new int[_engine.Players.Count];
            for (int i = 0; i < chips.Length; i++) chips[i] = _engine.Players[i].Chips;
            GameSession.Save(chips, _engine.ButtonIndex);
        }

        // ---------------- UI ----------------

        void BuildUI()
        {
            PokerUi.CreateOverlayCanvas(out _canvasRect);

            _statusText = PokerUi.Label(_canvasRect, "Status", new Vector2(0, 330), new Vector2(1400, 80),
                52, TextAnchor.MiddleCenter, new Color(1f, 0.93f, 0.6f));
            _statusText.fontStyle = FontStyle.Bold;

            _potText = PokerUi.Label(_canvasRect, "Pot", new Vector2(0, 150), new Vector2(600, 60),
                40, TextAnchor.MiddleCenter, Color.white);
            _potText.fontStyle = FontStyle.Bold;
            _worldLabels.Add(((RectTransform)_potText.transform, _tableCenter + new Vector3(0f, 1.7f, 0f)));

            // Raise-amount slider + readout. The FOLD / CALL_CHECK / BET_RAISE buttons themselves
            // are the sprites authored in the scene (wired up in WireSceneButtons); this slider just
            // lets the player pick how much to raise. It's repositioned above the buttons each frame.
            _raiseValueText = PokerUi.Label(_canvasRect, "RaiseValue", Vector2.zero, new Vector2(360, 40),
                28, TextAnchor.MiddleCenter, new Color(1f, 0.9f, 0.55f));
            _raiseSlider = PokerUi.HSlider(_canvasRect, Vector2.zero, new Vector2(360, 22));
            _raiseSlider.onValueChanged.AddListener(_ => UpdateRaiseValueText());
            _raiseSlider.gameObject.SetActive(false);
            _raiseValueText.gameObject.SetActive(false);

            // Per-seat info + bet labels (positioned over the world each frame).
            foreach (var s in _seats)
            {
                s.Info = PokerUi.Label(_canvasRect, "Info" + s.Seat, Vector2.zero, new Vector2(260, 110),
                    26, TextAnchor.MiddleCenter, Color.white);
                s.Info.fontStyle = FontStyle.Bold;
                s.Bet = PokerUi.Label(_canvasRect, "Bet" + s.Seat, Vector2.zero, new Vector2(180, 40),
                    26, TextAnchor.MiddleCenter, new Color(1f, 0.9f, 0.55f));
                _worldLabels.Add(((RectTransform)s.Info.transform, s.InfoWorld));
                _worldLabels.Add(((RectTransform)s.Bet.transform, s.BetWorld));
            }
        }

        void LateUpdate()
        {
            if (_cam == null || _canvasRect == null) return;
            foreach (var (rt, world) in _worldLabels)
            {
                Vector3 sp = _cam.WorldToScreenPoint(world);
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, sp, null, out var local))
                    rt.anchoredPosition = local;
            }

            // Pot counts up toward the real value instead of snapping.
            if (_potText != null && _engine != null)
            {
                int target = _engine.Pot;
                _shownPot = Mathf.MoveTowards(_shownPot, target, (Mathf.Abs(target - _shownPot) * 6f + 250f) * Time.deltaTime);
                int shown = Mathf.RoundToInt(_shownPot);
                _potText.text = shown > 0 ? $"POT  ${shown}" : "";
            }

            // Pulse the acting-player spotlight.
            if (_actingGlow != null && _actingGlow.enabled)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 6f);
                var c = _actingGlow.color; c.a = 0.16f + 0.20f * pulse; _actingGlow.color = c;
                _actingGlow.transform.localScale = Vector3.one * (1.7f + 0.18f * pulse);
            }
        }

        void UpdateRaiseValueText()
        {
            if (_raiseValueText != null && _raiseSlider != null)
                _raiseValueText.text = $"Raise to  ${(int)_raiseSlider.value}";
        }

        // ---------------- scene action buttons ----------------

        // The Buttons node ships inactive (so GameObject.Find can't see it). Locate it, switch it on,
        // give each sprite a click collider, and start with everything dimmed until it's the human's turn.
        void WireSceneButtons()
        {
            var node = FindInScene("Buttons");
            if (node == null)
            {
                Debug.LogError("[PokerGame] No 'Buttons' node in the scene — using keyboard (F/C/R) only.");
                return;
            }
            _buttonsNode = node.transform;
            _buttonsNode.gameObject.SetActive(true); // activate when the game starts

            _foldBtnSr = SetupButton("FOLD", out _foldCol);
            _callBtnSr = SetupButton("CALL_CHECK", out _callCol);
            _raiseBtnSr = SetupButton("BET_RAISE", out _raiseCol);

            // The sprites carry no text, so overlay a label centered on each (tracked in LateUpdate).
            _foldText = MakeButtonLabel(_foldBtnSr, "FOLD");
            _callText = MakeButtonLabel(_callBtnSr, "CALL");
            _raiseText = MakeButtonLabel(_raiseBtnSr, "RAISE");

            // Float the raise slider + readout just above the BET_RAISE button (tracked in LateUpdate).
            if (_raiseBtnSr != null)
            {
                Vector3 anchor = _raiseBtnSr.transform.position;
                _worldLabels.Add(((RectTransform)_raiseSlider.transform, anchor + new Vector3(0f, 1.05f, 0f)));
                _worldLabels.Add(((RectTransform)_raiseValueText.transform, anchor + new Vector3(0f, 1.5f, 0f)));
            }

            HideControls();
        }

        // Find a named button under the Buttons node, add a click collider, and lift it above the table.
        SpriteRenderer SetupButton(string name, out Collider2D col)
        {
            col = null;
            Transform t = null;
            foreach (var child in _buttonsNode.GetComponentsInChildren<Transform>(true))
                if (child.name == name) { t = child; break; }
            if (t == null)
            {
                Debug.LogWarning($"[PokerGame] Button '{name}' not found under the Buttons node.");
                return null;
            }
            var sr = t.GetComponent<SpriteRenderer>();
            if (sr != null) sr.sortingOrder = 20; // keep buttons above the felt and avatars
            col = t.GetComponent<Collider2D>();
            if (col == null) col = t.gameObject.AddComponent<BoxCollider2D>(); // auto-fits the sprite bounds
            return sr;
        }

        // A canvas text label centered over a world-space button sprite.
        Text MakeButtonLabel(SpriteRenderer btn, string initial)
        {
            if (btn == null) return null;
            var label = PokerUi.Label(_canvasRect, btn.name + "Text", Vector2.zero, new Vector2(320, 90),
                34, TextAnchor.MiddleCenter, Color.white);
            label.fontStyle = FontStyle.Bold;
            label.text = initial;
            _worldLabels.Add(((RectTransform)label.transform, btn.transform.position));
            return label;
        }

        // Depth-first scene search that also returns inactive objects (unlike GameObject.Find).
        static GameObject FindInScene(string name)
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == name) return t.gameObject;
            return null;
        }

        // ---------------- pause menu (CrossButton → glassmorphic overlay) ----------------

        // Make the authored CrossButton open the pause menu. It polls input itself, so it works
        // any time (during bot turns too) and while paused.
        void WireCrossButton()
        {
            var cross = FindInScene("CrossButton");
            if (cross == null) { Debug.LogWarning("[PokerGame] No 'CrossButton' node in the scene."); return; }
            var sr = cross.GetComponent<SpriteRenderer>();
            if (sr != null) sr.sortingOrder = 25; // above the table, below the pause overlay
            var btn = cross.GetComponent<SpriteButton>();
            if (btn == null) btn = cross.AddComponent<SpriteButton>();
            btn.Init(_cam, sr != null ? sr.sprite : null, null, OpenPauseMenu);
        }

        void OpenPauseMenu()
        {
            if (_paused) return;
            _paused = true;
            Time.timeScale = 0f;       // freezes the WaitForSeconds-driven hand loop
            // The HUD is a ScreenSpaceOverlay canvas, which always draws over world sprites — hide it
            // so the pause overlay is the front-most thing on screen.
            if (_canvasRect != null) _canvasRect.gameObject.SetActive(false);
            BuildPauseOverlay();
        }

        // Continue: resume the in-progress game exactly where it paused.
        void ResumeGame()
        {
            _paused = false;
            Time.timeScale = 1f;
            if (_canvasRect != null) _canvasRect.gameObject.SetActive(true);
            if (_pauseOverlay != null) Destroy(_pauseOverlay);
            _pauseOverlay = null;
        }

        void OverlayNewGame()
        {
            GameSession.StartNew();
            Time.timeScale = 1f;
            SceneManager.LoadScene("game");
        }

        void BuildPauseOverlay()
        {
            _pauseOverlay = new GameObject("PauseOverlay");

            // Spawn at the "Start_PauseNode" anchor if it exists (drop that empty node below BG and
            // move it to control where the menu appears); otherwise centre on the camera. We use its
            // position only (not its parent/scale) so a scaled BG parent can't distort the menu.
            var anchor = FindInScene("Start_PauseNode");
            Vector3 c = anchor != null ? anchor.transform.position
                      : (_cam != null ? _cam.transform.position : Vector3.zero);
            c.z = 0f;
            _pauseOverlay.transform.position = c;
            _pauseOverlay.transform.localScale = Vector3.one * MenuPanelScale; // inherit the menu's panel scale

            // No background here — the live (frozen) game is the backdrop. Build the MenuPanel: panel
            // art at the centre plus the three buttons (LOCAL positions, so they scale with the panel).
            var panelSprite = MenuArt.LoadButton("MenuPanel"); // Resources/Menu/MenuPanel
            if (panelSprite != null)
                MakeOverlaySprite("MenuPanel", panelSprite, Vector3.zero, Color.white, 101);

            var defs = new (string normal, string pressed, Action act)[]
            {
                ("newgame",  "newgame_pressed",  OverlayNewGame),
                ("continue", "continue_pressed", ResumeGame),
                ("quit",     "quit_pressed",     MenuController.QuitApp),
            };
            // Local layout copied from the authored MenuPanel children — y offsets and the 2/3 child
            // scale (1.5 panel × 0.667 = native) — so the pause panel matches the menu exactly.
            float[] localY = { 1.2866668f, 0.10666667f, -1.12f }; // New Game / Continue / Quit
            const float childScale = 0.6666667f;
            for (int i = 0; i < defs.Length; i++)
            {
                var normal = MenuArt.LoadButton(defs[i].normal);
                var sr = MakeOverlaySprite("Btn_" + defs[i].normal, normal,
                                           new Vector3(0f, localY[i], 0f), Color.white, 102);
                sr.transform.localScale = Vector3.one * childScale;
                var btn = sr.gameObject.AddComponent<SpriteButton>();
                btn.Init(_cam, normal, MenuArt.LoadButton(defs[i].pressed), defs[i].act);
                if (defs[i].normal == "continue")
                    sr.gameObject.AddComponent<Breathe>(); // a game is in progress — nudge "Continue"
            }

            // Drop the whole panel in from above (children ride along, scaled). Unscaled (game paused).
            float drop = _cam != null ? _cam.orthographicSize * 3f : 16f;
            var items = new List<(Transform, Vector3)> { (_pauseOverlay.transform, c) };
            StartCoroutine(MenuArt.DropGroup(items, drop, 0.7f, true));
        }

        SpriteRenderer MakeOverlaySprite(string name, Sprite sprite, Vector3 localPos, Color color, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_pauseOverlay.transform, false);
            go.transform.localPosition = new Vector3(localPos.x, localPos.y, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
            return sr;
        }

        void Update()
        {
            if (_paused) return;       // pause overlay handles its own (SpriteButton) clicks
            if (!_awaitingHuman) return;

            // Click the world-space sprite buttons.
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && _cam != null)
            {
                Vector3 w = _cam.ScreenToWorldPoint(mouse.position.ReadValue());
                var p = new Vector2(w.x, w.y);
                if (_foldEnabled && _foldCol != null && _foldCol.OverlapPoint(p)) OnFoldClicked();
                else if (_callEnabled && _callCol != null && _callCol.OverlapPoint(p)) OnCallClicked();
                else if (_raiseEnabled && _raiseCol != null && _raiseCol.OverlapPoint(p)) OnRaiseClicked();
            }

            // Keyboard shortcuts (also the fallback if the buttons are missing).
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (_foldEnabled && kb.fKey.wasPressedThisFrame) OnFoldClicked();
                else if (_callEnabled && kb.cKey.wasPressedThisFrame) OnCallClicked();
                else if (_raiseEnabled && kb.rKey.wasPressedThisFrame) OnRaiseClicked();
            }
        }

        // Light up the buttons for the human's turn and set the raise slider's range.
        void ConfigureControls(LegalActions legal)
        {
            _foldEnabled = true;   // you can always fold or call/check on your turn
            _callEnabled = true;
            _raiseEnabled = legal.CanRaise;

            if (_foldBtnSr != null) _foldBtnSr.color = BtnOn;
            if (_callBtnSr != null) _callBtnSr.color = BtnOn;
            if (_raiseBtnSr != null) _raiseBtnSr.color = legal.CanRaise ? BtnOn : BtnOff;

            // Button labels reflect the live action.
            SetButtonText(_foldText, "FOLD", true);

            int myChips = _engine.Players[0].Chips;
            string callText = legal.CanCheck ? "CHECK"
                            : legal.ToCall >= myChips ? $"CALL ${legal.ToCall}\n(ALL IN)"
                            : $"CALL ${legal.ToCall}";
            SetButtonText(_callText, callText, true);

            string raiseText = !legal.CanRaise ? "RAISE"
                             : legal.MinRaiseTo >= legal.MaxRaiseTo ? "ALL IN"
                             : legal.CanCheck ? "BET" : "RAISE";
            SetButtonText(_raiseText, raiseText, legal.CanRaise);

            bool sliderUsable = legal.CanRaise && legal.MinRaiseTo < legal.MaxRaiseTo;
            _raiseSlider.gameObject.SetActive(sliderUsable);
            _raiseValueText.gameObject.SetActive(legal.CanRaise);

            if (legal.CanRaise)
            {
                _raiseSlider.minValue = legal.MinRaiseTo;
                _raiseSlider.maxValue = legal.MaxRaiseTo;
                _raiseSlider.value = legal.MinRaiseTo;
                if (sliderUsable) UpdateRaiseValueText();
                else _raiseValueText.text = $"All in  ${legal.MaxRaiseTo}"; // only the all-in raise is left
            }
            else
            {
                _raiseValueText.text = "";
            }
        }

        // Dim the buttons and hide the slider when it isn't the human's turn.
        void HideControls()
        {
            _foldEnabled = _callEnabled = _raiseEnabled = false;
            if (_foldBtnSr != null) _foldBtnSr.color = BtnOff;
            if (_callBtnSr != null) _callBtnSr.color = BtnOff;
            if (_raiseBtnSr != null) _raiseBtnSr.color = BtnOff;
            SetButtonText(_foldText, "FOLD", false);
            SetButtonText(_callText, "CALL", false);
            SetButtonText(_raiseText, "RAISE", false);
            if (_raiseSlider != null) _raiseSlider.gameObject.SetActive(false);
            if (_raiseValueText != null) _raiseValueText.gameObject.SetActive(false);
        }

        static void SetButtonText(Text label, string text, bool enabled)
        {
            if (label == null) return;
            label.text = text;
            label.color = enabled ? BtnOn : BtnOff;
        }

        void OnFoldClicked() { if (_awaitingHuman) { _pending = PlayerAction.Fold(); _actionReady = true; } }
        void OnCallClicked()
        {
            if (!_awaitingHuman) return;
            _pending = _currentLegal.CanCheck ? PlayerAction.Check() : PlayerAction.Call();
            _actionReady = true;
        }
        void OnRaiseClicked()
        {
            if (!_awaitingHuman || !_currentLegal.CanRaise) return;
            _pending = PlayerAction.RaiseTo((int)_raiseSlider.value);
            _actionReady = true;
        }

        // ---------------- game loop ----------------

        IEnumerator RunGame()
        {
            yield return new WaitForSeconds(0.4f);
            while (true)
            {
                if (!_engine.CanStartHand)
                {
                    AudioManager.instance.PlayAudio("GameOver");
                    foreach (var p in _engine.Players) p.Chips = StartingChips;
                    SetStatus("Game over — everyone re-bought");
                    RefreshSeatInfo();
                    yield return new WaitForSeconds(1.6f);
                }
                yield return PlayHand();
                SaveSession();
                yield return new WaitForSeconds(1.5f);
            }
        }

        IEnumerator PlayHand()
        {
            ClearTableVisuals();
            _humanChipsAtHandStart = _engine.Players[0].Chips; // baseline to judge win vs loss
            _engine.StartHand();
            PlaceDealerButton();
            SetStatus("");
            RefreshSeatInfo();
            RefreshPotAndBets();

            yield return DealHoleCards();
            HighlightActing(_engine.ActingPlayer);

            bool revealedRunout = false;

            while (!_engine.HandOver)
            {
                if (_engine.NeedsAction)
                {
                    var p = _engine.ActingPlayer;
                    HighlightActing(p);
                    PlayerAction action;
                    if (p.IsHuman)
                    {
                        yield return GetHumanAction();
                        action = _pending;
                    }
                    else
                    {
                        yield return new WaitForSeconds(0.5f + (float)_rng.NextDouble() * 0.6f);
                        action = _brains[p.Seat].Decide(_engine, p);
                    }

                    _engine.SubmitAction(action);
                    SetSeatAction(p, action);
                    RefreshSeatInfo();
                    RefreshPotAndBets();
                    HighlightActing(_engine.NeedsAction ? _engine.ActingPlayer : null);
                    yield return new WaitForSeconds(0.25f);
                }
                else
                {
                    // Betting round complete → next street.
                    foreach (var s in _seats)
                        if (!_engine.Players[s.Seat].Folded) s.ActionText = "";
                    yield return new WaitForSeconds(0.25f);

                    _engine.AdvanceStreet();

                    // If players are all-in, expose their cards before running the board out.
                    if (!revealedRunout && _engine.ActiveCount > 1 && !_engine.NeedsAction && !_engine.HandOver)
                    {
                        revealedRunout = true;
                        yield return RevealActiveHoles();
                    }

                    yield return RevealBoardForStreet();
                    RefreshPotAndBets();
                    if (_engine.Street != Street.Showdown)
                        SetStatus(StreetName(_engine.Street));
                    yield return new WaitForSeconds(0.5f);
                }
            }

            yield return Showdown();
        }

        IEnumerator GetHumanAction()
        {
            _currentLegal = _engine.GetLegalActions();
            ConfigureControls(_currentLegal);
            _actionReady = false;
            _awaitingHuman = true;
            SetStatus(_currentLegal.CanCheck ? "Your move" : $"Your move — ${_currentLegal.ToCall} to call");
            yield return new WaitUntil(() => _actionReady);
            _awaitingHuman = false;
            HideControls();
            SetStatus("");
        }

        // ---------------- showdown ----------------

        IEnumerator Showdown()
        {
            bool showdown = _engine.ReachedShowdown;
            if (showdown)
                yield return RevealActiveHoles();

            var shares = _engine.FinishHand();

            // Aggregate winnings by player.
            var totals = new Dictionary<SeatPlayer, int>();
            var bestValue = new Dictionary<SeatPlayer, HandValue>();
            foreach (var sh in shares)
            {
                totals.TryGetValue(sh.Player, out int cur);
                totals[sh.Player] = cur + sh.Amount;
                if (!sh.WonByFold) bestValue[sh.Player] = sh.Value;
            }

            SeatPlayer primary = null;
            int bestAmt = -1;
            foreach (var kv in totals)
                if (kv.Value > bestAmt) { bestAmt = kv.Value; primary = kv.Key; }

            if (primary != null)
            {
                string msg = $"{primary.Name} wins ${bestAmt}";
                if (showdown && bestValue.TryGetValue(primary, out var hv))
                    msg += $"\n{hv.Describe()}";
                if (totals.Count > 1) msg += "  (split)";
                SetStatus(msg);

                // Rake the pot to the winner, pop them, dim everyone else, and shake on a big pot.
                var ws = _seats[primary.Seat];
                Vector3 pot = _potChipsRoot != null ? _potChipsRoot.position : _tableCenter;
                FlyChips(pot, ws.Anchor, Mathf.Clamp(2 + bestAmt / 50, 2, 7));
                StartCoroutine(PopAvatar(ws.Avatar));
                foreach (var s in _seats)
                {
                    var asr = s.Avatar != null ? s.Avatar.GetComponent<SpriteRenderer>() : null;
                    if (asr != null) asr.color = s.Seat == primary.Seat ? Color.white : new Color(0.45f, 0.45f, 0.5f, 1f);
                }
                FloatText(ws.Anchor + Vector3.up * 0.6f, $"+${bestAmt}", new Color(0.5f, 1f, 0.55f));
                Punch(Mathf.Clamp(bestAmt / 1500f, 0.12f, 0.5f), 0.4f);
            }

            RefreshSeatInfo();
            RefreshPotAndBets();

            // Celebrate or commiserate based on how the human's stack changed over the hand.
            int delta = _engine.Players[0].Chips - _humanChipsAtHandStart;
            Vector3 fxPos = _seats[0].Anchor;
            if (delta > 0) { PlayWinEffect(fxPos); AudioManager.instance.PlayAudio("Win"); }
            else if (delta < 0)
            {
                PlayLoseEffect(fxPos);
                AudioManager.instance.PlayAudio("Loose");
                FloatText(_seats[0].Anchor + Vector3.up * 0.6f, $"-${-delta}", new Color(1f, 0.45f, 0.45f));
            }

            yield return new WaitForSeconds(2.4f);
        }

        // ---------------- juice (game feel) ----------------

        // Quick camera shake that decays back to the rest position.
        void Punch(float amp, float dur)
        {
            if (_cam == null) return;
            if (_shakeCo != null) StopCoroutine(_shakeCo);
            _shakeCo = StartCoroutine(ShakeRoutine(amp, dur));
        }

        IEnumerator ShakeRoutine(float amp, float dur)
        {
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = 1f - Mathf.Clamp01(t / dur);
                float dx = (UnityEngine.Random.value * 2f - 1f) * amp * k;
                float dy = (UnityEngine.Random.value * 2f - 1f) * amp * k;
                _cam.transform.position = _camBasePos + new Vector3(dx, dy, 0f);
                yield return null;
            }
            _cam.transform.position = _camBasePos;
            _shakeCo = null;
        }

        // Fling a few chip sprites from one spot to another (player→pot, or pot→winner).
        void FlyChips(Vector3 from, Vector3 to, int count)
        {
            count = Mathf.Clamp(count, 1, 7);
            int[] cols = { 1, 3, 2, 5, 4, 6 };
            for (int i = 0; i < count; i++)
            {
                Vector3 j = new Vector3((UnityEngine.Random.value - 0.5f) * 0.5f, (UnityEngine.Random.value - 0.5f) * 0.5f, 0f);
                StartCoroutine(FlyChipRoutine(from + j, to + j, cols[i % cols.Length], i * 0.05f));
            }
        }

        IEnumerator FlyChipRoutine(Vector3 from, Vector3 to, int colorIndex, float startDelay)
        {
            if (startDelay > 0f) yield return new WaitForSeconds(startDelay);
            var go = new GameObject("FlyChip");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _art.Chip(colorIndex);
            sr.sortingOrder = 30; // above the table, below the pause overlay
            go.transform.position = from;
            go.transform.localScale = Vector3.one * 0.45f;
            float dur = 0.35f, t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / dur);
                Vector3 p = Vector3.Lerp(from, to, u);
                p.y += Mathf.Sin(u * Mathf.PI) * 0.6f; // little arc
                go.transform.position = p;
                yield return null;
            }
            Destroy(go);
        }

        // A "+$250" / "-$120" that pops, drifts up and fades over a world spot.
        void FloatText(Vector3 world, string text, Color color)
        {
            if (_canvasRect == null || _cam == null) return;
            StartCoroutine(FloatTextRoutine(world, text, color));
        }

        IEnumerator FloatTextRoutine(Vector3 world, string text, Color color)
        {
            var label = PokerUi.Label(_canvasRect, "Float", Vector2.zero, new Vector2(320, 80),
                                      44, TextAnchor.MiddleCenter, color);
            label.fontStyle = FontStyle.Bold;
            label.text = text;
            var rt = (RectTransform)label.transform;
            Vector3 sp = _cam.WorldToScreenPoint(world);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, sp, null, out var basePos);
            float dur = 1.1f, t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / dur);
                rt.anchoredPosition = basePos + new Vector2(0f, u * 90f);
                rt.localScale = Vector3.one * (1f + 0.2f * Mathf.Sin(u * Mathf.PI));
                var c = color; c.a = 1f - u; label.color = c;
                yield return null;
            }
            Destroy(label.gameObject);
        }

        // A celebratory scale bounce on the winner's avatar.
        IEnumerator PopAvatar(Transform avatar)
        {
            if (avatar == null) yield break;
            Vector3 baseScale = avatar.localScale;
            float dur = 0.4f, t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / dur);
                avatar.localScale = baseScale * (1f + 0.25f * Mathf.Sin(u * Mathf.PI));
                yield return null;
            }
            avatar.localScale = baseScale;
        }

        // ---------------- win / lose effects ----------------

        // Confetti from Confetti_Particle_Effect.unitypackage, fired when you win the pot.
        void PlayWinEffect(Vector3 pos)
        {
            var prefab = winEffectPrefab != null
                ? winEffectPrefab
                : Resources.Load<GameObject>(WinEffectResource);
            if (prefab == null)
            {
                Debug.LogWarning($"[PokerGame] Win effect prefab not found (Resources/{WinEffectResource}).");
                return;
            }

            ClearActiveEffect();
            // Keep the prefab's authored orientation (the confetti sphere bursts in all directions).
            var fx = Instantiate(prefab, pos, prefab.transform.rotation);
            fx.transform.localScale = Vector3.one * Mathf.Max(0.01f, winEffectScale);
            SetEffectSpeed(fx, winEffectSpeed);
            MakeEffectRenderOnTop(fx);
            PlayAllSystems(fx);
            TrackEffect(fx, 11f);
        }

        // A code-built burst (no asset needed) fired when you lose the hand. A loseEffectPrefab,
        // if assigned, is used instead.
        void PlayLoseEffect(Vector3 pos)
        {
            ClearActiveEffect();

            if (loseEffectPrefab != null)
            {
                var fxp = Instantiate(loseEffectPrefab, pos, loseEffectPrefab.transform.rotation);
                fxp.transform.localScale = Vector3.one * Mathf.Max(0.01f, loseEffectScale);
                SetEffectSpeed(fxp, loseEffectSpeed);
                MakeEffectRenderOnTop(fxp);
                PlayAllSystems(fxp);
                TrackEffect(fxp, 4f);
                return;
            }

            var go = new GameObject("LoseBurst");
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * Mathf.Max(0.01f, loseEffectScale);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpeed = Mathf.Max(0f, loseEffectSpeed);
            main.duration = 1.0f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 6.0f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.34f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0.8f;
            main.maxParticles = 300;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            // Muted reds / greys read as "loss".
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.62f, 0.10f, 0.10f), new Color(0.35f, 0.35f, 0.38f));

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)70) });

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.2f;

            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.material = EffectMaterial();
            r.sortingOrder = 60;

            ps.Play();
            TrackEffect(go, 3.5f);
        }

        // Lift every particle system in a spawned effect above the table sprites, and only swap in a
        // URP-safe material where the existing one would render magenta (the imported confetti ships
        // with built-in/legacy particle materials). Prefabs already built for URP keep their materials.
        static void MakeEffectRenderOnTop(GameObject fx)
        {
            foreach (var r in fx.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                r.sortingOrder = 60;
                // Mesh-mode particles using the built-in "Plane" mesh lie in the XZ plane, so they're
                // edge-on to this top-down 2D camera and show as odd slivers. Billboard them so the
                // confetti pieces face the camera. (Quad-mesh effects already face us — left alone.)
                if (r.renderMode == ParticleSystemRenderMode.Mesh && r.mesh != null && r.mesh.name == "Plane")
                    r.renderMode = ParticleSystemRenderMode.Billboard;
                if (NeedsUrpFix(r.sharedMaterial)) r.material = EffectMaterial();
                if (r.trailMaterial != null && NeedsUrpFix(r.trailMaterial)) r.trailMaterial = EffectMaterial();
            }
        }

        // True for built-in / legacy particle materials (magenta under URP), false for URP-ready ones.
        static bool NeedsUrpFix(Material m)
        {
            if (m == null || m.shader == null) return true;
            string n = m.shader.name;
            return n.StartsWith("Particles/")                // built-in particle shaders
                || n.StartsWith("Legacy Shaders/Particles/") // legacy particle shaders
                || n == "Hidden/InternalErrorShader";        // already failed to resolve
        }

        static void PlayAllSystems(GameObject fx)
        {
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
                ps.Play();
        }

        // Scale every particle system in the effect by `multiplier` (1 = the prefab's own speed),
        // so sub-emitters keep their relative timing while the whole effect speeds up or slows down.
        static void SetEffectSpeed(GameObject fx, float multiplier)
        {
            float m = Mathf.Max(0f, multiplier);
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.simulationSpeed *= m;
            }
        }

        static Material EffectMaterial()
        {
            if (_fxMaterial == null)
            {
                var sh = Shader.Find("Sprites/Default"); // unlit, vertex-colored, works under URP 2D
                _fxMaterial = new Material(sh) { name = "PokerEffect (runtime)" };
            }
            return _fxMaterial;
        }

        void TrackEffect(GameObject fx, float life)
        {
            _activeEffect = fx;
            Destroy(fx, life);
        }

        void ClearActiveEffect()
        {
            if (_activeEffect != null) Destroy(_activeEffect);
            _activeEffect = null;
        }

        IEnumerator RevealActiveHoles()
        {
            foreach (var s in _seats)
            {
                var p = _engine.Players[s.Seat];
                if (!p.IsActive || p.IsHuman) continue;
                var parent = s.Avatar != null ? s.Avatar : _tableRoot;
                for (int k = 0; k < 2; k++)
                {
                    var cv = s.Hole[k];
                    if (cv == null || cv.FaceUp) continue;
                    // Grow to full size, spread out, and lift above the table clutter, then flip up.
                    cv.SortingOrder = 8;
                    SetCardScale(cv.transform, parent, s.RevealScale);
                    StartCoroutine(cv.MoveTo(parent.InverseTransformPoint(s.RevealPos[k]), 0.12f));
                    yield return cv.FlipTo(true, 0.12f);
                }
            }
        }

        // ---------------- card visuals ----------------

        // Size a card to `worldScale` on screen, cancelling out any scale on its parent seat node
        // so the avatar's own scale no longer shrinks/grows the cards hanging under it.
        static void SetCardScale(Transform card, Transform parent, float worldScale)
        {
            Vector3 p = parent != null ? parent.lossyScale : Vector3.one;
            float sx = Mathf.Abs(p.x) > 1e-4f ? worldScale / p.x : worldScale;
            float sy = Mathf.Abs(p.y) > 1e-4f ? worldScale / p.y : worldScale;
            card.localScale = new Vector3(sx, sy, 1f);
        }

        IEnumerator DealHoleCards()
        {
            AudioManager.instance.PlayAudio("ShuffleRiffle");
            for (int k = 0; k < 2; k++)
            {
                foreach (var s in _seats)
                {
                    var p = _engine.Players[s.Seat];
                    if (!p.InHand) continue;

                    // Hole cards live under their owner's seat node (Me, P1..P4).
                    var parent = s.Avatar != null ? s.Avatar : _tableRoot;
                    var cv = CardView.Create(parent, _backSprite, s.FaceUp ? 7 : 6);
                    cv.SetFace(_art.Card(p.Hole[k]));
                    SetCardScale(cv.transform, parent, s.HoleScale);
                    cv.transform.position = _tableCenter;       // deal from the centre…
                    if (s.FaceUp) cv.ShowFace(true);
                    s.Hole[k] = cv;

                    // …then slide out to the seat's hole spot (world pos expressed local to the seat).
                    StartCoroutine(cv.MoveTo(parent.InverseTransformPoint(s.HolePos[k]), 0.16f));
                    AudioManager.instance.PlayAudio("TakingCards");
                    yield return new WaitForSeconds(0.06f);
                }
            }
            yield return new WaitForSeconds(0.2f);
        }

        IEnumerator RevealBoardForStreet()
        {
            int have = _board.Count;
            int total = _engine.Board.Count;
            for (int i = have; i < total; i++)
            {
                var cv = CardView.Create(_dealerCardsRoot, _backSprite, 6);
                cv.SetFace(_art.Card(_engine.Board[i]));
                cv.transform.localScale = Vector3.one * 0.74f;
                cv.transform.localPosition = new Vector3((i - 2) * 0.74f, 0f, 0f); // laid out across the DealerCards node
                _board.Add(cv);
                AudioManager.instance.PlayAudio("CardsPlacing");
                yield return cv.FlipTo(true, 0.16f);
                if (i >= 3) Punch(0.12f, 0.22f); // a little kick on the turn and river
                yield return new WaitForSeconds(0.05f);
            }
        }

        void ClearTableVisuals()
        {
            foreach (var s in _seats)
            {
                for (int k = 0; k < 2; k++)
                    if (s.Hole[k] != null) { Destroy(s.Hole[k].gameObject); s.Hole[k] = null; }
                s.ActionText = "";
                if (s.Bet != null) s.Bet.text = "";
            }
            foreach (var cv in _board) if (cv != null) Destroy(cv.gameObject);
            _board.Clear();
            ShowPotChips(0);
            ClearActiveEffect();

            // Reset juice state for the new hand.
            _shownPot = 0f;
            _actingSeat = -1;
            if (_actingGlow != null) _actingGlow.enabled = false;
            foreach (var s in _seats)
            {
                var asr = s.Avatar != null ? s.Avatar.GetComponent<SpriteRenderer>() : null;
                if (asr != null) asr.color = Color.white; // undo the showdown dim
            }
        }

        void PlaceDealerButton()
        {
            if (_dealerDisc == null) return;
            int b = _engine.ButtonIndex;
            var s = _seats[b];
            Vector3 perp = new Vector3(-s.Dir.y, s.Dir.x, 0f);
            _dealerDisc.transform.position = s.Anchor + s.Dir * 0.55f + perp * 0.55f;
            _dealerDisc.enabled = true;
        }

        // ---------------- labels ----------------

        void SetStatus(string text) { if (_statusText != null) _statusText.text = text; }

        string StreetName(Street st) => st switch
        {
            Street.Flop => "Flop",
            Street.Turn => "Turn",
            Street.River => "River",
            _ => ""
        };

        void SetSeatAction(SeatPlayer p, PlayerAction action)
        {
            string verb = action.Type switch
            {
                ActionType.Fold => "Fold",
                ActionType.Check => "Check",
                ActionType.Call => p.IsAllIn ? "All in" : $"Call ${p.StreetCommitted}",
                ActionType.Raise => p.IsAllIn ? $"All in ${p.StreetCommitted}" : $"Raise ${p.StreetCommitted}",
                _ => ""
            };
            _seats[p.Seat].ActionText = verb;

            // Play appropriate audio for the action
            if (p.IsAllIn)
                AudioManager.instance.PlayAudio("All_in");
            else if (action.Type == ActionType.Raise || action.Type == ActionType.Call)
                AudioManager.instance.PlayAudio("ChipsPlacing");
            else if (action.Type == ActionType.Fold)
                AudioManager.instance.PlayAudio("CardsPound");

            // Juice: fling chips toward the pot when betting, and shake hard on an all-in.
            if (action.Type == ActionType.Call || action.Type == ActionType.Raise)
            {
                Vector3 pot = _potChipsRoot != null ? _potChipsRoot.position : _tableCenter;
                FlyChips(_seats[p.Seat].BetWorld, pot, Mathf.Clamp(1 + p.StreetCommitted / 40, 1, 4));
            }
            if (p.IsAllIn) Punch(0.28f, 0.4f);

            // Dim a folded player's cards.
            if (p.Folded)
            {
                var s = _seats[p.Seat];
                for (int k = 0; k < 2; k++)
                    if (s.Hole[k] != null) s.Hole[k].Color = new Color(1f, 1f, 1f, 0.35f);
            }
        }

        void RefreshSeatInfo()
        {
            foreach (var s in _seats)
            {
                var p = _engine.Players[s.Seat];
                string line = $"{p.Name}\n${p.Chips}";
                if (!string.IsNullOrEmpty(s.ActionText)) line += $"\n{s.ActionText}";
                else if (p.Chips <= 0 && !p.InHand) line += "\nOUT";
                s.Info.text = line;
                s.Info.color = p.Folded ? new Color(0.55f, 0.55f, 0.55f) : Color.white;
            }
        }

        void RefreshPotAndBets()
        {
            ShowPotChips(_engine.Pot); // pot text counts up in LateUpdate

            foreach (var s in _seats)
            {
                int bet = _engine.Players[s.Seat].StreetCommitted;
                s.Bet.text = bet > 0 ? $"${bet}" : "";
            }
        }

        void HighlightActing(SeatPlayer acting)
        {
            _actingSeat = acting != null ? acting.Seat : -1;
            if (_actingGlow != null)
            {
                if (acting != null)
                {
                    _actingGlow.transform.position = _seats[acting.Seat].Anchor;
                    _actingGlow.enabled = true;
                }
                else _actingGlow.enabled = false;
            }

            foreach (var s in _seats)
            {
                var p = _engine.Players[s.Seat];
                if (acting != null && p == acting)
                    s.Info.color = new Color(1f, 0.85f, 0.2f);
                else
                    s.Info.color = p.Folded ? new Color(0.55f, 0.55f, 0.55f) : Color.white;
            }
        }
    }
}
