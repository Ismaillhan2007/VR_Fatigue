using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace App.Relaxation.Editor
{
    /// <summary>
    /// Synthesises four seamless ambient loops (wind, birds, water, pad) so ForestDemo has sound without third-party
    /// audio. They are original, license-free placeholders: replace the .wav files with better recordings later and keep
    /// the file names. Output: Assets/App/Forest/Audio/*.wav (mono 22.05 kHz, 12 s, imported as Vorbis).
    /// </summary>
    public static class ForestAudioSynth
    {
        const int Rate = 22050;
        const int Seconds = 12;
        const int Length = Rate * Seconds;
        const int Fade = Rate; // 1 s cross-fade makes the loop seamless

        public static AudioClip Wind(string dir) => Write(dir, "ForestWind", MakeWind());
        public static AudioClip Birds(string dir) => Write(dir, "ForestBirds", MakeBirds());
        public static AudioClip Water(string dir) => Write(dir, "ForestWater", MakeWater());
        public static AudioClip Pad(string dir) => Write(dir, "ForestPad", MakePad());

        static float[] MakeWind()
        {
            var rng = new System.Random(11);
            var x = new float[Length + Fade];
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < x.Length; i++)
            {
                float n = (float)(rng.NextDouble() * 2 - 1);
                lp += 0.035f * (n - lp);          // ~120 Hz low-pass
                lp2 += 0.12f * (lp - lp2);
                float t = i / (float)Rate;
                float swell = 0.55f + 0.25f * Mathf.Sin(2 * Mathf.PI * t / 6f) + 0.2f * Mathf.Sin(2 * Mathf.PI * t / 4f + 1f);
                x[i] = lp2 * swell;
            }
            return Normalise(Loop(x), 0.7f);
        }

        static float[] MakeWater()
        {
            var rng = new System.Random(23);
            var x = new float[Length + Fade];
            float a = 0f, b = 0f, mod = 0f;
            for (int i = 0; i < x.Length; i++)
            {
                float n = (float)(rng.NextDouble() * 2 - 1);
                a += 0.45f * (n - a);             // high cut ~3.5 kHz
                b += 0.06f * (n - b);             // low cut ~ 0.6 kHz
                mod += 0.002f * ((float)(rng.NextDouble() * 2 - 1) - mod);
                x[i] = (a - b) * (0.7f + 6f * mod);
            }
            return Normalise(Loop(x), 0.6f);
        }

        static float[] MakeBirds()
        {
            var rng = new System.Random(37);
            var x = new float[Length + Fade];
            float t = 0.6f;
            while (t < Seconds - 1.2f)
            {
                float f0 = 2400f + (float)rng.NextDouble() * 1800f;
                int notes = 2 + rng.Next(4);
                float nt = t;
                for (int k = 0; k < notes; k++)
                {
                    float dur = 0.05f + (float)rng.NextDouble() * 0.05f;
                    float f = f0 * (1f + 0.12f * k);
                    int start = (int)(nt * Rate), len = (int)(dur * Rate);
                    double phase = 0;
                    for (int i = 0; i < len && start + i < x.Length; i++)
                    {
                        float u = i / (float)len;
                        float env = Mathf.Sin(Mathf.PI * u);
                        float freq = f * (1.2f - 0.4f * u);   // falling chirp
                        phase += 2 * Math.PI * freq / Rate;
                        x[start + i] += (float)Math.Sin(phase) * env * env * 0.5f;
                    }
                    nt += dur + 0.06f;
                }
                t = nt + 0.8f + (float)rng.NextDouble() * 2.4f;
            }
            return Normalise(Loop(x), 0.35f); // birds stay quiet; the mixer layer sets the rest
        }

        static float[] MakePad()
        {
            // Frequencies are snapped to whole cycles per loop so the pad needs no cross-fade.
            float[] freqs = { 110f, 164.81f, 220f, 277.18f };
            var x = new float[Length];
            for (int i = 0; i < Length; i++)
            {
                float t = i / (float)Rate;
                float s = 0f;
                for (int k = 0; k < freqs.Length; k++)
                {
                    float f = Mathf.Round(freqs[k] * Seconds) / Seconds;
                    float trem = 0.75f + 0.25f * Mathf.Sin(2 * Mathf.PI * (k + 1) * t / Seconds);
                    s += Mathf.Sin(2 * Mathf.PI * f * t) * trem / (1f + k * 0.6f);
                }
                x[i] = s;
            }
            return Normalise(x, 0.5f);
        }

        // Crossfade the tail into the head so the loop point is continuous.
        static float[] Loop(float[] x)
        {
            var o = new float[Length];
            for (int i = 0; i < Length; i++)
            {
                if (i < Fade)
                {
                    float w = i / (float)Fade;
                    o[i] = x[i] * w + x[Length + i] * (1f - w);
                }
                else o[i] = x[i];
            }
            return o;
        }

        static float[] Normalise(float[] x, float peak)
        {
            float max = 1e-6f;
            foreach (var v in x) max = Mathf.Max(max, Mathf.Abs(v));
            float g = peak / max;
            for (int i = 0; i < x.Length; i++) x[i] *= g;
            return x;
        }

        static AudioClip Write(string dir, string name, float[] samples)
        {
            Directory.CreateDirectory(dir);
            var path = dir + "/" + name + ".wav";
            using (var fs = new FileStream(path, FileMode.Create))
            using (var w = new BinaryWriter(fs))
            {
                int bytes = samples.Length * 2;
                w.Write(new[] { 'R', 'I', 'F', 'F' });
                w.Write(36 + bytes);
                w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                w.Write(16); w.Write((short)1); w.Write((short)1);
                w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(new[] { 'd', 'a', 't', 'a' });
                w.Write(bytes);
                foreach (var s in samples) w.Write((short)(Mathf.Clamp(s, -1f, 1f) * 32767f));
            }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (AudioImporter)AssetImporter.GetAtPath(path);
            importer.forceToMono = true;
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.CompressedInMemory;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.6f;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }
    }
}
