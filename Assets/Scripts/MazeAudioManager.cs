using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class MazeAudioManager : MonoBehaviour
{
    [Header("Script References")]
    [SerializeField] private MazeGameSystem gameSystem;
    [SerializeField] private PlayerToken playerToken;
    [SerializeField] private Transform playerTarget;
    [SerializeField] private bool autoFindReferences = true;

    [Header("Assigned Audio Clips")]
    [SerializeField] private AudioClip startSound;
    [SerializeField] private AudioClip wallBounceSound;
    [SerializeField] private AudioClip finishSound;
    [SerializeField] private AudioClip restartSound;
    [SerializeField] private AudioClip backgroundMusic;

    [Header("Procedural Fallback Sounds")]
    [SerializeField] private bool generateFallbackSounds = true;
    [SerializeField, Range(8000, 48000)] private int proceduralSampleRate = 44100;
    [SerializeField, Range(0f, 1f)] private float proceduralToneVolume = 0.8f;
    [SerializeField, Range(0f, 1f)] private float proceduralMusicVolumeScale = 0.45f;

    [Header("Audio Sources")]
    [SerializeField] private AudioSource effectsSource;
    [SerializeField] private AudioSource bounceSource;
    [SerializeField] private AudioSource musicSource;
    [SerializeField] private bool createSourcesIfMissing = true;

    [Header("Volume Controls")]
    [SerializeField, Range(0f, 1f)] private float masterVolume = 1f;
    [SerializeField, Range(0f, 1f)] private float effectsVolume = 0.85f;
    [SerializeField, Range(0f, 1f)] private float bounceVolume = 0.75f;
    [SerializeField, Range(0f, 1f)] private float musicVolume = 0.35f;

    [Header("Mute Options")]
    [SerializeField] private bool muteAll = false;
    [SerializeField] private bool muteEffects = false;
    [SerializeField] private bool muteBounce = false;
    [SerializeField] private bool muteMusic = false;

    [Header("Background Music")]
    [SerializeField] private bool playMusicOnStart = true;
    [SerializeField] private bool loopMusic = true;
    [SerializeField] private bool stopMusicOnFinish = false;
    [SerializeField, Min(0f)] private float musicFadeInSeconds = 0.75f;
    [SerializeField, Min(0f)] private float musicFadeOutSeconds = 0.5f;

    [Header("Wall Bounce Detection")]
    [SerializeField] private bool enableWallBounceSound = true;
    [SerializeField, Min(0f)] private float minimumBounceRelativeVelocity = 0.8f;
    [SerializeField, Min(0f)] private float bounceSoundCooldown = 0.12f;
    [SerializeField] private string wallObjectNameContains = "Wall";
    [SerializeField] private string wallTag = "";
    [SerializeField] private bool attachCollisionRelayToPlayer = true;

    [Header("Debug")]
    [SerializeField] private bool printStatusMessages = false;
    [SerializeField] private bool printWarnings = false;

    [SerializeField, HideInInspector] private string status = "not ready";

    private AudioClip generatedStartSound;
    private AudioClip generatedWallBounceSound;
    private AudioClip generatedFinishSound;
    private AudioClip generatedRestartSound;
    private AudioClip generatedBackgroundMusic;

    private MazeGameSystem.MazeGameState lastGameState;
    private MazeAudioCollisionRelay collisionRelay;
    private float lastBounceSoundTime = -999f;
    private Coroutine musicFadeCoroutine;

    public string Status => status;
    public bool IsMuted => muteAll;

    private void Awake()
    {
        ResolveReferences();

        if (createSourcesIfMissing)
        {
            CreateMissingAudioSources();
        }

        if (generateFallbackSounds)
        {
            GenerateFallbackAudioClips();
        }

        ConfigureAudioSources();
        AttachCollisionRelayIfPossible();

        status = "audio ready";
    }

    private void Start()
    {
        ResolveReferences();

        if (gameSystem != null)
        {
            lastGameState = gameSystem.CurrentState;
        }

        if (playMusicOnStart)
        {
            PlayBackgroundMusic();
        }

        if (printStatusMessages)
        {
            Debug.Log("audio ready");
        }
    }

    private void Update()
    {
        ResolveReferences();
        ApplySourceVolumes();

        if (attachCollisionRelayToPlayer)
        {
            AttachCollisionRelayIfPossible();
        }

        ObserveGameStateChanges();
    }

    private void OnValidate()
    {
        masterVolume = Mathf.Clamp01(masterVolume);
        effectsVolume = Mathf.Clamp01(effectsVolume);
        bounceVolume = Mathf.Clamp01(bounceVolume);
        musicVolume = Mathf.Clamp01(musicVolume);
        proceduralSampleRate = Mathf.Clamp(proceduralSampleRate, 8000, 48000);
        proceduralToneVolume = Mathf.Clamp01(proceduralToneVolume);
        proceduralMusicVolumeScale = Mathf.Clamp01(proceduralMusicVolumeScale);
        minimumBounceRelativeVelocity = Mathf.Max(0f, minimumBounceRelativeVelocity);
        bounceSoundCooldown = Mathf.Max(0f, bounceSoundCooldown);
        musicFadeInSeconds = Mathf.Max(0f, musicFadeInSeconds);
        musicFadeOutSeconds = Mathf.Max(0f, musicFadeOutSeconds);
    }

    public void PlayStartSound()
    {
        PlayOneShotSafe(effectsSource, GetStartClip(), effectsVolume, muteEffects);
    }

    public void PlayWallBounceSound()
    {
        if (!enableWallBounceSound)
        {
            return;
        }

        if (Time.time - lastBounceSoundTime < bounceSoundCooldown)
        {
            return;
        }

        lastBounceSoundTime = Time.time;
        PlayOneShotSafe(bounceSource != null ? bounceSource : effectsSource, GetBounceClip(), bounceVolume, muteBounce);
    }

    public void PlayFinishSound()
    {
        PlayOneShotSafe(effectsSource, GetFinishClip(), effectsVolume, muteEffects);
    }

    public void PlayRestartSound()
    {
        PlayOneShotSafe(effectsSource, GetRestartClip(), effectsVolume, muteEffects);
    }

    public void PlayBackgroundMusic()
    {
        AudioClip musicClip = GetMusicClip();

        if (musicSource == null || musicClip == null || muteAll || muteMusic)
        {
            return;
        }

        musicSource.clip = musicClip;
        musicSource.loop = loopMusic;

        if (!musicSource.isPlaying)
        {
            musicSource.volume = 0f;
            musicSource.Play();
        }

        StartMusicFade(GetMusicEffectiveVolume(), musicFadeInSeconds);
    }

    public void StopBackgroundMusic()
    {
        if (musicSource == null)
        {
            return;
        }

        if (musicFadeOutSeconds <= 0f)
        {
            musicSource.Stop();
            return;
        }

        StartMusicFade(0f, musicFadeOutSeconds, true);
    }

    public void SetMuted(bool muted)
    {
        muteAll = muted;
        ApplySourceVolumes();
    }

    public void SetEffectsMuted(bool muted)
    {
        muteEffects = muted;
        ApplySourceVolumes();
    }

    public void SetBounceMuted(bool muted)
    {
        muteBounce = muted;
        ApplySourceVolumes();
    }

    public void SetMusicMuted(bool muted)
    {
        muteMusic = muted;
        ApplySourceVolumes();

        if (muteMusic || muteAll)
        {
            StopBackgroundMusic();
        }
        else if (playMusicOnStart)
        {
            PlayBackgroundMusic();
        }
    }

    private void ObserveGameStateChanges()
    {
        if (gameSystem == null)
        {
            return;
        }

        MazeGameSystem.MazeGameState currentState = gameSystem.CurrentState;

        if (currentState == lastGameState)
        {
            return;
        }

        if (currentState == MazeGameSystem.MazeGameState.Playing)
        {
            PlayStartSound();

            if (playMusicOnStart && musicSource != null && !musicSource.isPlaying)
            {
                PlayBackgroundMusic();
            }
        }
        else if (currentState == MazeGameSystem.MazeGameState.Finished)
        {
            PlayFinishSound();

            if (stopMusicOnFinish)
            {
                StopBackgroundMusic();
            }
        }
        else if (currentState == MazeGameSystem.MazeGameState.Restarting)
        {
            PlayRestartSound();
        }

        lastGameState = currentState;
    }

    public void HandlePlayerCollision(Collision collision)
    {
        if (!enableWallBounceSound || collision == null)
        {
            return;
        }

        if (collision.relativeVelocity.magnitude < minimumBounceRelativeVelocity)
        {
            return;
        }

        if (!IsWallCollision(collision.collider))
        {
            return;
        }

        PlayWallBounceSound();
    }

    private bool IsWallCollision(Collider other)
    {
        if (other == null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(wallTag) && other.CompareTag(wallTag))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(wallObjectNameContains) &&
            other.gameObject.name.IndexOf(wallObjectNameContains, System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        Transform current = other.transform.parent;
        while (current != null)
        {
            if (!string.IsNullOrWhiteSpace(wallObjectNameContains) &&
                current.name.IndexOf(wallObjectNameContains, System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private void ResolveReferences()
    {
        if (!autoFindReferences)
        {
            return;
        }

        if (gameSystem == null)
        {
            gameSystem = FindFirstObjectByType<MazeGameSystem>();
        }

        if (playerToken == null)
        {
            playerToken = FindFirstObjectByType<PlayerToken>();
        }

        if (playerTarget == null && playerToken != null && playerToken.PlayerTransform != null)
        {
            playerTarget = playerToken.PlayerTransform;
        }

        if (playerTarget == null)
        {
            GameObject playerObject = GameObject.Find("Player Token");
            if (playerObject != null)
            {
                playerTarget = playerObject.transform;
            }
        }

        if (playerTarget == null)
        {
            GameObject playerObject = GameObject.Find("PlayerToken");
            if (playerObject != null)
            {
                playerTarget = playerObject.transform;
            }
        }
    }

    private void CreateMissingAudioSources()
    {
        if (effectsSource == null)
        {
            effectsSource = CreateAudioSource("Maze Effects Audio Source", false);
        }

        if (bounceSource == null)
        {
            bounceSource = CreateAudioSource("Maze Bounce Audio Source", false);
        }

        if (musicSource == null)
        {
            musicSource = CreateAudioSource("Maze Music Audio Source", true);
        }
    }

    private AudioSource CreateAudioSource(string objectName, bool isMusic)
    {
        GameObject sourceObject = new GameObject(objectName);
        sourceObject.transform.SetParent(transform, false);

        AudioSource source = sourceObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = isMusic;
        source.spatialBlend = 0f;
        source.dopplerLevel = 0f;
        source.rolloffMode = AudioRolloffMode.Linear;

        return source;
    }

    private void ConfigureAudioSources()
    {
        if (effectsSource != null)
        {
            effectsSource.playOnAwake = false;
            effectsSource.loop = false;
            effectsSource.spatialBlend = 0f;
        }

        if (bounceSource != null)
        {
            bounceSource.playOnAwake = false;
            bounceSource.loop = false;
            bounceSource.spatialBlend = 0f;
        }

        if (musicSource != null)
        {
            musicSource.playOnAwake = false;
            musicSource.loop = loopMusic;
            musicSource.spatialBlend = 0f;
        }

        ApplySourceVolumes();
    }

    private void ApplySourceVolumes()
    {
        if (effectsSource != null)
        {
            effectsSource.volume = GetEffectsEffectiveVolume();
            effectsSource.mute = muteAll || muteEffects;
        }

        if (bounceSource != null)
        {
            bounceSource.volume = GetBounceEffectiveVolume();
            bounceSource.mute = muteAll || muteBounce;
        }

        if (musicSource != null)
        {
            musicSource.loop = loopMusic;
            musicSource.mute = muteAll || muteMusic;

            if (musicFadeCoroutine == null)
            {
                musicSource.volume = GetMusicEffectiveVolume();
            }
        }
    }

    private float GetEffectsEffectiveVolume()
    {
        return muteAll || muteEffects ? 0f : masterVolume * effectsVolume;
    }

    private float GetBounceEffectiveVolume()
    {
        return muteAll || muteBounce ? 0f : masterVolume * bounceVolume;
    }

    private float GetMusicEffectiveVolume()
    {
        return muteAll || muteMusic ? 0f : masterVolume * musicVolume;
    }

    private void PlayOneShotSafe(AudioSource source, AudioClip clip, float volume, bool muted)
    {
        if (source == null || clip == null || muteAll || muted)
        {
            return;
        }

        source.PlayOneShot(clip, Mathf.Clamp01(masterVolume * volume));
    }

    private AudioClip GetStartClip()
    {
        return startSound != null ? startSound : generatedStartSound;
    }

    private AudioClip GetBounceClip()
    {
        return wallBounceSound != null ? wallBounceSound : generatedWallBounceSound;
    }

    private AudioClip GetFinishClip()
    {
        return finishSound != null ? finishSound : generatedFinishSound;
    }

    private AudioClip GetRestartClip()
    {
        return restartSound != null ? restartSound : generatedRestartSound;
    }

    private AudioClip GetMusicClip()
    {
        return backgroundMusic != null ? backgroundMusic : generatedBackgroundMusic;
    }

    private void StartMusicFade(float targetVolume, float duration, bool stopAfterFade = false)
    {
        if (musicSource == null)
        {
            return;
        }

        if (musicFadeCoroutine != null)
        {
            StopCoroutine(musicFadeCoroutine);
        }

        musicFadeCoroutine = StartCoroutine(FadeMusic(targetVolume, duration, stopAfterFade));
    }

    private IEnumerator FadeMusic(float targetVolume, float duration, bool stopAfterFade)
    {
        float startVolume = musicSource.volume;

        if (duration <= 0f)
        {
            musicSource.volume = targetVolume;

            if (stopAfterFade)
            {
                musicSource.Stop();
            }

            musicFadeCoroutine = null;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            musicSource.volume = Mathf.Lerp(startVolume, targetVolume, t);
            yield return null;
        }

        musicSource.volume = targetVolume;

        if (stopAfterFade)
        {
            musicSource.Stop();
        }

        musicFadeCoroutine = null;
    }

    private void AttachCollisionRelayIfPossible()
    {
        if (!attachCollisionRelayToPlayer || playerTarget == null)
        {
            return;
        }

        if (collisionRelay != null && collisionRelay.transform == playerTarget)
        {
            return;
        }

        collisionRelay = playerTarget.GetComponent<MazeAudioCollisionRelay>();
        if (collisionRelay == null)
        {
            collisionRelay = playerTarget.gameObject.AddComponent<MazeAudioCollisionRelay>();
        }

        collisionRelay.Initialize(this);
    }

    private void GenerateFallbackAudioClips()
    {
        generatedStartSound = GenerateStartChime();
        generatedWallBounceSound = GenerateElasticBounce();
        generatedFinishSound = GenerateFinishArpeggio();
        generatedRestartSound = GenerateRestartBlip();
        generatedBackgroundMusic = GenerateAmbientLoop();
    }

    private AudioClip GenerateStartChime()
    {
        float duration = 0.42f;
        int samples = Mathf.CeilToInt(duration * proceduralSampleRate);
        float[] data = new float[samples];

        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)proceduralSampleRate;
            float normalized = t / duration;
            float frequency = Mathf.Lerp(440f, 880f, normalized);
            float envelope = AttackDecayEnvelope(normalized, 0.08f, 0.88f);
            float tone = Mathf.Sin(2f * Mathf.PI * frequency * t);
            float shimmer = Mathf.Sin(2f * Mathf.PI * frequency * 2f * t) * 0.25f;
            data[i] = (tone + shimmer) * envelope * proceduralToneVolume * 0.55f;
        }

        return CreateClip("Generated Start Chime", data);
    }

    private AudioClip GenerateElasticBounce()
    {
        float duration = 0.18f;
        int samples = Mathf.CeilToInt(duration * proceduralSampleRate);
        float[] data = new float[samples];

        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)proceduralSampleRate;
            float normalized = t / duration;
            float frequency = Mathf.Lerp(360f, 120f, normalized);
            float envelope = Mathf.Exp(-normalized * 7f);
            float snap = Mathf.Sin(2f * Mathf.PI * frequency * t);
            float click = Mathf.Sin(2f * Mathf.PI * 1400f * t) * Mathf.Exp(-normalized * 22f) * 0.35f;
            data[i] = (snap + click) * envelope * proceduralToneVolume * 0.75f;
        }

        return CreateClip("Generated Elastic Bounce", data);
    }

    private AudioClip GenerateFinishArpeggio()
    {
        float duration = 0.95f;
        int samples = Mathf.CeilToInt(duration * proceduralSampleRate);
        float[] data = new float[samples];

        float[] noteStarts = { 0f, 0.16f, 0.32f, 0.48f };
        float[] frequencies = { 523.25f, 659.25f, 783.99f, 1046.5f };

        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)proceduralSampleRate;
            float sample = 0f;

            for (int n = 0; n < frequencies.Length; n++)
            {
                float localTime = t - noteStarts[n];
                if (localTime < 0f)
                {
                    continue;
                }

                float noteDuration = duration - noteStarts[n];
                float normalized = Mathf.Clamp01(localTime / noteDuration);
                float envelope = AttackDecayEnvelope(normalized, 0.05f, 0.92f);
                float tone = Mathf.Sin(2f * Mathf.PI * frequencies[n] * localTime);
                float harmonic = Mathf.Sin(2f * Mathf.PI * frequencies[n] * 2f * localTime) * 0.18f;
                sample += (tone + harmonic) * envelope * 0.33f;
            }

            data[i] = sample * proceduralToneVolume * 0.8f;
        }

        return CreateClip("Generated Finish Arpeggio", data);
    }

    private AudioClip GenerateRestartBlip()
    {
        float duration = 0.28f;
        int samples = Mathf.CeilToInt(duration * proceduralSampleRate);
        float[] data = new float[samples];

        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)proceduralSampleRate;
            float normalized = t / duration;
            float frequency = Mathf.Lerp(620f, 260f, normalized);
            float envelope = AttackDecayEnvelope(normalized, 0.04f, 0.82f);
            float tone = Mathf.Sin(2f * Mathf.PI * frequency * t);
            data[i] = tone * envelope * proceduralToneVolume * 0.55f;
        }

        return CreateClip("Generated Restart Blip", data);
    }

    private AudioClip GenerateAmbientLoop()
    {
        float duration = 4f;
        int samples = Mathf.CeilToInt(duration * proceduralSampleRate);
        float[] data = new float[samples];

        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)proceduralSampleRate;
            float normalized = t / duration;

            float loopEnvelope = 0.85f + 0.15f * Mathf.Sin(2f * Mathf.PI * normalized);
            float toneA = Mathf.Sin(2f * Mathf.PI * 110f * t) * 0.32f;
            float toneB = Mathf.Sin(2f * Mathf.PI * 165f * t) * 0.22f;
            float toneC = Mathf.Sin(2f * Mathf.PI * 220f * t) * 0.14f;
            float pulse = Mathf.Sin(2f * Mathf.PI * 0.5f * t) * 0.08f;

            data[i] = (toneA + toneB + toneC + pulse) *
                      loopEnvelope *
                      proceduralToneVolume *
                      proceduralMusicVolumeScale *
                      0.35f;
        }

        return CreateClip("Generated Ambient Maze Loop", data);
    }

    private AudioClip CreateClip(string clipName, float[] samples)
    {
        AudioClip clip = AudioClip.Create(
            clipName,
            samples.Length,
            1,
            proceduralSampleRate,
            false);

        clip.SetData(samples, 0);
        return clip;
    }

    private float AttackDecayEnvelope(float normalizedTime, float attackEnd, float releaseStart)
    {
        normalizedTime = Mathf.Clamp01(normalizedTime);

        if (normalizedTime < attackEnd)
        {
            return Mathf.SmoothStep(0f, 1f, normalizedTime / Mathf.Max(0.0001f, attackEnd));
        }

        if (normalizedTime > releaseStart)
        {
            float release = Mathf.InverseLerp(1f, releaseStart, normalizedTime);
            return Mathf.SmoothStep(0f, 1f, release);
        }

        return 1f;
    }
}

public sealed class MazeAudioCollisionRelay : MonoBehaviour
{
    private MazeAudioManager owner;

    public void Initialize(MazeAudioManager audioManager)
    {
        owner = audioManager;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (owner != null)
        {
            owner.HandlePlayerCollision(collision);
        }
    }
}