using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 雷の発火と落下位置を決める司令塔。空の GameObject にアタッチして使う。
/// <see cref="LightningBolt"/> を <see cref="_poolSize"/> 本だけ作って使い回すため、連発しても生成コストがかからない。
/// トリガーは <see cref="Strike"/> を呼ぶだけなので、将来タイムライン（<see cref="IMediaPlayer"/>）から
/// 発火させる場合もこのクラスは変更しなくてよい。
/// </summary>
[DisallowMultipleComponent]
public class LightningStrikeController : MonoBehaviour
{
    [Header("落下位置")]
    [Tooltip("落下地点の基準。未設定ならこの GameObject の位置を使う。")]
    [SerializeField] private Transform _target;

    [Tooltip("オンにすると基準点まわりのランダムな位置に落とす。")]
    [SerializeField] private bool _useRandomArea;

    [Tooltip("ランダム範囲の大きさ。基準点を中心とした箱の 1 辺。")]
    [SerializeField] private Vector3 _randomAreaSize = new Vector3(6f, 0f, 6f);

    [Tooltip("着弾点から見た雷の発生高さ。")]
    [SerializeField] private float _startHeight = 12f;

    [Header("発火")]
    [Tooltip("同時に出せる雷の本数。")]
    [SerializeField] private int _poolSize = 4;

    [Tooltip("テスト用の発火キー。Play 中に押すと雷が落ちる。")]
    [SerializeField] private KeyCode _testKey = KeyCode.L;

    [Tooltip("使う雷の Prefab。未設定なら実行時に自動生成する。")]
    [SerializeField] private LightningBolt _boltPrefab;

    private readonly List<LightningBolt> _pool = new List<LightningBolt>();
    private int _nextIndex;

    private void Awake()
    {
        int count = Mathf.Max(_poolSize, 1);
        for (int i = 0; i < count; i++)
            _pool.Add(CreateBolt(i));
    }

    private void Update()
    {
        if (Input.GetKeyDown(_testKey)) Strike();
    }

    /// <summary>設定に従って落下地点を決め、雷を 1 本落とす。</summary>
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

    /// <summary>着弾点を指定して雷を 1 本落とす。</summary>
    public void StrikeAt(Vector3 groundPoint)
    {
        var bolt = Rent();
        if (bolt == null) return;

        bolt.Play(groundPoint + Vector3.up * _startHeight, groundPoint);
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

    // 空いている雷を返す。すべて再生中なら最も古いものを取り上げて撃ち直す。
    private LightningBolt Rent()
    {
        if (_pool.Count == 0) return null;

        foreach (var bolt in _pool)
            if (!bolt.IsPlaying)
                return bolt;

        var reused = _pool[_nextIndex];
        _nextIndex = (_nextIndex + 1) % _pool.Count;
        return reused;
    }

    private LightningBolt CreateBolt(int index)
    {
        LightningBolt bolt;
        if (_boltPrefab != null)
        {
            bolt = Instantiate(_boltPrefab, transform);
        }
        else
        {
            var go = new GameObject($"Lightning Bolt {index}");
            go.transform.SetParent(transform, false);
            bolt = go.AddComponent<LightningBolt>();
        }

        bolt.gameObject.SetActive(false);
        return bolt;
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 basePoint = _target != null ? _target.position : transform.position;

        Gizmos.color = new Color(0.62f, 0.78f, 1f, 1f);
        Gizmos.DrawLine(basePoint + Vector3.up * _startHeight, basePoint);

        if (!_useRandomArea) return;

        Gizmos.color = new Color(0.62f, 0.78f, 1f, 0.5f);
        Gizmos.DrawWireCube(basePoint, new Vector3(_randomAreaSize.x, 0.1f, _randomAreaSize.z));
    }
}
