using System;

namespace Livisor.Shared.Common
{
    // "HH:mm:ss:ff"（時:分:秒:センチ秒）を表す値オブジェクト。Client / Server 共通の時刻ルール。
    // 生成(Parse/TryParse)時に妥当性を保証するため、これ以降は常に正しい時刻として扱える。
    public readonly struct PlaybackTime
    {
        public int Hours { get; }
        public int Minutes { get; }
        public int Seconds { get; }
        public int Centiseconds { get; }

        private PlaybackTime(int hours, int minutes, int seconds, int centiseconds)
        {
            Hours = hours;
            Minutes = minutes;
            Seconds = seconds;
            Centiseconds = centiseconds;
        }

        // 先頭アクション基準の相対再生などに使う総秒数。
        public double TotalSeconds => Hours * 3600 + Minutes * 60 + Seconds + Centiseconds / 100.0;

        // 整数での順序比較・同値判定に使う総センチ秒。23:59:59:99 で 8,639,999。
        // TotalSeconds(double)は二進で正確に表せない場合があるため、順序判定にはこちらを使う。
        public int TotalCentiseconds => ((Hours * 60 + Minutes) * 60 + Seconds) * 100 + Centiseconds;

        // ワイヤ表記 "HH:mm:ss:ff" へ戻す。
        public string ToRawString() => $"{Hours:D2}:{Minutes:D2}:{Seconds:D2}:{Centiseconds:D2}";

        // "HH:mm:ss:ff" をパースする。不正なら FormatException。
        public static PlaybackTime Parse(string value)
        {
            if (!TryParse(value, out var result))
                throw new FormatException($"invalid time format: '{value}' (expected HH:mm:ss:ff).");

            return result;
        }

        // "HH:mm:ss:ff" のパースを試みる。不正なら false。
        public static bool TryParse(string value, out PlaybackTime result)
        {
            result = default;

            if (string.IsNullOrEmpty(value))
                return false;

            var parts = value.Split(':');
            if (parts.Length != 4
                || !int.TryParse(parts[0], out var h) || h is < 0 or > 23
                || !int.TryParse(parts[1], out var m) || m is < 0 or > 59
                || !int.TryParse(parts[2], out var s) || s is < 0 or > 59
                || !int.TryParse(parts[3], out var ff) || ff is < 0 or > 99)
            {
                return false;
            }

            result = new PlaybackTime(h, m, s, ff);
            return true;
        }
    }
}
