using System;
using UnityEngine;

namespace Livisor.Live.Penlights
{
    /// <summary>
    /// ペンライトが音声解析値を取得するための共通インターフェース。
    /// 解析手段をReaktionに固定せず、将来別のFFTやネットワーク値へ交換できるようにする。
    /// </summary>
    public interface IPenlightAudioSource
    {
        /// <summary>現在、解析値を取得できる状態か。</summary>
        bool IsAvailable { get; }

        /// <summary>初期化ログへ表示する音声入力の説明。</summary>
        string Description { get; }

        /// <summary>指定した音量・周波数帯の正規化値（0～1）を返す。</summary>
        float GetLevel(PenlightAudioInput input);
    }

    /// <summary>
    /// MusicPlayerに既存のReaktion周波数解析を、ペンライト用音声入力へ変換するアダプター。
    /// 既存の4帯域を再利用するため、ペンライト専用のFFTは追加実行しない。
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
            // MusicPlayer配下からReaktorを集め、カットオフ周波数の低い順に並べる。
            // この順序をBass → LowMid → HighMid → Trebleとして利用する。
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
                    // OverallVolumeは4帯域のRMS（二乗平均平方根）とする。
                    // 単純平均よりも、どれかの帯域が強く鳴ったときに反応しやすい。
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
            // フィルターが見つからない場合は中央付近の値として扱い、並べ替えを継続する。
            var filter = reaktor != null ? reaktor.GetComponent<Reaktion.BandPassFilter>() : null;
            return filter != null ? filter.cutoff : 0.5f;
        }
    }
}
