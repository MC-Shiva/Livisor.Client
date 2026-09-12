using Livisor.Shared.Common;
using UnityEngine;

/// <summary>
/// <see cref="ActionType.Effect"/> の演出名（<see cref="EffectNames"/>）をシーンの演出に対応づけて実行する。
/// DemoSceneController と TimelineReceiver が共用する。時刻の管理や通信は呼び出し側が行う。
/// 演出を増やすときは EffectNames に定数を足し、ここに case を足す。サーバーの変更は要らない。
/// 知らない名前は警告して無視する。
/// </summary>
public sealed class EffectDispatcher
{
    // 雷の落下点。観客席は AudiencePenlightRig.prefab の Arc 配置（基準点から半径 4.5 m 以遠、±60°）なので、
    // その円弧の中から選ぶ。半径は最前列の少し後ろから中ほどまで（観客の頭上に落ちて見える範囲）。
    private const float LightningRadiusMin = 6f;
    private const float LightningRadiusMax = 14f;
    private const float LightningHalfAngleDegrees = 60f;

    private readonly StageDirector _director;
    private LightningVfxController _lightning;

    public EffectDispatcher(StageDirector director)
    {
        _director = director;
        // 雷の VFX は初回に読み込みが走るため、発火の瞬間ではなく開始時に用意しておく。
        ResolveLightning();
    }

    public void Fire(string effectName)
    {
        switch (effectName)
        {
            case EffectNames.ConfettiOn:
                _director.SetConfetti(true);
                break;

            case EffectNames.ConfettiOff:
                _director.SetConfetti(false);
                break;

            case EffectNames.Lightning:
                ResolveLightning().StrikeAt(RandomAudiencePoint());
                break;

            case EffectNames.SilverStreamer:
                _director.FireSilverStreamers();
                break;

            default:
                Debug.LogWarning($"[Effect] 未知の演出名のため無視する: '{effectName}'");
                return;
        }

        Debug.Log($"[Effect] {effectName}");
    }

    // DemoはC#で指定した着弾点を使う。
    public void FireLightning(Vector3 worldPosition)
    {
        ResolveLightning().StrikeAt(worldPosition);
        Debug.Log($"[Effect] {EffectNames.Lightning}");
    }

    // 観客席の円弧の中からランダムに 1 点選ぶ（「観客に落とす」演出案）。基準は StageDirector.audiencePenlightPlacement で、
    // その前方（+Z）が円弧の中央。未設定ならシーン原点を基準にする。
    private Vector3 RandomAudiencePoint()
    {
        var angle = Random.Range(-LightningHalfAngleDegrees, LightningHalfAngleDegrees) * Mathf.Deg2Rad;
        var radius = Random.Range(LightningRadiusMin, LightningRadiusMax);
        var local = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius;
        var placement = _director.audiencePenlightPlacement;
        return placement != null ? placement.TransformPoint(local) : local;
    }

    // 雷は LiveScene / DemoScene に置かれていないため、無ければ観客席の基準点に生成する。
    // 落下点は StrikeAt で毎回渡すので、生成位置自体は VFX の親としての意味しか無い。
    private LightningVfxController ResolveLightning()
    {
        if (_lightning != null)
            return _lightning;

        _lightning = Object.FindFirstObjectByType<LightningVfxController>();
        if (_lightning != null)
            return _lightning;

        var go = new GameObject("Lightning (timeline effect)");
        var placement = _director.audiencePenlightPlacement;
        go.transform.position = placement != null ? placement.position : Vector3.zero;
        _lightning = go.AddComponent<LightningVfxController>();
        return _lightning;
    }
}
