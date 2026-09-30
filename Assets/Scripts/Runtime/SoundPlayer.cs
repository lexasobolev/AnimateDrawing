using System.Collections.Generic;
using AnimatedDrawingsWorld.Logic.Audio;
using AnimatedDrawingsWorld.Logic.Behavior;
using UnityEngine;

namespace AnimatedDrawingsWorld.Runtime
{
    // Plays the characters' synthesized sounds. SoundDirector (game logic) decides what should be
    // heard and keeps it calm; this turns cues into AudioClips (generated once, then cached) and
    // plays them on a small pool of 2D audio sources, panned by where the character stands.
    public sealed class SoundPlayer : MonoBehaviour
    {
        private const string MutedKey = "AnimatedDrawingsWorld.Muted";
        private const int Voices = 4;

        private readonly SoundDirector director = new();
        private readonly List<SoundCue> cues = new();
        private readonly Dictionary<(SoundKind, CharacterKind, int, int), AudioClip> clips = new();
        private AudioSource[] sources;
        private GameController game;

        public bool Muted { get; private set; }

        private void Awake()
        {
            game = GetComponent<GameController>();
            Muted = PlayerPrefs.GetInt(MutedKey, 0) == 1;
            sources = new AudioSource[Voices];
            for (var i = 0; i < Voices; i++)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                sources[i] = source;
            }
        }

        public void SetMuted(bool muted)
        {
            Muted = muted;
            PlayerPrefs.SetInt(MutedKey, muted ? 1 : 0);
            if (muted) foreach (var s in sources) s.Stop();
        }

        public void PlayUi(SoundKind kind) => Play(director.UiCue(kind));

        private void LateUpdate()
        {
            if (game == null || game.World == null) return;
            cues.Clear();
            director.Update(game.World, cues); // runs even when muted, so unmuting doesn't replay old events
            foreach (var cue in cues) Play(cue);
        }

        private void Play(SoundCue cue)
        {
            if (Muted) return;
            var clip = ClipFor(cue);
            var source = FreeSource();
            source.clip = clip;
            source.volume = cue.Volume;
            source.panStereo = cue.Pan;
            source.pitch = 1f;
            source.Play();
        }

        private AudioSource FreeSource()
        {
            AudioSource oldest = sources[0];
            foreach (var s in sources)
            {
                if (!s.isPlaying) return s;
                if (s.time > oldest.time) oldest = s;
            }
            return oldest; // all busy: cut the one that has played longest
        }

        private AudioClip ClipFor(SoundCue cue)
        {
            // voices are cached per 5%-pitch step and a few variations, so characters differ but
            // the cache stays small
            var pitchStep = Mathf.RoundToInt(cue.Pitch * 20f);
            var key = (cue.Kind, cue.Voice, pitchStep, cue.Variant);
            if (clips.TryGetValue(key, out var clip)) return clip;

            var samples = SoundSynth.Generate(cue.Kind, cue.Voice, pitchStep / 20f, (int)cue.Kind * 7919 + (int)cue.Voice * 104729 + pitchStep * 31 + cue.Variant);
            clip = AudioClip.Create($"{cue.Kind}_{cue.Voice}_{pitchStep}_{cue.Variant}", samples.Length, 1, SoundSynth.SampleRate, false);
            clip.SetData(samples, 0);
            clips[key] = clip;
            return clip;
        }

        private void OnDestroy()
        {
            foreach (var clip in clips.Values) Destroy(clip);
        }
    }
}
