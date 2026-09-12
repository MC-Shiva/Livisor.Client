using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Livisor.MRDive
{
    /// <summary>
    /// ダイブの着地側。シーンを跨いで生き残り、遷移先で単色から明けてくる。
    ///
    /// 演出の最後に白飛び／暗転して切り替えると、切り替わった先が唐突に現れて興ざめになる。
    /// 出発側の最終色をそのまま持ち込んで明けることで、2 つのシーンが 1 本の演出に繋がる。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class DiveArrivalFade : MonoBehaviour
    {
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int AlphaId = Shader.PropertyToID("_Alpha");
        static readonly int CullId = Shader.PropertyToID("_Cull");

        const float CameraWaitTimeout = 5f;

        Color _color = Color.black;
        float _duration = 1f;
        float _holdBeforeFade = 0.15f;
        Scene _originScene;

        /// <summary>着地フェードを仕込む。シーン遷移を始める直前に呼ぶこと。</summary>
        public static DiveArrivalFade Spawn(Color color, float duration, float holdBeforeFade = 0.15f)
        {
            var go = new GameObject("MRDive Arrival Fade");
            DontDestroyOnLoad(go);

            var fade = go.AddComponent<DiveArrivalFade>();
            fade._color = color;
            fade._duration = Mathf.Max(0f, duration);
            fade._holdBeforeFade = Mathf.Max(0f, holdBeforeFade);

            // 出発側のシーンを覚えておく。遷移が完了する前に Camera.main を掴むと
            // 出発側のカメラを拾ってしまうので、その判別に使う。
            fade._originScene = SceneManager.GetActiveScene();
            return fade;
        }

        IEnumerator Start()
        {
            // このコンポーネントは遷移を始める直前に生成されるので、最初の数フレームは
            // まだ出発側のシーンが生きている。そこで Camera.main を掴むとベール球を
            // 出発側のカメラにぶら下げてしまい、シーンごと破棄されてフェードが一度も出ない。
            // 「出発側とは別のシーンに属するカメラ」が現れるまで待つ。
            Camera camera = null;
            float waited = 0f;

            while (waited < CameraWaitTimeout)
            {
                var candidate = Camera.main;
                if (candidate != null && (!_originScene.IsValid() || candidate.gameObject.scene != _originScene))
                {
                    camera = candidate;
                    break;
                }

                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (camera == null)
            {
                Debug.LogWarning("[MRDive] 遷移先にカメラが見つからず、着地フェードを省略しました。");
                Destroy(gameObject);
                yield break;
            }

            var shader = MRDiveShaders.Load(MRDiveShaders.Fade);
            if (shader == null)
            {
                Destroy(gameObject);
                yield break;
            }

            var material = new Material(shader) { renderQueue = DiveOverlay.BaseQueue + 500 };
            material.SetColor(ColorId, _color);
            material.SetFloat(AlphaId, 1f);
            if (material.HasProperty(CullId)) material.SetFloat(CullId, (float)CullMode.Front);

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Arrival Veil";
            var collider = sphere.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            var renderer = sphere.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.sharedMaterial = material;

            sphere.transform.SetParent(camera.transform, false);
            sphere.transform.localPosition = Vector3.zero;
            sphere.transform.localRotation = Quaternion.identity;
            sphere.transform.localScale = Vector3.one * 10f;

            // 遷移直後の 1 フレーム目は重いので、明け始める前に少し置く。
            float hold = 0f;
            while (hold < _holdBeforeFade)
            {
                hold += Time.unscaledDeltaTime;
                yield return null;
            }

            float elapsed = 0f;
            while (elapsed < _duration)
            {
                float t = _duration <= 0f ? 1f : elapsed / _duration;
                material.SetFloat(AlphaId, 1f - DiveEase.OutCubic(t));
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            Destroy(sphere);
            Destroy(material);
            Destroy(gameObject);
        }
    }
}
