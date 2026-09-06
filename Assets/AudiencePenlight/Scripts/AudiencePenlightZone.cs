using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Livisor.Live.Penlights
{
    /// <summary>
    /// 1つの観客エリアについて、ペンライトを置く座席座標を生成するコンポーネント。
    /// Grid（直線配置）とArc（ステージを囲む円弧配置）に対応する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AudiencePenlightZone : MonoBehaviour
    {
        // ブロック数、座席数、間隔、高さ、円弧半径などの配置設定。
        [SerializeField]
        PenlightLayoutSettings _layout = default;

        // 複数Zoneを使う場合に、Zone単位で振り始めをずらす秒数。
        [SerializeField]
        float _groupTimingOffsetSeconds;

        public PenlightLayoutSettings Layout => _layout;

        // カメラ周辺の除外を適用する前の最大生成本数。
        // Controllerが一時Listの初期容量を確保するために使用する。
        public int MaximumInstanceCount =>
            Mathf.Max(1, _layout.blockCount.x) *
            Mathf.Max(1, _layout.blockCount.y) *
            Mathf.Max(1, _layout.seatPerBlock.x) *
            Mathf.Max(1, _layout.seatPerBlock.y);

        void Reset()
        {
            _layout = PenlightLayoutSettings.Default();
        }

        void OnValidate()
        {
            _layout.Clamp();
        }

        internal void CollectInstances(
            bool hasViewer,
            float3 viewerPosition,
            uint globalSeed,
            ref int globalIndex,
            List<float3> positions,
            List<quaternion> rotations,
            List<float> groupOffsets,
            List<uint> seeds)
        {
            // Inspectorの値が不正でも、実行時には安全な範囲へ補正したコピーを使う。
            var layout = _layout;
            layout.Clamp();

            // ブロック → ブロック内座席の順に走査し、一本分のデータを生成する。
            for (var blockY = 0; blockY < layout.blockCount.y; blockY++)
            {
                for (var blockX = 0; blockX < layout.blockCount.x; blockX++)
                {
                    for (var seatY = 0; seatY < layout.seatPerBlock.y; seatY++)
                    {
                        for (var seatX = 0; seatX < layout.seatPerBlock.x; seatX++)
                        {
                            GetLocalPose(
                                layout,
                                blockX,
                                blockY,
                                seatX,
                                seatY,
                                out var localPosition,
                                out var localRotation);

                            // 配置先Transform確定後のワールド座標へ変換する。
                            // この値はControllerのNativeArrayへ保存されるため、配置変更時はRebuildが必要。
                            var worldPosition = transform.TransformPoint(localPosition);
                            if (hasViewer && layout.viewerExclusionRadius > 0.0f)
                            {
                                // プレイヤーの足元付近だけをXZ平面の距離で除外し、
                                // HMDのすぐ近くにペンライトが重なることを防ぐ。
                                var delta = new float2(
                                    worldPosition.x - viewerPosition.x,
                                    worldPosition.z - viewerPosition.z);
                                if (math.lengthsq(delta) < layout.viewerExclusionRadius * layout.viewerExclusionRadius)
                                    continue;
                            }

                            // 各Listの同じインデックスが、同じ一本のペンライトを表す。
                            positions.Add(worldPosition);
                            rotations.Add(transform.rotation * localRotation);
                            groupOffsets.Add(_groupTimingOffsetSeconds);
                            seeds.Add(HashSeed(globalSeed, (uint)globalIndex));
                            globalIndex++;
                        }
                    }
                }
            }
        }

        static void GetLocalPose(
            PenlightLayoutSettings layout,
            int blockX,
            int blockY,
            int seatX,
            int seatY,
            out Vector3 position,
            out Quaternion rotation)
        {
            // 1ブロックの幅・奥行きには、次ブロックまでの通路幅も含める。
            var blockWidth = layout.seatPitch.x * (layout.seatPerBlock.x - 1) + layout.aisleWidth.x;
            var blockDepth = layout.seatPitch.y * (layout.seatPerBlock.y - 1) + layout.aisleWidth.y;

            // Zone原点を中央として、まず共通の格子座標を求める。
            var gridX = layout.seatPitch.x * (seatX - (layout.seatPerBlock.x - 1) * 0.5f)
                        + blockWidth * (blockX - (layout.blockCount.x - 1) * 0.5f);
            var gridZ = layout.seatPitch.y * (seatY - (layout.seatPerBlock.y - 1) * 0.5f)
                        + blockDepth * (blockY - (layout.blockCount.y - 1) * 0.5f);
            // 後方の列ほど肩位置を高くし、段状の客席を表現する。
            var row = blockY * layout.seatPerBlock.y + seatY;
            var y = layout.shoulderHeight + row * layout.rowHeight;

            if (layout.layoutMode == PenlightLayoutMode.Grid)
            {
                // Gridでは格子座標をそのまま使い、Zoneの正面方向へ揃える。
                position = new Vector3(gridX, y, gridZ);
                rotation = Quaternion.identity;
                return;
            }

            // Arcでは横位置を-1～1へ正規化し、円弧角度へ変換する。
            var halfWidth = layout.seatPitch.x * (layout.seatPerBlock.x - 1) * 0.5f
                            + blockWidth * (layout.blockCount.x - 1) * 0.5f;
            var normalizedX = halfWidth > 0.0001f ? gridX / halfWidth : 0.0f;
            var angle = normalizedX * layout.arcAngle * 0.5f * Mathf.Deg2Rad;
            // 奥の座席ほど半径を増やし、同心円状の観客席にする。
            var depthFromFront = blockY * blockDepth + seatY * layout.seatPitch.y;
            var radius = layout.arcRadius + depthFromFront;
            var radial = new Vector3(Mathf.Sin(angle), 0.0f, Mathf.Cos(angle));

            position = radial * radius;
            position.y = y;
            // 全ペンライトの基準方向を円弧中心（ステージ側）へ向ける。
            rotation = Quaternion.LookRotation(-radial, Vector3.up);
        }

        static uint HashSeed(uint seed, uint index)
        {
            // グローバルSeedと通し番号から、再生ごとに変わらない個体Seedを作る。
            var value = seed ^ (index + 0x9e3779b9u + (seed << 6) + (seed >> 2));
            value ^= value >> 16;
            value *= 0x7feb352du;
            value ^= value >> 15;
            value *= 0x846ca68bu;
            value ^= value >> 16;
            return value == 0u ? 1u : value;
        }

        void OnDrawGizmosSelected()
        {
            // ペンライト本体を生成しなくても、Scene Viewで配置範囲を確認できるようにする。
            var layout = _layout;
            layout.Clamp();
            Gizmos.color = new Color(0.15f, 0.9f, 1.0f, 0.4f);

            if (layout.layoutMode == PenlightLayoutMode.Grid)
            {
                // Gridは全体を囲むワイヤーの箱で表示する。
                var width = layout.blockCount.x * layout.seatPerBlock.x * layout.seatPitch.x
                            + Mathf.Max(0, layout.blockCount.x - 1) * layout.aisleWidth.x;
                var depth = layout.blockCount.y * layout.seatPerBlock.y * layout.seatPitch.y
                            + Mathf.Max(0, layout.blockCount.y - 1) * layout.aisleWidth.y;
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawWireCube(
                    new Vector3(0.0f, layout.shoulderHeight, 0.0f),
                    new Vector3(width, 0.1f, depth));
                return;
            }

            // Arcは最前列と最後列の半径をワイヤー球で表示する。
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireSphere(Vector3.zero, layout.arcRadius);
            var lastRadius = layout.arcRadius
                             + (layout.blockCount.y - 1) *
                             (layout.seatPitch.y * (layout.seatPerBlock.y - 1) + layout.aisleWidth.y)
                             + (layout.seatPerBlock.y - 1) * layout.seatPitch.y;
            Gizmos.DrawWireSphere(Vector3.zero, lastRadius);
        }
    }
}
