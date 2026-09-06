using System.Collections.Generic;
using UnityEngine;
using AshenSol.Core;

namespace AshenSol.Audio
{
    /// <summary>Pooled SFX, crossfading music with seamless loop, one ambience bed.</summary>
    public class AudioManager : MonoBehaviour, IAudioService
    {
        public static AudioManager Instance { get; private set; }

        const int SfxVoices = 12;
        const float SfxMaster = 0.9f;
        const float MusicMaster = 0.55f;
        const float LoopCrossfade = 2.5f;

        AudioSource[] sfx;
        int sfxIndex;
        readonly Dictionary<string, float> lastPlay = new Dictionary<string, float>();

        class MusicVoice { public AudioSource Src; public float Target; public float Speed = 1f; public bool LoopHandoffDone; }
        MusicVoice mA, mB, current;
        string currentMusic;
        float duck = 1f, duckTarget = 1f, duckSpeed = 1f;

        AudioSource ambience; string ambienceName; float ambTarget, ambSpeed = 1f;

        void Awake()
        {
            Instance = this;
            Services.Audio = this;
            sfx = new AudioSource[SfxVoices];
            for (int i = 0; i < SfxVoices; i++) sfx[i] = MakeSource("Sfx" + i, false);
            mA = new MusicVoice { Src = MakeSource("MusicA", false) };
            mB = new MusicVoice { Src = MakeSource("MusicB", false) };
            ambience = MakeSource("Ambience", true);
        }

        AudioSource MakeSource(string name, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            s.loop = loop;
            s.volume = 0f;
            return s;
        }

        // ---------------- SFX ----------------
        public void PlaySfx(string name, float volume = 1f, float pitchVariance = 0.06f)
        {
            if (string.IsNullOrEmpty(name)) return;
            var clip = Res.Clip(Res.SfxPath + name);
            if (clip == null) return;
            float now = Time.unscaledTime;
            float last;
            if (lastPlay.TryGetValue(name, out last) && now - last < 0.04f) return;
            lastPlay[name] = now;
            var s = sfx[sfxIndex];
            sfxIndex = (sfxIndex + 1) % SfxVoices;
            s.Stop();
            s.clip = clip;
            s.loop = false;
            s.pitch = 1f + Random.Range(-pitchVariance, pitchVariance);
            s.volume = Mathf.Clamp01(volume) * SfxMaster;
            s.Play();
        }

        public void PlaySfxAt(string name, Vector2 worldPos, float volume = 1f, float pitchVariance = 0.06f)
        {
            float dist = 0f;
            if (Services.Cam != null && Services.Cam.Transform != null)
            {
                Vector2 c = Services.Cam.Transform.position;
                dist = Vector2.Distance(c, worldPos);
            }
            float att = 1f - Mathf.Clamp01((dist - 7f) / 12f);
            if (att <= 0.02f) return;
            PlaySfx(name, volume * att, pitchVariance);
        }

        // ---------------- Music ----------------
        public void PlayMusic(string name, float fadeSeconds = 1.5f)
        {
            if (name == currentMusic && current != null && current.Src.isPlaying) return;
            currentMusic = name;
            var clip = string.IsNullOrEmpty(name) ? null : Res.Clip(Res.MusicPath + name);
            float speed = 1f / Mathf.Max(0.05f, fadeSeconds);
            if (current != null) { current.Target = 0f; current.Speed = speed; }
            if (clip == null) { current = null; return; }
            var next = (current == mA) ? mB : mA;
            next.Src.Stop();
            next.Src.clip = clip;
            next.Src.time = 0f;
            next.Src.volume = 0f;
            next.Src.Play();
            next.Target = 1f; next.Speed = speed; next.LoopHandoffDone = false;
            current = next;
        }

        public void StopMusic(float fadeSeconds = 1f)
        {
            currentMusic = null;
            float speed = 1f / Mathf.Max(0.05f, fadeSeconds);
            mA.Target = 0f; mA.Speed = speed;
            mB.Target = 0f; mB.Speed = speed;
            current = null;
        }

        public void SetMusicDuck(float multiplier, float seconds)
        {
            duckTarget = Mathf.Clamp01(multiplier);
            duckSpeed = Mathf.Abs(duckTarget - duck) / Mathf.Max(0.05f, seconds);
        }

        // ---------------- Ambience ----------------
        public void PlayAmbience(string name, float volume = 0.5f)
        {
            if (string.IsNullOrEmpty(name))
            {
                ambienceName = null; ambTarget = 0f; ambSpeed = 1f;
                return;
            }
            if (name == ambienceName) { ambTarget = volume; return; }
            var clip = Res.Clip(Res.SfxPath + name);
            ambienceName = name;
            if (clip == null) { ambTarget = 0f; return; }
            ambience.Stop();
            ambience.clip = clip;
            ambience.loop = true;
            ambience.volume = 0f;
            ambience.Play();
            ambTarget = volume; ambSpeed = 0.5f;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            duck = Mathf.MoveTowards(duck, duckTarget, duckSpeed * dt);
            TickVoice(mA, dt);
            TickVoice(mB, dt);

            // seamless loop handoff
            if (current != null && current.Src.clip != null && current.Src.isPlaying && !current.LoopHandoffDone)
            {
                float remaining = current.Src.clip.length - current.Src.time;
                if (remaining <= LoopCrossfade)
                {
                    current.LoopHandoffDone = true;
                    var next = current == mA ? mB : mA;
                    next.Src.Stop();
                    next.Src.clip = current.Src.clip;
                    next.Src.time = 0f;
                    next.Src.volume = 0f;
                    next.Src.Play();
                    next.Target = 1f; next.Speed = 1f / LoopCrossfade; next.LoopHandoffDone = false;
                    current.Target = 0f; current.Speed = 1f / LoopCrossfade;
                    current = next;
                }
            }

            float ambGoal = ambTarget;
            ambience.volume = Mathf.MoveTowards(ambience.volume, ambGoal, ambSpeed * dt);
            if (ambience.volume <= 0.001f && ambGoal <= 0f && ambience.isPlaying) ambience.Stop();
        }

        void TickVoice(MusicVoice v, float dt)
        {
            float goal = v.Target * duck * MusicMaster;
            v.Src.volume = Mathf.MoveTowards(v.Src.volume, goal, v.Speed * MusicMaster * dt);
            if (v.Target <= 0f && v.Src.volume <= 0.001f && v.Src.isPlaying) v.Src.Stop();
        }
    }
}
