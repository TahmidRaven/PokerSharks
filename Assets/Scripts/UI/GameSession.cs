using UnityEngine;

namespace Poker
{
    /// <summary>
    /// Cross-scene glue for the menu. Holds whether the next load of the game scene should resume
    /// the previous session, and persists a lightweight snapshot (each seat's chips + the dealer
    /// button) so "Continue" is meaningful even after the app is restarted.
    /// </summary>
    public static class GameSession
    {
        // Set by the menu just before it loads the game scene; read once by PokerGame on start.
        public static bool ResumeRequested;

        const string KeyHas    = "poker_has_save";
        const string KeyChips  = "poker_chips";   // CSV of per-seat chip counts
        const string KeyButton = "poker_button";  // dealer-button seat index

        public static bool HasSavedGame => PlayerPrefs.GetInt(KeyHas, 0) == 1;

        // New Game: forget any saved session and start fresh.
        public static void StartNew()
        {
            ResumeRequested = false;
            PlayerPrefs.DeleteKey(KeyHas);
            PlayerPrefs.DeleteKey(KeyChips);
            PlayerPrefs.DeleteKey(KeyButton);
            PlayerPrefs.Save();
        }

        // Continue: ask the next game load to restore the saved stacks (if any).
        public static void RequestResume() => ResumeRequested = true;

        public static void Save(int[] chips, int buttonIndex)
        {
            PlayerPrefs.SetString(KeyChips, string.Join(",", chips));
            PlayerPrefs.SetInt(KeyButton, buttonIndex);
            PlayerPrefs.SetInt(KeyHas, 1);
            PlayerPrefs.Save();
        }

        public static bool TryLoad(out int[] chips, out int buttonIndex)
        {
            chips = null;
            buttonIndex = 0;
            if (!HasSavedGame) return false;

            string csv = PlayerPrefs.GetString(KeyChips, "");
            if (string.IsNullOrEmpty(csv)) return false;

            var parts = csv.Split(',');
            chips = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                if (!int.TryParse(parts[i], out chips[i])) return false;

            buttonIndex = PlayerPrefs.GetInt(KeyButton, 0);
            return true;
        }
    }
}
