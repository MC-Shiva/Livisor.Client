using System;
using UnityEngine;

namespace Livisor.Live.Penlights
{
    public interface IPenlightAudioSource
    {
        bool IsAvailable { get; }
        string Description { get; }
        float GetLevel(PenlightAudioInput input);
    }

    /// <summary>
    /// Adapts the four Reaktion frequency analysers already present in MusicPlayer
    /// to the penlight audio input interface. No additional FFT is performed.
    /// </summary>
    public sealed class ReaktionPenlightAudioSource : IPenlightAudioSource
    {
        readonly Reaktion.Reaktor[] _bands;

        public bool IsAvailable => _bands.Length > 0;
        public string Description => IsAvailable
            ? $"Reaktion ({_bands.Length} bands)"
            : "Reaktion (no bands)";

        public ReaktionPenlightAudioSource(GameObject musicPlayer)
        {
            _bands = musicPlayer != null
                ? musicPlayer.GetComponentsInChildren<Reaktion.Reaktor>(true)
                : Array.Empty<Reaktion.Reaktor>();

            Array.Sort(_bands, CompareCutoffFrequency);
        }

        public float GetLevel(PenlightAudioInput input)
        {
            if (_bands.Length == 0)
                return 0.0f;

            switch (input)
            {
                case PenlightAudioInput.Bass:
                    return GetBand(0);
                case PenlightAudioInput.LowMid:
                    return GetBand(Mathf.Min(1, _bands.Length - 1));
                case PenlightAudioInput.HighMid:
                    return GetBand(Mathf.Min(2, _bands.Length - 1));
                case PenlightAudioInput.Treble:
                    return GetBand(_bands.Length - 1);
                default:
                    var squareSum = 0.0f;
                    for (var i = 0; i < _bands.Length; i++)
                    {
                        var level = Mathf.Clamp01(_bands[i].Output);
                        squareSum += level * level;
                    }

                    return Mathf.Sqrt(squareSum / _bands.Length);
            }
        }

        float GetBand(int index)
        {
            return Mathf.Clamp01(_bands[index].Output);
        }

        static int CompareCutoffFrequency(Reaktion.Reaktor left, Reaktion.Reaktor right)
        {
            return GetCutoff(left).CompareTo(GetCutoff(right));
        }

        static float GetCutoff(Reaktion.Reaktor reaktor)
        {
            var filter = reaktor != null ? reaktor.GetComponent<Reaktion.BandPassFilter>() : null;
            return filter != null ? filter.cutoff : 0.5f;
        }
    }
}
