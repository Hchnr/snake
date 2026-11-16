using SnakeClub.Core;
using UnityEngine;

namespace SnakeClub
{
    // Short procedural cues avoid large audio downloads. They are optional and
    // describe game events, never collision or scoring inputs.
    public sealed class ArenaFeedback : MonoBehaviour
    {
        private AudioSource output;
        private AudioClip growth, rebound, elimination, capture, star, finish;
        private int tick = -1, length, kills, stars;
        private int captureTick = -1;
        private int bossShield = -1;
        private float pulse;
        private bool ended;
        private float nextGrowth;
        public bool Enabled
        {
            get => PlayerPrefs.GetInt("snake.v3.sound", 1) != 0;
            set { PlayerPrefs.SetInt("snake.v3.sound", value ? 1 : 0); PlayerPrefs.Save(); }
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
            output.playOnAwake = false; output.spatialBlend = 0; output.volume = .38f;
            growth = MakeClip("Growth", 700, 960, .07f);
            rebound = MakeClip("Rebound", 300, 580, .16f);
            elimination = MakeClip("Elimination", 440, 880, .20f);
            capture = MakeClip("Loop closed", 520, 780, .12f);
            star = MakeClip("Star", 620, 1180, .24f);
            finish = MakeClip("Finish", 600, 360, .28f);
        }

        public void Observe(ArenaState state, string playerId)
        {
            if (state == null || string.IsNullOrEmpty(playerId)) return;
            var snake = state.snakes.Find(item => item.id == playerId);
            if (snake == null) return;
            var boss = state.snakes.Find(item => item.boss);
            if (tick < 0 || state.tick < tick)
            {
                length = snake.segments.Count; kills = snake.kills; stars = snake.starPoints;
                bossShield = boss != null ? boss.bossShield : -1;
                pulse = snake.rebound; ended = state.phase == "ended"; captureTick = state.captureTick; tick = state.tick; return;
            }
            if (Enabled)
            {
                if (!ended && state.phase == "ended") output.PlayOneShot(finish);
                else if (boss != null && bossShield >= 0 && boss.bossShield < bossShield) output.PlayOneShot(star);
                else if (snake.starPoints > stars) output.PlayOneShot(star);
                else if (snake.kills > kills) output.PlayOneShot(elimination);
                else if (state.captureOwner == playerId && state.captureTick >= 0 && state.captureTick != captureTick) output.PlayOneShot(capture);
                else if (snake.rebound > 0 && pulse <= 0) output.PlayOneShot(rebound);
                else if (snake.segments.Count > length && Time.unscaledTime >= nextGrowth)
                {
                    output.PlayOneShot(growth); nextGrowth = Time.unscaledTime + .18f;
                }
            }
            tick = state.tick; length = snake.segments.Count; kills = snake.kills;
            bossShield = boss != null ? boss.bossShield : -1;
            stars = snake.starPoints; pulse = snake.rebound; ended = state.phase == "ended"; captureTick = state.captureTick;
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
            if (growth != null) Destroy(growth); if (rebound != null) Destroy(rebound);
            if (elimination != null) Destroy(elimination); if (capture != null) Destroy(capture); if (star != null) Destroy(star); if (finish != null) Destroy(finish);
        }
    }
}
