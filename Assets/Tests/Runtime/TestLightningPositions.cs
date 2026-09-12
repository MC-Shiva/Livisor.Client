using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

/// <summary>Play ModeでRunを呼び、Demoの座標変換・移動追従・同時発火を確認する。</summary>
public static class TestLightningPositions
{
    public static Vector3 Ground(LightningBolt bolt) => (Vector3)typeof(LightningBolt)
        .GetField("_ground", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(bolt);

    public static string[] Run(DemoSceneController demo)
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Play Modeで実行してください。");
        var checks = new List<string>();
        void Check(bool success, string name)
        {
            if (!success) throw new InvalidOperationException(name);
            checks.Add(name);
        }
        bool Near(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < 0.001f;
        void Reject(Action action, string name)
        {
            try { action(); }
            catch (ArgumentException) { checks.Add(name); return; }
            throw new InvalidOperationException(name);
        }

        var director = demo.GetComponent<StageDirector>();
        var origin = demo.lightningOrigin;
        var originalPosition = origin.position;
        var originalRotation = origin.rotation;
        var originalScale = origin.localScale;
        var hips = director.PerformerHips;
        var originalHips = hips.position;
        var effects = new EffectDispatcher(director);
        try
        {
            Check(Near(demo.ResolveLightningPosition(new(0, "audience")), demo.lightningAudiencePoint.position), "audienceは固定点を使う");
            Check(Near(demo.ResolveLightningPosition(new(0, "stage")), demo.lightningStagePoint.position), "stageは固定点を使う");
            hips.position += new Vector3(2, 1, -3);
            var floor = new Plane(origin.up, origin.position);
            Check(Near(demo.ResolveLightningPosition(new(0, "unity-chan")), floor.ClosestPointOnPlane(hips.position)),
                "unity-chanの移動先の床へ追従する");

            origin.SetPositionAndRotation(new Vector3(10, 2, 20), Quaternion.Euler(0, 90, 0));
            origin.localScale = Vector3.one * 2;
            var first = demo.ResolveLightningPosition(new(0, 1, 3, 4));
            var second = demo.ResolveLightningPosition(new(0, -1, 0, 2));
            Check(Near(first, new Vector3(14, 5, 19)), "座標はステージ基準のmで、原点の移動と回転を反映する");
            Reject(() => new DemoLightningSchedule.Cue(0, (string)null), "位置なしを受け付けない");
            Reject(() => new DemoLightningSchedule.Cue(0, "unknown"), "未知の対象を受け付けない");
            Reject(() => new DemoLightningSchedule.Cue(-1, "stage"), "負の時刻を受け付けない");
            Reject(() => new DemoLightningSchedule.Cue(0, float.NaN, 0, 0), "NaN座標を受け付けない");

            effects.FireLightning(first);
            effects.FireLightning(second);
            var active = UnityEngine.Object.FindObjectsByType<LightningBolt>(FindObjectsSortMode.None)
                .Where(bolt => bolt.IsPlaying).Select(Ground).ToArray();
            Check(active.Any(point => Near(point, new Vector3(14, 5, 19)))
                && active.Any(point => Near(point, new Vector3(12, 2, 21))), "同時発火した雷が両方の座標を保持する");
            return checks.ToArray();
        }
        finally
        {
            origin.SetPositionAndRotation(originalPosition, originalRotation);
            origin.localScale = originalScale;
            hips.position = originalHips;
        }
    }
}
