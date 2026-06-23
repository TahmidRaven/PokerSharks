using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(AudioSource))]
public class AudioContent : MonoBehaviour
{
    [SerializeField]
    public string audioName = "";

    [SerializeField]
    public AudioClip audioClip;

    [SerializeField]
    public bool loop = false;

    [Range(0f, 1f)]
    [SerializeField]
    public float volume = 1.0f;

    [SerializeField]
    public bool playOnAwake = false;

    [SerializeField]
    public UnityEvent<string> onPlayingStart = new UnityEvent<string>();

    [SerializeField]
    public UnityEvent<string> onPlayingEnd = new UnityEvent<string>();

    private AudioSource audioSource;

    private void OnEnable()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }
        }

        audioSource.clip = audioClip;
        audioSource.loop = loop;
        audioSource.volume = volume;
        audioSource.playOnAwake = playOnAwake;

        if (playOnAwake)
        {
            Play();
        }
    }

    public void Play()
    {
        if (audioSource && audioClip != null)
        {
            audioSource.clip = audioClip;
            audioSource.loop = loop;
            audioSource.volume = volume;
            audioSource.Play();
            onPlayingStart?.Invoke(audioName);

            if (!loop)
            {
                Invoke(nameof(OnAudioEnd), audioClip.length);
            }
        }
    }

    public void Stop()
    {
        if (audioSource)
        {
            audioSource.Stop();
        }
    }

    private void OnAudioEnd()
    {
        onPlayingEnd?.Invoke(audioName);
    }
}
