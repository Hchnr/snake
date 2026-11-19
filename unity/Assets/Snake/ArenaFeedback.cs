using System;
using SnakeClub.Core;
using UnityEngine;

namespace SnakeClub
{
    // Short procedural cues avoid large audio downloads. They are optional and
    // describe game events, never collision or scoring inputs.
    public sealed class ArenaFeedback : MonoBehaviour
    {
        private AudioSource output, musicOutput;
        private AudioClip homeMusic, battleMusic;
        private bool interacted, musicPaused, homeGenerated, battleGenerated;
        private readonly MusicLoadRetry homeRetry = new MusicLoadRetry(), battleRetry = new MusicLoadRetry();
        private bool observedMusicEnabled = true;
        private int observedMusicScene = -1;
#if UNITY_EDITOR
        private Func<bool, AudioClip> musicClipLoaderOverride = null;
#endif
        private AudioClip growth, rebound, elimination, capture, star, finish, strengthBite, strengthDamage, strengthLevelUp, strengthPickup;
        private int length, kills, stars;
        private readonly FeedbackCursor cursor = new FeedbackCursor();
        private readonly MusicTransition musicTransition = new MusicTransition();
        private int captureTick = -1;
        private int bossShield = -1;
        private float pulse;
        private bool ended;
        private float nextGrowth, nextHaptic;
        public int MusicVolume
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt("snake.v3.musicVolume", 100), 0, 100);
            set { PlayerPrefs.SetInt("snake.v3.musicVolume", Mathf.Clamp(value, 0, 100)); PlayerPrefs.Save(); if (musicOutput != null) musicOutput.volume = .25f * MusicVolume / 100f * musicTransition.Gain; }
        }
        public int EffectsVolume
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt("snake.v3.effectsVolume", 100), 0, 100);
            set { PlayerPrefs.SetInt("snake.v3.effectsVolume", Mathf.Clamp(value, 0, 100)); PlayerPrefs.Save(); if (output != null) output.volume = .38f * EffectsVolume / 100f; }
        }
        public bool MusicEnabled
        {
            get => PlayerPrefs.GetInt("snake.v3.music", 1) != 0;
            set
            {
                if (value && !MusicEnabled) { homeRetry.Reset(); battleRetry.Reset(); }
                PlayerPrefs.SetInt("snake.v3.music", value ? 1 : 0); PlayerPrefs.Save();
                if (!value) SuspendMusic();
            }
        }
        public bool HapticsEnabled
        {
            get => PlayerPrefs.GetInt("snake.v3.haptics", 0) != 0;
            set { PlayerPrefs.SetInt("snake.v3.haptics", value ? 1 : 0); PlayerPrefs.Save(); }
        }
        public bool Enabled
        {
            get => PlayerPrefs.GetInt("snake.v3.sound", 1) != 0;
            set { PlayerPrefs.SetInt("snake.v3.sound", value ? 1 : 0); PlayerPrefs.Save(); if (output != null) { output.mute = !value; if (!value) output.Stop(); } }
        }

        private void Awake() => Initialize();

        private void OnEnable()
        {
            if (Application.isPlaying && (output == null || growth == null)) Initialize();
        }

        private void Initialize()
        {
            output = GetComponent<AudioSource>();
            if (output == null) output = gameObject.AddComponent<AudioSource>();
            output.playOnAwake = false; output.spatialBlend = 0; output.volume = .38f * EffectsVolume / 100f; output.mute = !Enabled;
            if (musicOutput == null) musicOutput = gameObject.AddComponent<AudioSource>();
            musicOutput.playOnAwake = false; musicOutput.spatialBlend = 0; musicOutput.loop = true; musicOutput.volume = .25f * MusicVolume / 100f * musicTransition.Gain;
            growth = MakeClip("Growth", 700, 960, .07f);
            rebound = MakeClip("Rebound", 300, 580, .16f);
            elimination = MakeClip("Elimination", 440, 880, .20f);
            capture = MakeClip("Loop closed", 520, 780, .12f);
            star = MakeClip("Star", 620, 1180, .24f);
            finish = MakeClip("Finish", 600, 360, .28f);
            strengthBite = MakeClip("Body bite", 520, 1040, .10f);
            strengthDamage = MakeClip("Body damage", 480, 190, .16f);
            strengthLevelUp = MakeClip("Match level up", 680, 1320, .18f);
            strengthPickup = MakeClip("Energy pickup", 740, 1180, .13f);
        }

        public void Observe(ArenaState state, string playerId, PveFixedWorld strengthWorld = null)
        {
            if (state == null || string.IsNullOrEmpty(playerId)) return;
            var snake = state.snakes.Find(item => item.id == playerId);
            if (snake == null) return;
            var boss = state.snakes.Find(item => item.boss);
            int previousTick = cursor.LastTick;
            if (cursor.Observe(state.tick))
            {
                length = snake.segments.Count; kills = snake.kills; stars = snake.starPoints;
                bossShield = boss != null ? boss.bossShield : -1;
                pulse = snake.rebound; ended = state.phase == "ended"; captureTick = state.captureTick; return;
            }
            bool eliminationOccurred = snake.kills > kills;
            bool captureOccurred = StrengthSoundFeedback.CaptureHaptic(strengthWorld != null && strengthWorld.StrengthPreview,
                previousTick, state.tick, state.captureTick, state.captureOwner, playerId, state.captureCount);
            bool reboundOccurred = snake.rebound > 0 && pulse <= 0;
            bool starOccurred = snake.starPoints > stars;
            bool bossHit = boss != null && bossShield >= 0 && boss.bossShield < bossShield;
            var strengthCue = StrengthSoundFeedback.Select(strengthWorld != null && strengthWorld.StrengthPreview,
                previousTick, state.tick, strengthWorld != null ? strengthWorld.LastPlayerAttackTick : -1,
                strengthWorld != null ? strengthWorld.LastPlayerDamageTick : -1,
                strengthWorld != null ? strengthWorld.LastPlayerPickupTick : -1);
            if (HapticsEnabled && GamePlatform.SupportsHaptics && Time.unscaledTime >= nextHaptic &&
                (eliminationOccurred || captureOccurred || reboundOccurred || starOccurred || bossHit || strengthCue != StrengthSoundCue.None))
            {
                GamePlatform.VibrateShort();
                nextHaptic = Time.unscaledTime + .5f;
            }
            if (Enabled)
            {
                if (!ended && state.phase == "ended") output.PlayOneShot(finish);
                else if (boss != null && bossShield >= 0 && boss.bossShield < bossShield) output.PlayOneShot(star);
                else if (snake.starPoints > stars) output.PlayOneShot(star);
                else if (snake.kills > kills) output.PlayOneShot(elimination);
                else if (strengthCue == StrengthSoundCue.Damage) output.PlayOneShot(strengthDamage);
                else if (strengthCue == StrengthSoundCue.Bite) output.PlayOneShot(strengthBite);
                else if (strengthCue == StrengthSoundCue.Pickup) output.PlayOneShot(strengthPickup);
                else if (StrengthLevelNotice.NewSince(strengthWorld, previousTick, state.tick)) output.PlayOneShot(strengthLevelUp);
                else if (state.captureOwner == playerId && state.captureTick >= 0 && state.captureTick != captureTick) output.PlayOneShot(capture);
                else if (snake.rebound > 0 && pulse <= 0) output.PlayOneShot(rebound);
                else if (snake.segments.Count > length && Time.unscaledTime >= nextGrowth)
                {
                    output.PlayOneShot(growth); nextGrowth = Time.unscaledTime + .18f;
                }
            }
            length = snake.segments.Count; kills = snake.kills;
            bossShield = boss != null ? boss.bossShield : -1;
            stars = snake.starPoints; pulse = snake.rebound; ended = state.phase == "ended"; captureTick = state.captureTick;
        }

        public void UpdateMusic(bool battle, bool focused, bool paused, bool adShowing, bool userInteraction)
        {
            if (userInteraction) interacted = true;
            bool enabled = MusicEnabled;
            if (enabled && !observedMusicEnabled) { homeRetry.Reset(); battleRetry.Reset(); }
            observedMusicEnabled = enabled;
            if (!MusicPolicy.ShouldPlay(enabled, interacted, focused, paused, adShowing))
            {
                SuspendMusic();
                return;
            }
            int scene = battle ? 1 : 0;
            var retry = battle ? battleRetry : homeRetry;
            if (observedMusicScene != scene) { retry.Reset(); observedMusicScene = scene; }
            retry.Advance(Time.unscaledDeltaTime);
            AudioClip desired = battle ? battleMusic : homeMusic;
            if (desired == null)
            {
                if (!retry.Ready) { StopUnavailableMusic(); return; }
                retry.Attempted();
                // The previous cached clip is absent; ownership applies only
                // to a clip generated by this attempt.
                if (battle) battleGenerated = false; else homeGenerated = false;
                try
                {
#if UNITY_EDITOR
                    if (musicClipLoaderOverride != null) desired = musicClipLoaderOverride(battle);
                    else
#endif
                    {
                        // Resources clips must be cleared for the game's actual distribution rights.
                        try { desired = Resources.Load<AudioClip>(battle ? "Music/Battle" : "Music/Home"); }
                        catch (Exception) { desired = null; }
                        if (desired == null)
                        {
                            float[] samples = MusicComposer.Create(battle);
                            desired = AudioClip.Create(battle ? "Original battle loop" : "Original home loop", samples.Length, 1, MusicComposer.SampleRate, false);
                            if (battle) battleGenerated = desired != null; else homeGenerated = desired != null;
                            if (desired != null && !desired.SetData(samples, 0))
                            {
                                Destroy(desired); desired = null;
                                if (battle) battleGenerated = false; else homeGenerated = false;
                            }
                        }
                    }
                    if (battle) battleMusic = desired; else homeMusic = desired;
                }
                catch (Exception)
                {
                    if (desired != null && (battle ? battleGenerated : homeGenerated)) Destroy(desired);
                    if (battle) battleGenerated = false; else homeGenerated = false;
                    desired = null;
                }
                if (desired == null) { StopUnavailableMusic(); return; }
                retry.Reset();
            }
            if (musicPaused) { musicOutput.UnPause(); musicPaused = false; }
            if (musicTransition.Advance(battle, Time.unscaledDeltaTime))
            {
                musicOutput.Stop(); musicOutput.clip = desired;
                musicOutput.volume = 0; musicOutput.Play();
            }
            else if (musicOutput.clip != null && !musicOutput.isPlaying) musicOutput.Play();
            musicOutput.volume = .25f * MusicVolume / 100f * musicTransition.Gain;
        }

        private void StopUnavailableMusic()
        {
            musicOutput.Stop(); musicOutput.clip = null; musicOutput.volume = 0;
            musicPaused = false; musicTransition.Reset();
        }

        public void SuspendAudio()
        {
            SuspendMusic();
            if (output != null) output.Stop();
            cursor.Suspend();
        }

        // Called before the next simulation step so new events on that step
        // remain audible while old background events are not replayed.
        public void RebaselineAfterSuspend(ArenaState state, string playerId)
        {
            if (cursor.PendingBaseline) Observe(state, playerId);
        }

        public void SuspendMusic()
        {
            if (musicOutput != null && musicOutput.isPlaying) { musicOutput.Pause(); musicPaused = true; }
        }

        private static AudioClip MakeClip(string name, float from, float to, float seconds)
        {
            const int rate = 22050;
            int size = Mathf.CeilToInt(rate * seconds);
            var samples = new float[size];
            float phase = 0;
            for (int i = 0; i < size; i++)
            {
                float progress = i / (float)size;
                phase += Mathf.Lerp(from, to, progress) * 2 * Mathf.PI / rate;
                float envelope = Mathf.Min(1f, progress * 25f) * (1f - progress) * (1f - progress);
                samples[i] = Mathf.Sin(phase) * envelope;
            }
            var clip = AudioClip.Create(name, size, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private void OnDestroy()
        {
            if (homeGenerated && homeMusic != null) Destroy(homeMusic); if (battleGenerated && battleMusic != null) Destroy(battleMusic);
            if (growth != null) Destroy(growth); if (rebound != null) Destroy(rebound);
            if (elimination != null) Destroy(elimination); if (capture != null) Destroy(capture); if (star != null) Destroy(star); if (finish != null) Destroy(finish);
            if (strengthBite != null) Destroy(strengthBite); if (strengthDamage != null) Destroy(strengthDamage);
            if (strengthLevelUp != null) Destroy(strengthLevelUp); if (strengthPickup != null) Destroy(strengthPickup);
        }
    }
}
