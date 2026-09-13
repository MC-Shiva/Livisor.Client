using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

/// <summary>Play ModeでDemoのランダム範囲と、移動した基準点への追従を確認する。</summary>
public static class TestLightningPositions
{
    public static Vector3 Ground(LightningBolt bolt) => (Vector3)typeof(LightningBolt)
        .GetField("_ground", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(bolt);

    public static string[] Run(DemoSceneController demo)
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Play Modeで実行してください。");
        var lightning = demo.GetComponent<LightningVfxController>();
        var origin = demo.lightningStagePoint;
        var originalPosition = origin.position;
        var randomState = UnityEngine.Random.state;
        var checks = new List<string>();
        void Check(bool success, string name)
        {
            if (!success) throw new InvalidOperationException(name);
            checks.Add(name);
        }
        try
        {
            foreach (var x in new[] { -2f, 2f })
            foreach (var z in new[] { -2f, 2f })
            {
                var start = originalPosition + new Vector3(x, 5, z);
                Check(Physics.Raycast(start, Vector3.down, out var hit, 10, 1 << 20)
                    && hit.collider.name == "Visualizer(Clone)", $"範囲の隅 ({x}, {z}) が中央ステージの床にある");
            }
            UnityEngine.Random.InitState(22);
            origin.position = new Vector3(10, 2, 20);
            var points = Enumerable.Range(0, 64).Select(_ => lightning.ResolveGroundPoint()).ToArray();
            Check(points.All(p => p.x >= 8 && p.x <= 12 && p.z >= 18 && p.z <= 22),
                "雷はステージ中心の4m四方に収まる");
            Check(points.All(p => Mathf.Abs(p.y - 2) < 0.001f), "雷の高さはステージ中心と同じ");
            Check(points.Distinct().Count() > 1, "押すたびに異なる位置を選べる");
            return checks.ToArray();
        }
        finally
        {
            origin.position = originalPosition;
            UnityEngine.Random.state = randomState;
        }
    }
}
