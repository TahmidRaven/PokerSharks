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

        static readonly string[] BotNames = { "You", "Jett", "Mira", "Cole", "Vega" };

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
            // Priority: Inspector reference → find-by-name in the scene → a generated fallback.
            var t = new Transform[NumPlayers];
            t[0] = humanSeat != null ? humanSeat : FindOrCreate("Me", DefaultAnchors[0]);
            for (int i = 1; i < NumPlayers; i++)
            {
                Transform assigned = (botSeats != null && i - 1 < botSeats.Length) ? botSeats[i - 1] : null;
                t[i] = assigned != null ? assigned : FindOrCreate("P" + i, DefaultAnchors[i]);
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
                s.HoleScale = 1.0f;
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
                s.HoleScale = 0.5f;
                Vector3 hc = s.Anchor + s.Dir * 0.85f;
                s.HolePos[0] = hc - perp * 0.16f;
                s.HolePos[1] = hc + perp * 0.16f;
                s.InfoWorld = s.Anchor - s.Dir * 1.2f;
                s.BetWorld = s.Anchor + s.Dir * 1.7f;
                // When shown at showdown, grow to full size and spread apart so both read clearly.
                s.RevealScale = 1.0f;
                s.RevealPos[0] = hc - perp * 0.5f;
                s.RevealPos[1] = hc + perp * 0.5f;
            }
            return s;
        }

        static Transform FindOrCreate(string name, Vector3 fallback)
        {
            var go = GameObject.Find(name);
            if (go != null) return go.transform;
            var created = new GameObject("Seat_" + name);
            created.transform.position = fallback;
            return created.transform;
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

        void Update()
        {
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
                    foreach (var p in _engine.Players) p.Chips = StartingChips;
                    SetStatus("New game — everyone re-bought");
                    RefreshSeatInfo();
                    yield return new WaitForSeconds(1.6f);
                }
                yield return PlayHand();
                yield return new WaitForSeconds(1.5f);
            }
        }

        IEnumerator PlayHand()
        {
            ClearTableVisuals();
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
            }

            RefreshSeatInfo();
            RefreshPotAndBets();
            yield return new WaitForSeconds(2.4f);
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
                    cv.transform.localScale = Vector3.one * s.RevealScale;
                    StartCoroutine(cv.MoveTo(parent.InverseTransformPoint(s.RevealPos[k]), 0.12f));
                    yield return cv.FlipTo(true, 0.12f);
                }
            }
        }

        // ---------------- card visuals ----------------

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
                    cv.transform.localScale = Vector3.one * s.HoleScale;
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
            int pot = _engine.Pot;
            if (_potText != null) _potText.text = pot > 0 ? $"POT  ${pot}" : "";
            ShowPotChips(pot);

            foreach (var s in _seats)
            {
                int bet = _engine.Players[s.Seat].StreetCommitted;
                s.Bet.text = bet > 0 ? $"${bet}" : "";
            }
        }

        void HighlightActing(SeatPlayer acting)
        {
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
