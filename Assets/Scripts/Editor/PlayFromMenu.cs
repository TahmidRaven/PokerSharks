#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Poker.EditorTools
{
    /// <summary>
    /// Makes pressing Play in the Editor always boot from the Menu scene (matching a real build,
    /// where Menu is build index 0), no matter which scene you currently have open.
    /// Remove this file (or clear "Play Mode Start Scene" at the bottom of the Build Settings)
    /// if you'd rather Play run the scene you have open.
    /// </summary>
    [InitializeOnLoad]
    static class PlayFromMenu
    {
        const string MenuScenePath = "Assets/Scenes/Menu.unity";

        static PlayFromMenu()
        {
            var menu = AssetDatabase.LoadAssetAtPath<SceneAsset>(MenuScenePath);
            if (menu != null)
                EditorSceneManager.playModeStartScene = menu;
        }
    }
}
#endif
