using System;
using MessagePack;

namespace Livisor.Shared.DTO
{
    /// <summary>
    /// room の再生状態と、デフォルト演出・追加予約を時刻順に並べた一覧。
    /// Client は一覧をメモリに読み込み、曲の再生位置に達したアクションを一度ずつ実行する。
    /// </summary>
    [MessagePackObject]
    public class TransportState
    {
        [Key(0)]
        public bool Playing { get; set; }

        /// <summary>再生を開始したサーバー時刻（UTC ミリ秒）。停止中は 0。</summary>
        [Key(1)]
        public long StartedAtServerMs { get; set; }

        /// <summary>
        /// 状態を確定したサーバー時刻（UTC ミリ秒）。音源がない Client は
        /// ServerTimeMs - StartedAtServerMs を受信時点の経過時間として使う。
        /// Client 自身の時計とは比較しない。片道の通信遅延は残る。
        /// </summary>
        [Key(2)]
        public long ServerTimeMs { get; set; }

        /// <summary>
        /// デフォルト演出と追加予約の全件。Time は曲の先頭からの位置。
        /// 停止中も一覧は保持する。CANCEL は追加予約だけを除く。
        /// </summary>
        [Key(3)]
        public TimelineAction[] Actions { get; set; } = Array.Empty<TimelineAction>();
    }
}
