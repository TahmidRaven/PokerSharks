# Top-Down Texas Hold'em

A complete single-player Texas Hold'em game: **you vs 4 AI bots**, built entirely from
code at runtime on top of the SBS 2D Poker Pack (Top-Down) art.

## How to run

**Recommended (wire it to your scene):**
1. Select your **GameManager** node → *Add Component* → **Poker Game**.
2. Assign **Human Seat** = your `Me` node, and **Bot Seats** (size 4) = `P1, P2, P3, P4`.
3. Uncheck **Build Table Felt** if your `BG` sprite already shows a table.
4. Press **Play**.

Seat layout, card spots and labels are derived from wherever you placed those nodes; the pot and
community cards sit at the average of the five seats (or a manual **Table Center**). The deck,
cards, action UI, dealer button and pot/seat labels are still created at runtime — those aren't
wired by hand.

**Zero-config fallback:** if you skip step 1, a `[RuntimeInitializeOnLoadMethod]` auto-spawns the
controller in the `game` scene. With no Inspector references it finds the nodes by name
(`Me`, `P1`–`P4`) and otherwise falls back to built-in default seat positions.

## Controls
- **FOLD / CHECK·CALL / RAISE** buttons appear (bottom-right) only on your turn.
- Drag the **slider** to choose a raise amount, then press **RAISE / BET**.
- Hands play out automatically; busted players re-buy so it never dead-ends.

## Code map
| File | Responsibility |
|------|----------------|
| `Core/Cards.cs` | `Suit`, `Rank`, `Card` (+ sprite-name mapping), `Deck` (shuffle/deal) |
| `Core/HandEvaluator.cs` | Best 5-of-7 evaluation → comparable `HandValue` + describe |
| `Game/PokerEngine.cs` | Betting state machine: blinds, streets, raises, all-in, **side pots**, showdown |
| `Game/BotBrain.cs` | Monte-Carlo equity estimate + personality-weighted decisions |
| `View/PokerArt.cs` | Loads sliced sprites from `Resources/Poker` |
| `View/CardView.cs` | One card: face/back, flip & slide animations |
| `View/PokerUi.cs` | Runtime UI factory + procedural felt/disc textures |
| `View/PokerGame.cs` | Bootstrap, table/seat layout, UI, and the hand-by-hand game loop |



## Art / Resources
Six sprite sheets were **copied** (originals untouched) into `Assets/Resources/Poker/` with
fresh GUIDs so they can be loaded at runtime via `Resources.LoadAll<Sprite>`. If you re-slice
or replace the originals, re-copy them here.

## Tuning
Edit the constants at the top of `PokerGame.cs`: `NumPlayers`, `StartingChips`,
`SmallBlind`, `BigBlind`, `BotNames`, and the `anchors[]` seat positions in `BuildSeats()`.

## Known limitations (good next steps)
- Seat labels are placed in world space; on a very narrow (≈1:1) Game-view aspect the side
  seats can drift toward the edges. Designed around ~16:9.
- No sound, no betting-history log, no save. Heads-up (2-player) blind order is simplified.
- The existing `BG` object is left in place behind the felt; delete/hide it if you prefer a
  plain background.


# What's on Raven's Mind: 


- the core gameloop doens't really care for the player which is 'me' if it wins or loses there's no huge impact upon it. 
  For which the gamification seems to be a bit dull at the moment. For now, we have the core loop where the game continues until any of the players win (even if the the player is AI/Automated). This can drag the game for quite sometime; which makes the player sit for a long while until that pot has been played properly. At this moment I've no idea what to do w/ the game juice or the game play mechanics to make it more fun and enjoyable for players. Eventually, however, not now I will move the game to online letting other players join. AI's can also join if slots are not met. 

- This game also will be a participating in a gamejam for JuniperDev, a gameDev youtuber. The game Jam know as "VERY SERIOUS GAMEJAM".   

  The theme is spin which already matches as our players take turn in a round table. If i'm wrong than we might need to do something about it so not only we can participate properly also have a shot at winning. 

