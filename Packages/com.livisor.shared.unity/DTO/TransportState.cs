using MessagePack;

namespace Livisor.Shared.DTO
{
    /// <summary>
    /// 再生トランスポートの現在値。
    ///
    /// 目的は、サーバーとクライアントが同じ瞬間に同じ処理をすることである。
    /// そのために時計を合わせるのではなく、受け取った瞬間を基準にする方式を採る
    /// （2026-08-29 の決定 / Issue #17 のコメント）。
    ///
    ///   発火までの待ち時間 = 相対時間 - (ServerTimeMs - StartedAtServerMs)
    ///
    /// クライアントはこの状態を受け取った瞬間から、この時間だけ待って発火する。
    /// 括弧内は「送信した時点で既に経過していた再生位置」である。再生開始と同時に届いた場合は
    /// ほぼ 0 になり、待ち時間は相対時間そのものになる。再生中に予約が追加された場合は、
    /// 経過したぶんが引かれる。
    ///
    /// この式にはサーバーとクライアントの時計を比べる箇所が無い。そのため両者の時計がズレていても
    /// 結果に影響しない。代わりに片道の通信遅延は残り、その分だけ遅れて発火する。
    /// クライアント間のずれは、それぞれの片道遅延の差になる。
    ///
    /// この式は <c>Playing</c> が true のときだけ成立する。false のときは基準時刻が無い。
    /// 停止中も <c>ScheduledAction</c> は残る（再生し直せば同じ相対位置で発火する）ので、
    /// 受信側は Playing が false になった時点で予約タイマーを取り消し、
    /// 次に Playing が true になったときに張り直す。
    /// </summary>
    [MessagePackObject]
    public class TransportState
    {
        /// <summary>再生中かどうか。</summary>
        [Key(0)]
        public bool Playing { get; set; }

        /// <summary>
        /// 再生を開始したサーバー時刻（UTC ミリ秒）。停止中は 0（基準を持たないという意味）。
        /// 単体では使わず、<c>ServerTimeMs</c> との差＝送信時点で経過していた再生位置として使う。
        /// </summary>
        [Key(1)]
        public long StartedAtServerMs { get; set; }

        /// <summary>
        /// この状態を確定した時点のサーバー時刻（UTC ミリ秒）。送信時刻にあたる。
        /// <c>StartedAtServerMs</c> との差が、送信時点で経過していた再生位置になる。
        /// クライアント自身の時計とは比べない。比べると時計のズレが結果に入り込む。
        /// </summary>
        [Key(2)]
        public long ServerTimeMs { get; set; }

        /// <summary>
        /// 予約中のアクション。最大 1 件で、無ければ null。
        /// <c>Time</c> は絶対時刻ではなく、再生開始からの相対時間を表す。
        /// クライアントは DefaultActions の間へ時刻順に差し込み、同時刻なら予約を後に実行する。
        /// 過去の時刻の予約は受信後に 1 回実行し、再配信だけでは実行し直さない。
        /// 取り消しはこの項目だけを null にし、DefaultActions は変えない。
        /// </summary>
        [Key(3)]
        public TimelineAction? ScheduledAction { get; set; }

        /// <summary>
        /// Live / Demo 共通のデフォルト演出。サーバーは起動時に DefaultActionSet を検証し、配信ごとに新しい DTO を作る。
        /// Time は曲の先頭からの位置。クライアントは音源の再生位置で発火し、一時停止中は進めない。
        /// 再配信では実行済みのデフォルト演出を繰り返さない。予約との区別を保つため、別の配列として送る。
        /// この項目が無い旧形式では初期値の空配列になる。受信側は null も空として扱う。
        /// </summary>
        [Key(4)]
        public TimelineAction[] DefaultActions { get; set; } = new TimelineAction[0];
    }
}
