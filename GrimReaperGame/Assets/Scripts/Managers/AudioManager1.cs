using UnityEngine;
using FMODUnity;
using FMOD.Studio;
using System.Dynamic;
using System.Collections.Generic;


public class AudioManager : MonoBehaviour
{
    private List<EventInstance> events = new List<EventInstance>();

    public static AudioManager instance { get; private set; }

    [Header("Volume")]
    [Range(0f, 1f)] public float SFXVolume = 1f;
    [Range(0f, 1f)] public float AmbVolume = 1f;
    [Range(0f, 1f)] public float MusicVolume = 0.8f;

    private EventInstance ambienceEventInstance;
    private EventInstance musicEventInstance;
    [SerializeField] private EventReference ambienceReference;
    [SerializeField] private EventReference musicReference;


    private Bus musicBus;
    private Bus sfxBus;
    private Bus ambBus;

    private void Awake()
    {
        if(instance != null && instance != this) {

            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
        musicBus = RuntimeManager.GetBus("bus:/MusicBus");
        sfxBus = RuntimeManager.GetBus("bus:/SFXBus");
        ambBus = RuntimeManager.GetBus("bus:/AmbienceBus");

        InitializeAmbience(ambienceReference, 0f);
    }

    private void Update()
    {
        musicBus.setVolume(MusicVolume);
        sfxBus.setVolume(SFXVolume);
        ambBus.setVolume(AmbVolume);
    }

    public void PauseMusic(bool pause)
    {
        if (musicEventInstance.isValid())
            musicEventInstance.setPaused(pause);
    }

    public void InitializeAmbience(EventReference ambienceEvent, float value)
    {
        if (!ambienceEventInstance.isValid())
        {
            ambienceEventInstance = CreateInstance(ambienceEvent);
            ambienceEventInstance.start();
        }
       // ambienceEventInstance.setParameterByName("Room", value);
    }

    public void InitializeSpecificMusic(EventReference musicEvent, params (string name, float value)[] parameters)
    {
        if (!musicEventInstance.isValid())
            musicEventInstance = CreateInstance(musicEvent);

        foreach (var (name, value) in parameters)
            musicEventInstance.setParameterByName(name, value);

        PLAYBACK_STATE ps;
        musicEventInstance.getPlaybackState(out ps);
        if (ps != PLAYBACK_STATE.PLAYING) musicEventInstance.start();
    }

    public void InitializeMusic()
    {
        if (!musicEventInstance.isValid())
            musicEventInstance = CreateInstance(musicReference);

        PLAYBACK_STATE ps;
        musicEventInstance.getPlaybackState(out ps);
        if (ps != PLAYBACK_STATE.PLAYING) musicEventInstance.start();
    }

    public void StopMusic()
    {
        if (!musicEventInstance.isValid()) return;
        musicEventInstance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
        musicEventInstance.release();
        musicEventInstance.clearHandle(); // or: musicEventInstance = default;
    }

    public void musicIntensityChange (string value)
    {
        musicEventInstance.setParameterByNameWithLabel("Mood", value);
    }


    public void PlayOneShot(EventReference sound, Vector3 worldPos)
    {
        RuntimeManager.PlayOneShot(sound, worldPos);
    }

    public void PlayOneShotWithParameters(EventReference fmodEvent, Vector3 position, params (string name, float value)[] parameters)
    {
        FMOD.Studio.EventInstance instance = FMODUnity.RuntimeManager.CreateInstance(fmodEvent);

        foreach (var (name, value) in parameters)
        {
            instance.setParameterByName(name, value);
        }

        instance.set3DAttributes(position.To3DAttributes());
        instance.start();
        instance.release();
    }


    public EventInstance CreateInstance (EventReference eventReference)
    {
        EventInstance eventInstance = RuntimeManager.CreateInstance(eventReference);
        events.Add(eventInstance);
        
        return eventInstance;
    }


    private void Cleanup()
    {
        foreach (EventInstance eventInstance in events)
        {
            eventInstance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            eventInstance.release();
        }
    }

    private void OnDestroy()
    {
        Cleanup();
    }
}
