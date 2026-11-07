using System.Collections.Generic;
using UnityEngine;

namespace BattleBlood
{
    /// <summary>
    /// All sound in one pool. Every file is levelled to the same loudness by tools/level_audio.py; the mix between kinds of sound is set here.
    /// Variation is reuse: random variant, pitch and level. A sound already played many times this moment is skipped, so a melee stays a roar and not a wall.
    /// </summary>
    public class GameAudio : MonoBehaviour
    {
        public static GameAudio Instance { get; private set; }
        const int Voices = 40;
        readonly AudioSource[] pool = new AudioSource[Voices];
        int nextVoice;
        readonly Dictionary<string, int> recent = new();
        float recentReset;
        AudioSource music, musicB, ambience, melee, gallop, fireLoop;
        float musicTarget = 0.6f, muffle, meleeLevel;
        AudioLowPassFilter lowpass;

        static readonly Dictionary<string, float> GroupGain = new()
        {
            ["step"] = 0.55f, ["hoof"] = 0.7f, ["swing"] = 0.65f, ["pain"] = 0.72f, ["cry"] = 0.65f,
            ["flesh"] = 1f, ["gore"] = 0.9f, ["sever"] = 1f, ["spurt"] = 0.7f, ["scream"] = 0.85f, ["burn_scream"] = 0.95f,
        };

        public static GameAudio Create()
        {
            var go = new GameObject("Audio");
            Instance = go.AddComponent<GameAudio>();
            Instance.Build();
            return Instance;
        }

        void Build()
        {
            for (int i = 0; i < Voices; i++)
            {
                var s = new GameObject("voice").AddComponent<AudioSource>();
                s.transform.SetParent(transform, false);
                s.playOnAwake = false; s.spatialBlend = 1f; s.rolloffMode = AudioRolloffMode.Logarithmic;
                s.minDistance = 4f; s.maxDistance = 130f; s.dopplerLevel = 0f;
                pool[i] = s;
            }
            music = Loop("music", 0f, 2f);
            musicB = Loop("music b", 0f, 2f);
            ambience = Loop("ambience", 0f, 2f);
            swamp = Loop("swamp", 0f, 2f);
            melee = Loop("melee", 0f, 2f);
            gallop = Loop("gallop", 0f, 2f);
            fireLoop = Loop("fire", 0f, 1f);
            fireLoop.spatialBlend = 1f; fireLoop.minDistance = 5f; fireLoop.maxDistance = 60f; fireLoop.rolloffMode = AudioRolloffMode.Linear;
        }

        AudioSource Loop(string name, float volume, float blend2d)
        {
            var s = new GameObject(name).AddComponent<AudioSource>();
            s.transform.SetParent(transform, false);
            s.loop = true; s.playOnAwake = false; s.spatialBlend = 0f; s.volume = volume;
            return s;
        }

        static AudioClip One(string group)
        {
            var c = Art.Clips(group);
            return c.Length > 0 ? c[Random.Range(0, c.Length)] : null;
        }

        public static void Play(string group, Vector3 pos, float volume = 1f, float pitchVar = 0.09f)
        {
            if (Instance == null) return;
            var cam = Camera.main;
            if (cam != null && (cam.transform.position - pos).sqrMagnitude > 170f * 170f) return;
            Instance.PlayAt(group, pos, volume, pitchVar, 1f);
        }

        public static void Play2D(string group, float volume = 1f)
        {
            if (Instance == null) return;
            Instance.PlayAt(group, Vector3.zero, volume, 0.02f, 0f);
        }

        void PlayAt(string group, Vector3 pos, float volume, float pitchVar, float blend)
        {
            recent.TryGetValue(group, out int n);
            if (n >= 5) return;
            recent[group] = n + 1;
            var clip = One(group);
            if (clip == null) return;
            var s = pool[nextVoice];
            nextVoice = (nextVoice + 1) % Voices;
            s.transform.position = pos;
            s.clip = clip;
            s.spatialBlend = blend;
            s.pitch = 1f + Random.Range(-pitchVar, pitchVar);
            GroupGain.TryGetValue(group, out float g);
            s.volume = volume * (g > 0f ? g : 0.85f);
            s.Play();
        }

        public void PlayMusic(string group, float volume)
        {
            var c = One(group);
            if (c == null) return;
            // Crossfade: the new track on the idle source.
            (music, musicB) = (musicB, music);
            music.clip = c; music.volume = 0f; music.Play();
            musicTarget = volume;
        }

        public void StartBattle(bool swampy)
        {
            // Wind over the whole field; the swamp's frogs and insects only near its water, and quietly (see Update).
            var amb = One("wind");
            if (amb != null) { ambience.clip = amb; ambience.volume = 0.5f; ambience.Play(); }
            var sw = swampy ? One("swamp") : null;
            if (sw != null) { swamp.clip = sw; swamp.volume = 0f; swamp.Play(); }
            else swamp.Stop();
            var m = One("melee");
            if (m != null) { melee.clip = m; melee.volume = 0f; melee.Play(); }
            var gal = One("gallop");
            if (gal != null) { gallop.clip = gal; gallop.volume = 0f; gallop.Play(); }
            PlayMusic("music_march", 0.5f);
        }

        public void BattleJoined(bool fighting)
        {
            meleeLevel = fighting ? 0.75f : 0.18f;
            if (fighting) PlayMusic("music_battle", 0.56f);
        }

        public void BattleEnded(bool won)
        {
            meleeLevel = 0f;
            musicTarget = 0f;
            Play2D(won ? "victory" : "defeat", 1f);
        }

        /// <summary>The crackle comes from the fire closest to the listener.</summary>
        public void FireLoopAt(Vector3? pos)
        {
            if (pos.HasValue)
            {
                fireLoop.transform.position = pos.Value;
                if (!fireLoop.isPlaying) { var c = One("fire"); if (c != null) { fireLoop.clip = c; fireLoop.Play(); } }
            }
            fireWant = pos.HasValue ? 0.8f : 0f;
        }
        float fireWant;
        AudioSource swamp; AudioListener listener; float swampCheck, swampWant;
        public const float SwampVolume = 0.22f;

        public void Muffle(bool on) { muffle = on ? 1f : 0f; }

        void Update()
        {
            recentReset -= Time.unscaledDeltaTime;
            if (recentReset <= 0f) { recent.Clear(); recentReset = 0.12f; }
            float dt = Time.unscaledDeltaTime;
            music.volume = Mathf.MoveTowards(music.volume, musicTarget, dt * 0.4f);
            musicB.volume = Mathf.MoveTowards(musicB.volume, 0f, dt * 0.4f);
            if (musicB.volume <= 0f && musicB.isPlaying) musicB.Stop();
            melee.volume = Mathf.MoveTowards(melee.volume, meleeLevel, dt * 0.3f);
            var b = Battle.I;
            float gallopWant = 0f;
            if (b != null && b.Ready && b.Player != null && b.Player.Type == UnitType.Cavalry && b.Player.Alive)
            {
                gallopWant = Mathf.InverseLerp(3f, 12f, b.Player.Speed) * 0.8f;
                gallop.pitch = Mathf.Lerp(0.85f, 1.25f, Mathf.InverseLerp(4f, 14f, b.Player.Speed));
            }
            gallop.volume = Mathf.MoveTowards(gallop.volume, gallopWant, dt * 1.5f);
            fireLoop.volume = Mathf.MoveTowards(fireLoop.volume, fireWant, dt * 1.2f);
            // The swamp is heard only near it: full (and still soft) at the water's edge, gone 35 m away, or from high above.
            if (swamp.isPlaying && b != null && b.Ready)
            {
                swampCheck -= dt;
                if (swampCheck <= 0f)
                {
                    swampCheck = 0.25f;
                    if (listener == null || !listener.isActiveAndEnabled) listener = FindAnyObjectByType<AudioListener>();
                    if (listener != null)
                    {
                        Vector3 p = listener.transform.position;
                        float up = Mathf.Max(0f, p.y - b.Field.Height(p.x, p.z) - 6f);
                        float d = b.Field.DistanceToSwamp(p) + up;
                        swampWant = SwampVolume * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(6f, 35f, d)));
                    }
                }
                swamp.volume = Mathf.MoveTowards(swamp.volume, swampWant, dt * 0.25f);
            }
            AudioListener.volume = Mathf.MoveTowards(AudioListener.volume, muffle > 0.5f ? 0.45f : 1f, dt * 1.5f);
        }

        void OnDestroy() { AudioListener.volume = 1f; }
    }
}
