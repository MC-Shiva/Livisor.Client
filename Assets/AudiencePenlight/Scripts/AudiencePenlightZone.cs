using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Livisor.Live.Penlights
{
    [DisallowMultipleComponent]
    public sealed class AudiencePenlightZone : MonoBehaviour
    {
        [SerializeField]
        PenlightLayoutSettings _layout = default;

        [SerializeField]
        float _groupTimingOffsetSeconds;

        public PenlightLayoutSettings Layout => _layout;

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
            var layout = _layout;
            layout.Clamp();

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

                            var worldPosition = transform.TransformPoint(localPosition);
                            if (hasViewer && layout.viewerExclusionRadius > 0.0f)
                            {
                                var delta = new float2(
                                    worldPosition.x - viewerPosition.x,
                                    worldPosition.z - viewerPosition.z);
                                if (math.lengthsq(delta) < layout.viewerExclusionRadius * layout.viewerExclusionRadius)
                                    continue;
                            }

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
            var blockWidth = layout.seatPitch.x * (layout.seatPerBlock.x - 1) + layout.aisleWidth.x;
            var blockDepth = layout.seatPitch.y * (layout.seatPerBlock.y - 1) + layout.aisleWidth.y;

            var gridX = layout.seatPitch.x * (seatX - (layout.seatPerBlock.x - 1) * 0.5f)
                        + blockWidth * (blockX - (layout.blockCount.x - 1) * 0.5f);
            var gridZ = layout.seatPitch.y * (seatY - (layout.seatPerBlock.y - 1) * 0.5f)
                        + blockDepth * (blockY - (layout.blockCount.y - 1) * 0.5f);
            var row = blockY * layout.seatPerBlock.y + seatY;
            var y = layout.shoulderHeight + row * layout.rowHeight;

            if (layout.layoutMode == PenlightLayoutMode.Grid)
            {
                position = new Vector3(gridX, y, gridZ);
                rotation = Quaternion.identity;
                return;
            }

            var halfWidth = layout.seatPitch.x * (layout.seatPerBlock.x - 1) * 0.5f
                            + blockWidth * (layout.blockCount.x - 1) * 0.5f;
            var normalizedX = halfWidth > 0.0001f ? gridX / halfWidth : 0.0f;
            var angle = normalizedX * layout.arcAngle * 0.5f * Mathf.Deg2Rad;
            var depthFromFront = blockY * blockDepth + seatY * layout.seatPitch.y;
            var radius = layout.arcRadius + depthFromFront;
            var radial = new Vector3(Mathf.Sin(angle), 0.0f, Mathf.Cos(angle));

            position = radial * radius;
            position.y = y;
            rotation = Quaternion.LookRotation(-radial, Vector3.up);
        }

        static uint HashSeed(uint seed, uint index)
        {
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
            var layout = _layout;
            layout.Clamp();
            Gizmos.color = new Color(0.15f, 0.9f, 1.0f, 0.4f);

            if (layout.layoutMode == PenlightLayoutMode.Grid)
            {
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
