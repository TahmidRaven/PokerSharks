using UnityEngine;
using System.Collections.Generic;

public class AudioManager : MonoBehaviour
{
    public static AudioManager instance { get; private set; }

    [SerializeField]
    private List<AudioContent> audioNodes = new List<AudioContent>();

    private Dictionary<string, AudioContent> audioMap = new Dictionary<string, AudioContent>();

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        InitializeAudioMap();
    }

    private void InitializeAudioMap()
    {
        audioMap.Clear();

        // Find all AudioContent components in children
        AudioContent[] allAudio = GetComponentsInChildren<AudioContent>();
        foreach (AudioContent audio in allAudio)
        {
            if (!string.IsNullOrEmpty(audio.audioName))
            {
                if (audioMap.ContainsKey(audio.audioName))
                {
                    Debug.LogWarning($"Duplicate audio name: {audio.audioName}");
                }
                else
                {
                    audioMap[audio.audioName] = audio;
                    audioNodes.Add(audio);
                }
            }
        }

        Debug.Log($"AudioManager initialized with {audioMap.Count} audio clips");
    }

    public void PlayAudio(string audioName)
    {
        if (audioMap.TryGetValue(audioName, out AudioContent audio))
        {
            audio.Play();
        }
        else
        {
            Debug.LogWarning($"Audio '{audioName}' not found in AudioManager");
        }
    }

    public void StopAudio(string audioName)
    {
        if (audioMap.TryGetValue(audioName, out AudioContent audio))
        {
            audio.Stop();
        }
    }

    public void StopAllAudio()
    {
        foreach (AudioContent audio in audioNodes)
        {
            audio.Stop();
        }
    }

    public AudioContent GetAudio(string audioName)
    {
        audioMap.TryGetValue(audioName, out AudioContent audio);
        return audio;
    }

    public List<AudioContent> GetAllAudio()
    {
        return new List<AudioContent>(audioNodes);
    }
}
