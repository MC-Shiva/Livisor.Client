using System;
using System.Collections;
using UnityEngine;

namespace Livisor.MRDive
{
    /// <summary>
    /// ダイブ演出 1 パターンの基底。
    ///
    /// 実装側は <see cref="Run"/> の中で <see cref="Sweep"/> を回し、0→1 の進行度から
    /// 見た目を組み立てる。フェーズ分割は <see cref="Span"/> を使うと読みやすい。
    ///
    ///   yield return Sweep(t =>
    ///   {
    ///       float open = Span(t, 0.0f, 0.4f);   // 前半で開く
    ///       float pull = Span(t, 0.4f, 1.0f);   // 後半で引き込む
    ///       ...
    ///   });
    /// </summary>
    public abstract class DiveTransitionBase : MonoBehaviour
    {
        /// <summary>UI のボタンに出す名前。</summary>
        public abstract string DisplayName { get; }

        /// <summary>UI のボタンに出す一行説明。</summary>
        public abstract string Tagline { get; }

        /// <summary>演出全体の尺（秒）。</summary>
        public abstract float Duration { get; }

        /// <summary>UI のアクセントカラー。演出の主色に合わせる。</summary>
        public abstract Color AccentColor { get; }

        /// <summary>遷移先のシーンで、この色から明けてくる。白閃光で終わるなら白。</summary>
        public virtual Color ArrivalFadeColor => Color.black;

        /// <summary>現在の進行度 0..1。UI やデバッグ表示用。</summary>
        public float Normalized { get; protected set; }

        /// <summary>演出が始まる直前。重いものの生成はここで済ませておく。</summary>
        public virtual void OnPrepare(DiveContext context)
        {
        }

        /// <summary>演出本体。</summary>
        public abstract IEnumerator Run(DiveContext context);

        /// <summary>
        /// 後始末。
        ///
        /// 契約として、以下は <see cref="DiveDirector"/> が必ず面倒を見るので、
        /// ここで重ねてやる必要はない:
        ///   - <c>Overlay.Clear()</c> — このパターンが作ったレイヤーとマテリアルの破棄
        ///   - <c>Passthrough.RestoreDefaults()</c> — 色調整と輪郭線の復帰
        ///   - <c>Passthrough.Opacity = 1</c> — 現実が見えている状態へ戻す
        ///
        /// ここに書くのは「このパターンだけが抱えている状態」の巻き戻しに限る。
        /// なお、シーン遷移した場合はそもそも呼ばれない（シーンごと消えるため）ので、
        /// 必須の処理を置かないこと。
        /// </summary>
        public virtual void OnCleanup(DiveContext context)
        {
        }

        /// <summary><see cref="Duration"/> をかけて 0→1 を回し、毎フレーム step を呼ぶ。</summary>
        protected IEnumerator Sweep(Action<float> step)
        {
            return Sweep(Duration, step);
        }

        /// <summary>指定秒かけて 0→1 を回し、毎フレーム step を呼ぶ。最後は必ず t=1 で 1 回呼ぶ。</summary>
        protected IEnumerator Sweep(float duration, Action<float> step)
        {
            if (step == null) yield break;

            if (duration <= 0f)
            {
                Normalized = 1f;
                step(1f);
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                Normalized = Mathf.Clamp01(elapsed / duration);
                step(Normalized);
                yield return null;
                elapsed += Time.deltaTime;
            }

            Normalized = 1f;
            step(1f);
        }

        /// <summary>t が区間 [a, b] のどこにいるかを 0..1 で返す。<see cref="DiveEase.Span"/> の別名。</summary>
        protected static float Span(float t, float a, float b) => DiveEase.Span(t, a, b);
    }
}
