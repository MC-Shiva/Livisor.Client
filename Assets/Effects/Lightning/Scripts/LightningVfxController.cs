using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 下降雷撃を発火させる。描画と着弾の閃光は LightningBolt が管理する。
/// 既存シーンの参照を維持するためクラス名はそのままにしている。
/// </summary>
[DisallowMultipleComponent]
public class LightningVfxController : MonoBehaviour
{
    [Header("描画負荷")]
    [Tooltip("雷本体はVFX Graph。Quest3は着弾を1メッシュで描画。DesktopEnhancedはGPU火花と環境光を追加。")]
    [SerializeField] private LightningBolt.Quality _quality = LightningBolt.Quality.Quest3;
    [Header("雷撃")]
    [SerializeField, Min(0.1f)] private float _startHeight = 12f;
    [Tooltip("柱の束の太さ。各柱に異なる幅を割り当てる。")]
    [SerializeField, Min(0.05f)] private float _strikeWidth = 0.95f;
    [Tooltip("柱が分かれる半径。大きくすると、絡まる柱の間に隙間が見える。")]
    [SerializeField, Min(0f)] private float _bundleRadius = 0.7f;
    [SerializeField, Min(0.01f)] private float _descentDuration = 0.1f;
    [Tooltip("電流の明暗差。上げると外側の電流の暗い隙間が深くなる。")]
    [SerializeField, Range(0f, 1f)] private float _electricContrast = 0.85f;
    [Tooltip("電流模様の更新速度。")]
    [SerializeField, Range(1f, 60f)] private float _arcSpeed = 24f;
    [Tooltip("DesktopEnhanced の追加 Bloom。Quest3 は使わずシェーダーでにじみを描く。")]
    [SerializeField, Range(0f, 1f)] private float _bloomIntensity = 0.18f;
    [Tooltip("枝雷の幅。主軸の太さとは独立。")]
    [SerializeField, Min(0.005f)] private float _branchWidth = 0.018f;
    [Tooltip("着弾から広がる細い放電の幅。")]
    [SerializeField, Min(0.005f)] private float _impactArcWidth = 0.025f;
    [Tooltip("主軸と電流の折れの強さ。太さは変えない。")]
    [SerializeField, Range(0f, 1f)] private float _angularity = 0.8f;
    [Tooltip("複数の柱が前後を交差して絡む強さ。")]
    [SerializeField, Range(0f, 1f)] private float _entanglement = 0.85f;
    [Tooltip("着弾後、主軸全体が光る時間。")]
    [SerializeField, Min(0f)] private float _trunkHold = 0.16f;
    [Tooltip("主軸が上から下へ消える時間。")]
    [SerializeField, Min(0.01f)] private float _trunkErase = 0.18f;
    [Tooltip("着弾から外へ走る地面の放電の時間。")]
    [SerializeField, Min(0.01f)] private float _groundDuration = 0.65f;
    private LightningBolt _strike;
    private readonly List<LightningBolt> _strikes = new();

    [Header("落下位置")]
    [Tooltip("落下地点の基準。未設定ならこの GameObject の位置を使う。")]
    [SerializeField] private Transform _target;

    [Tooltip("オンにすると基準点まわりのランダムな位置に落とす。")]
    [SerializeField] private bool _useRandomArea;

    [Tooltip("ランダム範囲の大きさ。基準点を中心とした箱の 1 辺。")]
    [SerializeField] private Vector3 _randomAreaSize = new Vector3(6f, 0f, 6f);

    [Header("見た目")]
    [Tooltip("内側の芯の色。明るさは LightningBolt 側で管理する。")]
    [ColorUsage(true, true)]
    [SerializeField] private Color _coreColor = new Color(8.5f, 9.6f, 10f, 1f);

    [Tooltip("外側の縁の色。")]
    [ColorUsage(true, true)]
    [SerializeField] private Color _edgeColor = new Color(0.2f, 2.6f, 4f, 1f);

    [Tooltip("着弾時の飛沫と地面の放電が広がる半径。")]
    [SerializeField] private float _boltLength = 4.8f;

    [Header("発火")]
    [SerializeField, Tooltip("RecordScene向け。ライブが一時停止中でも雷を最後まで再生する。")]
    private bool _useUnscaledTime;
    [Tooltip("テスト用の発火キー。Play 中に押すと雷が落ちる。")]
    [SerializeField] private KeyCode _testKey = KeyCode.L;

    private void Awake()
    {
        EnsureStrike();
        _strike.ConfigurePerformance(_quality);
        _strike.Warmup();
    }

    private void EnsureStrike()
    {
        // 同時刻の別位置への雷を上書きしない。再生が終わった雷は再利用する。
        foreach (var strike in _strikes)
            if (!strike.IsPlaying)
            {
                _strike = strike;
                return;
            }
        var go = new GameObject("Descending Lightning");
        go.layer = gameObject.layer;
        go.transform.SetParent(transform, false);
        _strike = go.AddComponent<LightningBolt>();
        _strikes.Add(_strike);
    }

    private void Update()
    {
        if (Input.GetKeyDown(_testKey)) Strike();
    }

    /// <summary>設定に従って落下地点を決め、雷を 1 回落とす。</summary>
    [ContextMenu("テスト発火")]
    public void Strike()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[Lightning] 雷は Play 中だけ落とせる。");
            return;
        }

        StrikeAt(ResolveGroundPoint());
    }

    /// <summary>着弾点を指定して雷を 1 回落とす。</summary>
    public void StrikeAt(Vector3 groundPoint)
    {
        if (!Application.isPlaying) return;
        EnsureStrike();
        _strike.ConfigurePerformance(_quality);
        _strike.ConfigureStrike(_coreColor, _edgeColor, _strikeWidth, _descentDuration, _boltLength);
        _strike.ConfigureElectricity(_electricContrast, _arcSpeed, _bloomIntensity, _branchWidth, _impactArcWidth);
        _strike.ConfigureShape(_angularity, _entanglement);
        _strike.ConfigureBundle(_bundleRadius);
        _strike.ConfigureTiming(_trunkHold, _trunkErase, _groundDuration);
        _strike.UseUnscaledTime = _useUnscaledTime;
        _strike.Play(groundPoint + Vector3.up * Mathf.Max(0.1f, _startHeight), groundPoint);
    }

    private Vector3 ResolveGroundPoint()
    {
        Vector3 basePoint = _target != null ? _target.position : transform.position;
        if (!_useRandomArea) return basePoint;

        return basePoint + new Vector3(
            Random.Range(-_randomAreaSize.x, _randomAreaSize.x) * 0.5f,
            Random.Range(-_randomAreaSize.y, _randomAreaSize.y) * 0.5f,
            Random.Range(-_randomAreaSize.z, _randomAreaSize.z) * 0.5f);
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 basePoint = _target != null ? _target.position : transform.position;

        Gizmos.color = new Color(1f, 0.82f, 0.29f, 1f);
        Gizmos.DrawWireSphere(basePoint, _boltLength);
        Gizmos.DrawLine(basePoint + Vector3.up * _startHeight, basePoint);

        if (!_useRandomArea) return;

        Gizmos.color = new Color(1f, 0.82f, 0.29f, 0.5f);
        Gizmos.DrawWireCube(basePoint, new Vector3(_randomAreaSize.x, 0.1f, _randomAreaSize.z));
    }
}
