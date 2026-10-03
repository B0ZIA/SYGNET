using System;
using Sygnet.Core;
using UnityEngine;

namespace Sygnet.App
{
    /// <summary>
    /// „Przekaż dalej” (CLIENT_UNITY.md §4.4): odtwarza dźwiękiem ORYGINALNE bajty ramki, więc podpis zostaje
    /// nienaruszony i telefon sąsiada weryfikuje go sam.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class RelayPlayer : MonoBehaviour
    {
        AudioSource source;
        AudioClip clip;
        bool wasPlaying;

        /// <summary>true na start odtwarzania, false na koniec – nasłuch mikrofonu ma się wtedy wstrzymać.</summary>
        public event Action<bool> PlayingChanged;

        public bool IsPlaying => source != null && source.isPlaying;

        void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0;
            source.volume = 1;
        }

        public float Play(byte[] frame, int repeat = 1)
        {
            int sr = AudioSettings.outputSampleRate;
            var samples = ModemEncoder.Encode(frame, sr, repeat);
            if (clip != null) Destroy(clip);
            clip = AudioClip.Create("sygnet-relay", samples.Length, 1, sr, false);
            clip.SetData(samples, 0);
            source.clip = clip;
            source.Play();
            wasPlaying = true;
            PlayingChanged?.Invoke(true);
            return samples.Length / (float)sr;
        }

        public void Stop()
        {
            if (source.isPlaying) source.Stop();
        }

        void Update()
        {
            if (wasPlaying && !source.isPlaying)
            {
                wasPlaying = false;
                PlayingChanged?.Invoke(false);
            }
        }
    }
}
