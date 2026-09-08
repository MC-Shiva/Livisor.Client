using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Livisor.Live.Effects
{
    /// <summary>One particle is one subdivided ribbon. Shape deformation runs in the vertex shader.</summary>
    [DisallowMultipleComponent]
    public sealed class SilverStreamerController : MonoBehaviour
    {
        [Header("Burst (local metres; keep the prefab scale at one)")]
        [SerializeField] Vector3[] launchPositions = { new Vector3(-2.5f, .3f, 0), new Vector3(2.5f, .3f, 0) };
        [SerializeField, Range(1, 1000)] int countPerLauncher = 100;
        [SerializeField, Range(1, 4000)] int maxParticles = 800;
        [SerializeField, Min(.1f)] float lifetime = 12;
        [SerializeField, Min(.1f), Tooltip("抵抗を除いた打ち上げ高さの目安（m）。実際の高さは抵抗により低くなります。")] float launchHeight = 4;
        [SerializeField, Range(.05f, 2)] float gravityMultiplier = .12f;
        [Header("Launch angles (degrees, local +Z is forward)")]
        [SerializeField, Range(10, 90), Tooltip("水平からの仰角。90度で真上。")]
        float launchElevation = 60;
        [SerializeField, Range(-180, 180), Tooltip("左右の方向。0度で前方、正の角度で右。")]
        float launchAzimuth = 0;
        [SerializeField, Range(0, 180), Tooltip("左右に広がる全角。90なら中心から左右45度。")]
        float horizontalSpread = 90;
        [SerializeField, Range(0, 90), Tooltip("上下に広がる全角。地面に向けては射出しません。")]
        float verticalSpread = 24;
        [Header("Air spreading")]
        [SerializeField, Range(0, 2), Tooltip("空中で粒子ごとに異なる横方向の加速度（m/s²）。0で追加拡散なし。")]
        float airSpreadAcceleration = .6f;
        [Header("Optional clipping area in local space")]
        [SerializeField] Vector3 areaCenter = new Vector3(0, 2.5f, 4.5f);
        [SerializeField] Vector3 areaSize = new Vector3(9, 7, 12);
        [SerializeField] bool restrictToArea = false;
        [Header("Floor collision (changes apply on the next Play Mode)")]
        [SerializeField, Tooltip("衝突対象のレイヤー。既存のステージ床はレイヤー20です。")]
        LayerMask collisionLayers = 1 << 20;
        [SerializeField, Range(1, 2), Tooltip("帯を囲む判定球の余白。大きいほど床に触れる前に消えます。")]
        float collisionRadiusScale = 1.05f;
        [SerializeField, Tooltip("Colliderのない場所でも指定高さを床として扱う場合のみ有効にします。")]
        bool useFallbackFloor = false;
        [SerializeField, Tooltip("補助床のローカルY座標。Use Fallback Floorが有効な場合のみ使用。")]
        float floorHeight = 0;
        [SerializeField, Range(0, 1)] float sway = .25f;
        [Header("Ribbon")]
        [SerializeField, Range(.1f, 2)] float length = .325f;
        [SerializeField, Range(.01f, .15f)] float width = .035f;
        [SerializeField, Range(4, 32)] int segments = 12;
        [SerializeField, Range(0, .3f)] float bend = .045f;
        [SerializeField, Range(0, 3)] float twist = 1.2f;
        [SerializeField, Range(.1f, 10)] float flutterSpeed = 4;

        ParticleSystem particles;
        ParticleSystemRenderer particleRenderer;
        ParticleSystem.Particle[] buffer;
        Mesh ribbon;
        Material material;
        MaterialPropertyBlock properties;
        float effectTime;
        static readonly int Clock = Shader.PropertyToID("_EffectTime");

        public int ParticleCount => particles != null ? particles.particleCount : 0;

        void Awake() => Initialize();

        bool Initialize()
        {
            if (particles != null) return true;
            var shader = Resources.Load<Shader>("SilverStreamer");
            if (shader == null)
            {
                Debug.LogError("SilverStreamer shader is missing from Resources.", this);
                return false;
            }
            var child = new GameObject("Ribbon Particles");
            child.transform.SetParent(transform, false);
            particles = child.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = lifetime;
            main.startLifetime = lifetime;
            main.startSpeed = 0;
            main.startSize = 1;
            main.maxParticles = maxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true;
            main.gravityModifier = gravityMultiplier;
            main.startRotation3D = true;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = particles.emission;
            emission.enabled = false;
            var shape = particles.shape;
            shape.enabled = false;
            var noise = particles.noise;
            noise.enabled = true;
            noise.strength = sway;
            noise.frequency = .45f;
            noise.scrollSpeed = .3f;
            noise.octaveCount = 1;
            noise.quality = ParticleSystemNoiseQuality.Low;
            var limit = particles.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 20;
            // Preserve the initial fan, then restore drag for the floating descent.
            limit.drag = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(
                new Keyframe(0, .08f), new Keyframe(.2f, .12f), new Keyframe(.5f, .4f), new Keyframe(1, .4f)));
            var force = particles.forceOverLifetime;
            force.enabled = true;
            force.space = ParticleSystemSimulationSpace.Local;
            force.randomized = false;
            var spreadMin = new AnimationCurve(new Keyframe(0, 0), new Keyframe(.12f, -1),
                new Keyframe(.45f, -1), new Keyframe(.75f, 0), new Keyframe(1, 0));
            var spreadMax = new AnimationCurve(new Keyframe(0, 0), new Keyframe(.12f, 1),
                new Keyframe(.45f, 1), new Keyframe(.75f, 0), new Keyframe(1, 0));
            // Keep all axes in TwoCurves mode with valid curves. Leaving Y at its
            // default constant while X/Z use TwoCurves can crash native ForceModule
            // curve evaluation (observed on Unity 6000.3.11f1/macOS).
            force.y = new ParticleSystem.MinMaxCurve(1,
                AnimationCurve.Constant(0, 1, 0), AnimationCurve.Constant(0, 1, 0));
            force.x = new ParticleSystem.MinMaxCurve(airSpreadAcceleration, spreadMin, spreadMax);
            force.z = new ParticleSystem.MinMaxCurve(airSpreadAcceleration * .5f, spreadMin, spreadMax);
            limit.multiplyDragByParticleSize = false;
            limit.multiplyDragByParticleVelocity = true;
            // Static world collisions continue with the particle system's unscaled clock.
            // The padded mesh bounds include GPU bending, which physics cannot see directly.
            var collision = particles.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.quality = ParticleSystemCollisionQuality.High;
            collision.collidesWith = collisionLayers;
            collision.enableDynamicColliders = false;
            collision.maxCollisionShapes = 256;
            collision.radiusScale = collisionRadiusScale;
            collision.lifetimeLoss = 1;
            collision.bounce = 0;
            collision.dampen = 1;
            collision.sendCollisionMessages = false;
            var rotation = particles.rotationOverLifetime;
            rotation.enabled = true;
            rotation.separateAxes = true;
            rotation.x = new ParticleSystem.MinMaxCurve(-1.3f, 1.3f);
            rotation.y = new ParticleSystem.MinMaxCurve(-.8f, .8f);
            rotation.z = new ParticleSystem.MinMaxCurve(-1.8f, 1.8f);
            // Shrink only at the end; opaque ribbons avoid transparent sorting/overdraw.
            var size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(
                new Keyframe(0, 1), new Keyframe(.85f, 1), new Keyframe(1, 0)));

            ribbon = CreateRibbon();
            material = new Material(shader) { name = "Silver Streamer (runtime)" };
            material.SetFloat("_Bend", bend);
            material.SetFloat("_Twist", twist);
            material.SetFloat("_Width", width);
            material.SetFloat("_FlutterSpeed", flutterSpeed);
            particleRenderer = child.GetComponent<ParticleSystemRenderer>();
            particleRenderer.renderMode = ParticleSystemRenderMode.Mesh;
            particleRenderer.mesh = ribbon;
            particleRenderer.sharedMaterial = material;
            particleRenderer.shadowCastingMode = ShadowCastingMode.Off;
            particleRenderer.receiveShadows = false;
            // Packed TEXCOORD0: UV.xy, AgePercent.z, StableRandomX.w.
            particleRenderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream> {
                ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal,
                ParticleSystemVertexStream.Tangent, ParticleSystemVertexStream.UV,
                ParticleSystemVertexStream.AgePercent, ParticleSystemVertexStream.StableRandomX });
            buffer = new ParticleSystem.Particle[maxParticles];
            properties = new MaterialPropertyBlock();
            return true;
        }

        /// <summary>Emit another burst, including while timeScale is zero. Existing ribbons remain alive.</summary>
        [ContextMenu("Fire Silver Streamers (Play Mode)")]
        public void Fire()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || !Initialize()) return;
            // Keep the configured nominal height at the central elevation (before drag).
            float gravity = Mathf.Max(.01f, Physics.gravity.magnitude * gravityMultiplier);
            float verticalSpeed = Mathf.Sqrt(2 * gravity * launchHeight);
            float speed = verticalSpeed / Mathf.Sin(Mathf.Clamp(launchElevation, 10, 90) * Mathf.Deg2Rad);
            // Floors may have been instantiated while timeScale is zero, before a physics tick.
            Physics.SyncTransforms();
            particles.Play();
            int available = Mathf.Max(0, maxParticles - particles.particleCount);
            int perLauncher = Mathf.Min(countPerLauncher, available / Mathf.Max(1, launchPositions.Length));
            foreach (var launcher in launchPositions)
            {
                for (int i = 0; i < perLauncher; i++)
                {
                    float elevation = Mathf.Clamp(launchElevation + Random.Range(-.5f, .5f) * verticalSpread, 0, 90);
                    float azimuth = launchAzimuth + Random.Range(-.5f, .5f) * horizontalSpread;
                    var velocity = LaunchDirection(elevation, azimuth) * speed * Random.Range(.9f, 1.1f);
                    particles.Emit(new ParticleSystem.EmitParams {
                        position = transform.TransformPoint(launcher),
                        velocity = transform.TransformDirection(velocity),
                        startLifetime = lifetime * Random.Range(.85f, 1),
                        startSize = 1,
                        rotation3D = new Vector3(Random.Range(0, 360), Random.Range(0, 360), Random.Range(0, 360))
                    }, 1);
                }
            }
        }

        static Vector3 LaunchDirection(float elevation, float azimuth)
        {
            float pitch = elevation * Mathf.Deg2Rad;
            float yaw = azimuth * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch),
                Mathf.Cos(yaw) * Mathf.Cos(pitch));
        }

        /// <summary>Set the central elevation and azimuth for subsequent bursts, in degrees.</summary>
        public void SetLaunchAngles(float elevation, float azimuth)
        {
            if (!IsFinite(new Vector3(elevation, azimuth, 0)))
                throw new System.ArgumentException("Launch angles must be finite.");
            launchElevation = Mathf.Clamp(elevation, 10, 90);
            launchAzimuth = Mathf.Repeat(azimuth + 180, 360) - 180;
        }

        /// <summary>Set an axis-aligned spread area in this object's local coordinates, in metres.</summary>
        public void SetSpreadArea(Vector3 center, Vector3 size, bool restrict = false)
        {
            if (!IsFinite(center) || !IsFinite(size) || size.x <= 0 || size.y <= 0 || size.z <= 0)
                throw new System.ArgumentException("Spread center must be finite and size must be finite and positive.");
            areaCenter = center;
            areaSize = size;
            restrictToArea = restrict;
        }

        static bool IsFinite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);

        public void Clear()
        {
            if (particles != null) particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        void LateUpdate()
        {
            if (particles == null || particles.particleCount == 0) return;
            effectTime += Time.unscaledDeltaTime;
            properties.SetFloat(Clock, effectTime);
            particleRenderer.SetPropertyBlock(properties);
            int count = particles.GetParticles(buffer);
            var bounds = new Bounds(areaCenter, areaSize);
            // Conservative sphere enclosing the ribbon and its maximum shader displacement.
            float margin = .5f * Mathf.Sqrt(length * length + width * width) + bend + width;
            var inner = new Bounds(bounds.center, Vector3.Max(Vector3.zero, bounds.size - Vector3.one * (2 * margin)));
            for (int i = 0; i < count; i++)
            {
                var local = transform.InverseTransformPoint(buffer[i].position);
                if ((useFallbackFloor && local.y <= floorHeight + margin) ||
                    (restrictToArea && !inner.Contains(local)))
                    buffer[i].remainingLifetime = 0;
            }
            particles.SetParticles(buffer, count);
        }

        Mesh CreateRibbon()
        {
            int rows = Mathf.Clamp(segments, 4, 32) + 1;
            var vertices = new Vector3[rows * 2];
            var uv = new Vector2[vertices.Length];
            var normals = new Vector3[vertices.Length];
            var tangents = new Vector4[vertices.Length];
            var triangles = new int[(rows - 1) * 6];
            for (int row = 0; row < rows; row++)
            {
                float t = (float)row / (rows - 1);
                for (int side = 0; side < 2; side++)
                {
                    int index = row * 2 + side;
                    vertices[index] = new Vector3((side - .5f) * width, (t - .5f) * length, 0);
                    uv[index] = new Vector2(side, t);
                    normals[index] = Vector3.forward;
                    tangents[index] = new Vector4(1, 0, 0, 1);
                }
                if (row == rows - 1) continue;
                int v = row * 2, k = row * 6;
                triangles[k] = v; triangles[k + 1] = v + 1; triangles[k + 2] = v + 2;
                triangles[k + 3] = v + 1; triangles[k + 4] = v + 3; triangles[k + 5] = v + 2;
            }
            var mesh = new Mesh { name = "Silver Ribbon", vertices = vertices, uv = uv,
                normals = normals, tangents = tangents, triangles = triangles };
            mesh.RecalculateBounds();
            var bounds = mesh.bounds;
            bounds.Expand(2 * (bend + width));
            mesh.bounds = bounds;
            return mesh;
        }

        void OnDisable() => Clear();
        void OnDestroy()
        {
            if (particles != null) Destroy(particles.gameObject);
            if (material != null) Destroy(material);
            if (ribbon != null) Destroy(ribbon);
        }

        void OnValidate()
        {
            countPerLauncher = Mathf.Clamp(countPerLauncher, 1, 1000);
            maxParticles = Mathf.Clamp(maxParticles, 1, 4000);
            launchElevation = Mathf.Clamp(launchElevation, 10, 90);
            launchAzimuth = Mathf.Clamp(launchAzimuth, -180, 180);
            horizontalSpread = Mathf.Clamp(horizontalSpread, 0, 180);
            verticalSpread = Mathf.Clamp(verticalSpread, 0, 90);
            airSpreadAcceleration = Mathf.Clamp(airSpreadAcceleration, 0, 2);
            collisionRadiusScale = Mathf.Clamp(collisionRadiusScale, 1, 2);
            lifetime = Mathf.Max(.1f, lifetime);
            launchHeight = Mathf.Max(.1f, launchHeight);
            areaSize = Vector3.Max(areaSize, Vector3.one * .01f);
            if (launchPositions == null) launchPositions = new Vector3[0];
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(areaCenter, areaSize);
            Gizmos.color = Color.yellow;
            foreach (var launcher in launchPositions)
            {
                Gizmos.DrawWireSphere(launcher, .12f);
                float rayLength = Mathf.Min(launchHeight, 3);
                Gizmos.DrawLine(launcher, launcher + LaunchDirection(launchElevation, launchAzimuth) * rayLength);
                for (int side = -1; side <= 1; side += 2)
                    for (int vertical = -1; vertical <= 1; vertical += 2)
                        Gizmos.DrawLine(launcher, launcher + LaunchDirection(
                            Mathf.Clamp(launchElevation + vertical * verticalSpread * .5f, 0, 90),
                            launchAzimuth + side * horizontalSpread * .5f) * rayLength);
            }
        }
    }
}
