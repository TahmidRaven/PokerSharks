# Audio Assets Reference

## Available Audio Clips in AudioAssets/

| File | Suggested Use |
|------|---------------|
| BGM_Kokushi_Musou_loop.mp3 | Background music (looping) |
| placingPokerChips.mp3 | SFX: Chips placed on table |
| TakingCards.mp3 | SFX: Drawing/taking cards |
| All_in_pokersPush.mp3 | SFX: All-in push |
| ShuffleRiffle.mp3 | SFX: Card shuffle |
| PoundingCardsOnTheTable.mp3 | SFX: Cards pounded/dealt |
| PokerChipsAlot.mp3 | SFX: Multiple chips handled |
| PlacingPlayingCards.mp3 | SFX: Cards placed |
| PokerRoomEnvironment.mp3 | Ambient: Room environment |

## Setup Instructions

1. **Create AudioManager Node**
   - In your game.unity scene, create an empty GameObject called "AudioManager"
   - Add the AudioManager.cs component to it

2. **Create Audio Nodes Under AudioManager**
   - For each audio clip, create a child GameObject under AudioManager
   - Name it appropriately (e.g., "BGM", "PlaceChipsSFX", etc.)
   - Add AudioContent.cs component to each

3. **Configure AudioContent**
   - Set the audio name (e.g., "place_chips", "shuffle_card")
   - Assign the AudioClip from AudioAssets/
   - Configure volume, loop, and playOnAwake as needed

4. **Play Audio in Code**
   ```csharp
   // Play audio by name
   AudioManager.instance.PlayAudio("place_chips");
   
   // Stop audio
   AudioManager.instance.StopAudio("place_chips");
   
   // Stop all audio
   AudioManager.instance.StopAllAudio();
   
   // Get audio reference
   AudioContent audio = AudioManager.instance.GetAudio("place_chips");
   ```

## Naming Convention for Audio Names

Use snake_case for audio names:
- `bgm_main` for background music
- `sfx_place_chips` for sound effects
- `sfx_shuffle_cards` for card shuffles
- `ambient_room` for ambience
