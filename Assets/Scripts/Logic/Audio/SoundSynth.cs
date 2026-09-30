using System;
using System.Collections.Generic;
using AnimatedDrawingsWorld.Logic.Behavior;

namespace AnimatedDrawingsWorld.Logic.Audio
{
    public enum SoundKind
    {
        Babble,    // talking
        Hello,     // waving
        Surprise,  // "eek!"
        Scare,     // monster roar / "boo"
        Yawn,
        Snore,
        Boing,     // jumping
        Splash,    // into the water
        Whee,      // picked up by the player
        Thud,      // landing
        Sniff,     // smelling a flower
        Jingle,    // starting to dance
        Happy,     // being petted / high five
        Pop,       // a character arrives
        Whoosh,    // new background
        Sweep,     // everyone cleared
        Click,     // a button
    }

    // Procedural sound effects: every sound is synthesized (no audio files), so each character
    // can have its own voice pitch. Voices are a buzzy source through two vowel formant filters;
    // the character kind changes the source — children's voices are high and clean, monsters low,
    // rough and growly, animals nasal and squeaky.
    public static class SoundSynth
    {
        public const int SampleRate = 22050;

        // (F1, F2) of the vowels a, e, i, o, u
        private static readonly (float f1, float f2)[] Vowels =
        {
            (800f, 1200f), (500f, 1900f), (320f, 2400f), (500f, 900f), (330f, 800f),
        };

        public static float[] Generate(SoundKind kind, CharacterKind voice, float pitch, int seed)
        {
            var rng = new Random(seed);
            var p = Math.Max(0.5f, pitch);
            float[] s = kind switch
            {
                SoundKind.Babble => Babble(voice, p, rng),
                SoundKind.Hello => Hello(voice, p, rng),
                SoundKind.Surprise => Surprise(voice, p, rng),
                SoundKind.Scare => Scare(voice, p, rng),
                SoundKind.Yawn => Yawn(voice, p, rng),
                SoundKind.Snore => Snore(voice, p, rng),
                SoundKind.Boing => Boing(p),
                SoundKind.Splash => Splash(rng),
                SoundKind.Whee => Whee(voice, p, rng),
                SoundKind.Thud => Thud(rng),
                SoundKind.Sniff => Sniff(rng),
                SoundKind.Jingle => Jingle(voice, p),
                SoundKind.Happy => Happy(voice, p, rng),
                SoundKind.Pop => Pop(),
                SoundKind.Whoosh => Whoosh(rng, false),
                SoundKind.Sweep => Whoosh(rng, true),
                _ => Click(),
            };
            Normalize(s, kind is SoundKind.Snore or SoundKind.Click ? 0.5f : 0.9f);
            return s;
        }

        // ------------------------------------------------------------------ voices

        private static float BaseF0(CharacterKind voice) => voice switch
        {
            CharacterKind.Monster => 95f,
            CharacterKind.Animal => 190f,
            _ => 300f, // a child's voice
        };

        // one voiced syllable: f0 glides from f0a to f0b, vowel from va to vb
        private static void Syllable(List<float> output, CharacterKind voice, float f0a, float f0b, int va, int vb,
            float duration, float breath, Random rng)
        {
            var n = (int)(duration * SampleRate);
            var roughness = voice == CharacterKind.Monster ? 0.6f : voice == CharacterKind.Animal ? 0.25f : 0.05f;
            var nasal = voice == CharacterKind.Animal;
            var f1 = new Formant();
            var f2 = new Formant();
            var f3 = new Formant();
            double phase = 0;
            // gentle one-pole low-pass: softens the buzzy source into a rounder voice
            var soften = 1f - MathF.Exp(-2f * MathF.PI * (voice == CharacterKind.Monster ? 1800f : 3000f) / SampleRate);
            var smooth = 0f;
            for (var i = 0; i < n; i++)
            {
                var t = i / (float)n;
                var f0 = f0a + (f0b - f0a) * t;
                // slight natural vibrato
                f0 *= 1f + 0.015f * MathF.Sin(i * 2f * MathF.PI * 5.5f / SampleRate);
                phase += f0 / SampleRate;
                var ph = (float)(phase - Math.Floor(phase));
                // glottal-ish pulse: sawtooth softened
                var src = 1f - 2f * ph;
                src = src * (1f - roughness) + roughness * ((float)rng.NextDouble() * 2f - 1f) * (0.5f + 0.5f * MathF.Sin(ph * MathF.PI * 2f));
                src = src * (1f - breath) + breath * ((float)rng.NextDouble() * 2f - 1f);

                var (a1, a2) = Vowels[va];
                var (b1, b2) = Vowels[vb];
                var fa = a1 + (b1 - a1) * t;
                var fb = a2 + (b2 - a2) * t;
                // monsters have long throats (lower formants), animals are nasal
                if (voice == CharacterKind.Monster) { fa *= 0.75f; fb *= 0.75f; }
                var y = f1.Process(src, fa, 6f) * 1.0f + f2.Process(src, fb, 8f) * 0.6f;
                if (nasal) y += f3.Process(src, 250f, 4f) * 0.8f;

                // attack/release
                smooth += soften * (y - smooth);
                var env = MathF.Min(1f, t * 12f) * MathF.Min(1f, (1f - t) * 6f);
                output.Add(smooth * env);
            }
        }

        private static float Pitch(CharacterKind voice, float pitch) => BaseF0(voice) * pitch;

        private static void Silence(List<float> output, float seconds)
        {
            var n = (int)(seconds * SampleRate);
            for (var i = 0; i < n; i++) output.Add(0f);
        }

        private static float[] Babble(CharacterKind voice, float pitch, Random rng)
        {
            var o = new List<float>();
            var f0 = Pitch(voice, pitch);
            var syllables = voice == CharacterKind.Animal ? 2 : rng.Next(3, 6);
            for (var s = 0; s < syllables; s++)
            {
                var a = f0 * (0.9f + 0.3f * (float)rng.NextDouble());
                var b = a * (0.85f + 0.3f * (float)rng.NextDouble());
                Syllable(o, voice, a, b, rng.Next(5), rng.Next(5), 0.09f + 0.07f * (float)rng.NextDouble(), 0.05f, rng);
                Silence(o, 0.03f + 0.04f * (float)rng.NextDouble());
            }
            return o.ToArray();
        }

        private static float[] Hello(CharacterKind voice, float pitch, Random rng)
        {
            var o = new List<float>();
            var f0 = Pitch(voice, pitch);
            if (voice == CharacterKind.Animal) return Happy(voice, pitch, rng);
            // "hi-yaa": rising then falling
            Syllable(o, voice, f0 * 1.0f, f0 * 1.25f, 1, 2, 0.14f, 0.15f, rng);
            Silence(o, 0.04f);
            Syllable(o, voice, f0 * 1.3f, f0 * 1.0f, 2, 0, 0.26f, 0.05f, rng);
            return o.ToArray();
        }

        private static float[] Surprise(CharacterKind voice, float pitch, Random rng)
        {
            var o = new List<float>();
            var f0 = Pitch(voice, pitch);
            if (voice == CharacterKind.Monster)
                Syllable(o, voice, f0 * 1.2f, f0 * 1.6f, 0, 3, 0.3f, 0.1f, rng); // "huh?"
            else
                Syllable(o, voice, f0 * 1.4f, f0 * 2.1f, 1, 2, 0.22f, 0.05f, rng); // "eek!"
            return o.ToArray();
        }

        private static float[] Scare(CharacterKind voice, float pitch, Random rng)
        {
            var o = new List<float>();
            var f0 = Pitch(voice, pitch);
            if (voice == CharacterKind.Monster)
            {
                // RAAAR: growl swelling, then falling
                Syllable(o, voice, f0 * 0.9f, f0 * 1.3f, 3, 0, 0.35f, 0.25f, rng);
                Syllable(o, voice, f0 * 1.3f, f0 * 0.8f, 0, 0, 0.55f, 0.3f, rng);
                // extra rumble: amplitude modulation
                for (var i = 0; i < o.Count; i++) o[i] *= 0.7f + 0.3f * MathF.Sin(i * 2f * MathF.PI * 28f / SampleRate);
            }
            else
            {
                Syllable(o, voice, f0 * 0.9f, f0 * 0.8f, 4, 4, 0.45f, 0.2f, rng); // "boo!"
            }
            return o.ToArray();
        }

        private static float[] Yawn(CharacterKind voice, float pitch, Random rng)
        {
            var o = new List<float>();
            var f0 = Pitch(voice, pitch);
            Syllable(o, voice, f0 * 1.2f, f0 * 0.7f, 0, 3, 1.1f, 0.45f, rng);
            return o.ToArray();
        }

        private static float[] Snore(CharacterKind voice, float pitch, Random rng)
        {
            // breathing in with a low rattle, then a soft sigh out
            var n = (int)(1.4f * SampleRate);
            var o = new float[n];
            var f = new Formant();
            var rattle = voice == CharacterKind.Monster ? 45f : voice == CharacterKind.Animal ? 70f : 60f;
            for (var i = 0; i < n; i++)
            {
                var t = i / (float)n;
                var noise = (float)rng.NextDouble() * 2f - 1f;
                var am = 0.5f + 0.5f * MathF.Sin(i * 2f * MathF.PI * rattle * pitch / SampleRate);
                var y = f.Process(noise * (t < 0.55f ? am : 0.4f), t < 0.55f ? 500f : 900f, 2f);
                var env = t < 0.55f ? MathF.Sin(t / 0.55f * MathF.PI) : 0.35f * MathF.Sin((t - 0.55f) / 0.45f * MathF.PI);
                o[i] = y * env;
            }
            return o;
        }

        private static float[] Whee(CharacterKind voice, float pitch, Random rng)
        {
            var o = new List<float>();
            var f0 = Pitch(voice, pitch);
            if (voice == CharacterKind.Animal) Syllable(o, voice, f0 * 1.5f, f0 * 2.4f, 2, 2, 0.35f, 0.1f, rng); // squeal
            else Syllable(o, voice, f0 * 1.1f, f0 * 1.8f, 2, 2, 0.55f, 0.05f, rng);
            return o.ToArray();
        }

        private static float[] Happy(CharacterKind voice, float pitch, Random rng)
        {
            var o = new List<float>();
            var f0 = Pitch(voice, pitch);
            if (voice == CharacterKind.Animal)
            {
                // oink oink: nasal, pitch dipping
                Syllable(o, voice, f0 * 1.1f, f0 * 0.8f, 3, 4, 0.16f, 0.2f, rng);
                Silence(o, 0.06f);
                Syllable(o, voice, f0 * 1.15f, f0 * 0.85f, 3, 4, 0.18f, 0.2f, rng);
            }
            else
            {
                // "yay!" / giggle: two quick rising syllables
                Syllable(o, voice, f0 * 1.2f, f0 * 1.5f, 1, 0, 0.12f, 0.1f, rng);
                Silence(o, 0.03f);
                Syllable(o, voice, f0 * 1.3f, f0 * 1.7f, 1, 0, 0.14f, 0.1f, rng);
            }
            return o.ToArray();
        }

        // ------------------------------------------------------------------ effects

        private static float[] Boing(float pitch)
        {
            var n = (int)(0.45f * SampleRate);
            var o = new float[n];
            double phase = 0;
            for (var i = 0; i < n; i++)
            {
                var t = i / (float)n;
                var f = (160f + 420f * MathF.Min(1f, t * 3f)) * pitch * (1f + 0.12f * MathF.Sin(t * 70f) * (1f - t));
                phase += f / SampleRate;
                o[i] = MathF.Sin((float)(phase * 2 * Math.PI)) * MathF.Exp(-t * 4f) * MathF.Min(1f, t * 60f);
            }
            return o;
        }

        private static float[] Splash(Random rng)
        {
            var n = (int)(0.6f * SampleRate);
            var o = new float[n];
            var f = new Formant();
            for (var i = 0; i < n; i++)
            {
                var t = i / (float)n;
                var noise = (float)rng.NextDouble() * 2f - 1f;
                o[i] = f.Process(noise, 2200f - 1200f * t, 1.2f) * MathF.Exp(-t * 6f) * MathF.Min(1f, t * 80f);
            }
            // a few bubbles
            for (var b = 0; b < 5; b++)
            {
                var start = (int)((0.15f + 0.4f * (float)rng.NextDouble()) * SampleRate);
                var freq = 500f + 700f * (float)rng.NextDouble();
                var len = (int)(0.04f * SampleRate);
                for (var i = 0; i < len && start + i < n; i++)
                {
                    var t = i / (float)len;
                    o[start + i] += 0.3f * MathF.Sin(2f * MathF.PI * freq * (1f + t) * i / SampleRate) * (1f - t);
                }
            }
            return o;
        }

        private static float[] Thud(Random rng)
        {
            var n = (int)(0.25f * SampleRate);
            var o = new float[n];
            double phase = 0;
            for (var i = 0; i < n; i++)
            {
                var t = i / (float)n;
                phase += (95f - 50f * t) / SampleRate;
                o[i] = (MathF.Sin((float)(phase * 2 * Math.PI)) + 0.3f * ((float)rng.NextDouble() * 2f - 1f) * MathF.Exp(-t * 30f)) * MathF.Exp(-t * 9f);
            }
            return o;
        }

        private static float[] Sniff(Random rng)
        {
            var o = new List<float>();
            var f = new Formant();
            for (var k = 0; k < 2; k++)
            {
                var n = (int)(0.11f * SampleRate);
                for (var i = 0; i < n; i++)
                {
                    var t = i / (float)n;
                    o.Add(f.Process((float)rng.NextDouble() * 2f - 1f, 3500f, 1.5f) * MathF.Sin(t * MathF.PI));
                }
                Silence(o, 0.06f);
            }
            return o.ToArray();
        }

        private static float[] Jingle(CharacterKind voice, float pitch)
        {
            // a little arpeggio; monsters get a lower, minor-ish one
            var notes = voice == CharacterKind.Monster ? new[] { 0, 3, 7, 12 } : new[] { 0, 4, 7, 12 };
            var root = (voice == CharacterKind.Monster ? 220f : 523f) * MathF.Pow(pitch, 0.5f);
            var noteLength = (int)(0.11f * SampleRate);
            var o = new float[noteLength * notes.Length + (int)(0.25f * SampleRate)];
            for (var k = 0; k < notes.Length; k++)
            {
                var f = root * MathF.Pow(2f, notes[k] / 12f);
                var len = k == notes.Length - 1 ? noteLength + (int)(0.25f * SampleRate) : noteLength;
                for (var i = 0; i < len; i++)
                {
                    var t = i / (float)len;
                    var ph = f * i / SampleRate;
                    var tri = 1f - 4f * MathF.Abs(ph - MathF.Floor(ph + 0.5f)); // triangle wave
                    o[k * noteLength + i] += tri * MathF.Exp(-t * 3f) * MathF.Min(1f, t * 200f);
                }
            }
            return o;
        }

        private static float[] Pop()
        {
            var n = (int)(0.1f * SampleRate);
            var o = new float[n];
            double phase = 0;
            for (var i = 0; i < n; i++)
            {
                var t = i / (float)n;
                phase += (380f + 700f * t) / SampleRate;
                o[i] = MathF.Sin((float)(phase * 2 * Math.PI)) * MathF.Sin(t * MathF.PI);
            }
            return o;
        }

        private static float[] Whoosh(Random rng, bool downward)
        {
            var n = (int)(0.7f * SampleRate);
            var o = new float[n];
            var f = new Formant();
            for (var i = 0; i < n; i++)
            {
                var t = i / (float)n;
                var cutoff = downward ? 3000f - 2600f * t : 400f + 2600f * MathF.Sin(t * MathF.PI);
                o[i] = f.Process((float)rng.NextDouble() * 2f - 1f, cutoff, 1.5f) * MathF.Sin(t * MathF.PI);
            }
            return o;
        }

        private static float[] Click()
        {
            var n = (int)(0.035f * SampleRate);
            var o = new float[n];
            for (var i = 0; i < n; i++)
            {
                var t = i / (float)n;
                o[i] = MathF.Sin(2f * MathF.PI * 1500f * i / SampleRate) * (1f - t) * (1f - t);
            }
            return o;
        }

        private static void Normalize(float[] s, float peak)
        {
            var max = 1e-6f;
            foreach (var v in s) max = Math.Max(max, Math.Abs(v));
            var gain = peak / max;
            for (var i = 0; i < s.Length; i++) s[i] *= gain;
            // 5 ms fades so nothing clicks
            var fade = Math.Min(s.Length / 2, SampleRate / 200);
            for (var i = 0; i < fade; i++)
            {
                var g = i / (float)fade;
                s[i] *= g;
                s[s.Length - 1 - i] *= g;
            }
        }

        // resonant band-pass (RBJ biquad), coefficients updated per sample for gliding formants
        private sealed class Formant
        {
            private float x1, x2, y1, y2;

            public float Process(float x, float frequency, float q)
            {
                frequency = Math.Min(frequency, SampleRate * 0.45f);
                var w = 2f * MathF.PI * frequency / SampleRate;
                var alpha = MathF.Sin(w) / (2f * q);
                var a0 = 1f + alpha;
                var b0 = alpha / a0;
                var b2 = -alpha / a0;
                var a1 = -2f * MathF.Cos(w) / a0;
                var a2 = (1f - alpha) / a0;
                var y = b0 * x + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x;
                y2 = y1; y1 = y;
                return y;
            }
        }
    }
}
