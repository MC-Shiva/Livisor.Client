using System.Collections.Generic;
using System.Linq;
using Livisor.Shared.Common;
using Livisor.Shared.DTO;

/// <summary>
/// 管理画面の行データ（<see cref="TimelineRowModel"/>）を検証し、<see cref="TimelineAction"/>[] へ変換する。
/// UnityEngine に依存しない純粋な変換・検証ロジック。
/// </summary>
public static class TimelineDraft
{
    /// <summary>
    /// 行データを検証し、TimelineAction[] を組み立てる。
    /// 成功した場合は時刻の昇順に整列済みの配列を返す。失敗した場合は error に行番号付きの理由を入れる。
    /// </summary>
    public static bool TryBuild(IReadOnlyList<TimelineRowModel> rows, out TimelineAction[] actions, out string error)
    {
        actions = null;

        if (rows == null || rows.Count == 0)
        {
            error = "タイムラインが空です。行を追加してください。";
            return false;
        }

        var built = new List<(int centiseconds, TimelineAction action)>(rows.Count);

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowNumber = i + 1;

            // 各欄は入力時に 0-23 / 0-59 / 0-59 / 0-99 へクランプ済みのため、
            // ここでの TryParse は形式チェックの保険。
            var raw = $"{row.Hours:D2}:{row.Minutes:D2}:{row.Seconds:D2}:{row.Centiseconds:D2}";
            if (!PlaybackTime.TryParse(raw, out var time))
            {
                error = $"{rowNumber} 行目: 時刻の形式が不正です（HH:mm:ss:ff で入力してください）。";
                return false;
            }

            if (!TryBuildValue(row, rowNumber, out var value, out error))
                return false;

            var action = new TimelineAction
            {
                Time = time.ToRawString(),
                Action = row.Action,
                Value = value,
            };

            built.Add((time.TotalCentiseconds, action));
        }

        actions = built
            .OrderBy(entry => entry.centiseconds)
            .Select(entry => entry.action)
            .ToArray();
        error = null;
        return true;
    }

    /// <summary>
    /// 配信停止用の単発タイムラインを組む。play: false を単独で配信することで、
    /// Server 側の「Broadcast は今の演目を丸ごと差し替える」セマンティクスに乗せ、
    /// 受信側で進行中のスケジュールを破棄させたうえで停止させる。
    /// </summary>
    public static TimelineAction[] BuildStopActions() => new[]
    {
        new TimelineAction
        {
            Time = "00:00:00:00",
            Action = ActionType.Play,
            Value = ActionValue.From(false),
        },
    };

    private static bool TryBuildValue(TimelineRowModel row, int rowNumber, out ActionValue value, out string error)
    {
        value = default;
        error = null;

        switch (ActionValueKindMap.KindOf(row.Action))
        {
            case ActionValueKind.Number:
                if (!int.TryParse(row.NumberText, out var number))
                {
                    error = $"{rowNumber} 行目: 値は整数で入力してください。";
                    return false;
                }

                value = ActionValue.From(number);
                return true;

            case ActionValueKind.Bool:
                value = ActionValue.From(row.BoolValue);
                return true;

            case ActionValueKind.Text:
                value = ActionValue.From(row.TextValue);
                return true;

            default:
                error = $"{rowNumber} 行目: 未対応の値の種類です。";
                return false;
        }
    }
}
