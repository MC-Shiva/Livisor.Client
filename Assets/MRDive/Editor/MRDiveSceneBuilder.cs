using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Livisor.MRDive.EditorTools
{
    /// <summary>
    /// MR ダイブ用の入口シーン（DiveEntry）を組み立てる Editor 拡張。
    ///
    /// Meta XR SDK の型（OVRCameraRig / OVRPassthroughLayer / OVRManager）には
    /// 一切コンパイル時依存しない。すべてリフレクションとアセット検索で触るので、
    /// SDK が未解決・バージョン違いでもこのファイル自体は必ずコンパイルが通る。
    ///
    /// SDK 側のシリアライズ名はバージョンで変わりうるため、候補を複数試し、
    /// どれも見つからなければ黙ってスキップする（壊すより何もしないほうがマシ）。
    /// </summary>
    public static class MRDiveSceneBuilder
    {
        // ------------------------------------------------------------------
        // 定数
        // ------------------------------------------------------------------

        const string DiveScenePath = "Assets/Scenes/DiveEntry.unity";
        const string MainScenePath = "Assets/Scenes/Main.unity";

        /// <summary>DiveDirector.targetSceneName に入れる遷移先シーン名。</summary>
        const string TargetSceneName = "Main";

        const string LogTag = "[MRDive]";

        /// <summary>Meta XR SDK 側の型名。アセンブリ名は付けない（全アセンブリを走査するため）。</summary>
        const string PassthroughLayerTypeName = "OVRPassthroughLayer";
        const string OvrManagerTypeName = "OVRManager";

        /// <summary>ビルドに含まれている必要があるシェーダー 7 本。</summary>
        static readonly string[] RequiredShaders =
        {
            MRDiveShaders.Fade,
            MRDiveShaders.PortalRift,
            MRDiveShaders.PortalSurface,
            MRDiveShaders.DigitalDissolve,
            MRDiveShaders.DataStream,
            MRDiveShaders.LiquidDive,
            MRDiveShaders.Ripple,
        };

        // ------------------------------------------------------------------
        // ① ダイブシーンを生成
        // ------------------------------------------------------------------

        [MenuItem("Livisor/MR Dive/ダイブシーンを生成", false, 10)]
        public static void BuildDiveScene()
        {
            // 既存ファイルの上書き確認。黙って潰さない。
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(DiveScenePath) != null)
            {
                bool overwrite = Confirm(
                    "ダイブシーンを生成",
                    $"{DiveScenePath} はすでに存在します。\n\n" +
                    "中身をすべて作り直して上書きします。手で加えた変更は失われます。",
                    "上書きする",
                    "やめる");
                if (!overwrite) return;
            }

            // 今開いているシーンの未保存分を先に片付ける。
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // --- a. カメラリグ ---
            GameObject rig = InstantiateCameraRig(out bool usedMetaPrefab);
            Camera eyeCamera = ConfigureEyeCamera(rig);
            SetUpPassthroughLayer(rig);
            SetUpOvrManager(rig, scene);

            // --- b. ダイブの仕掛け ---
            BuildDiveRig(eyeCamera);

            // --- c. 環境 ---
            BuildEnvironment();

            EditorSceneManager.MarkSceneDirty(scene);

            if (!EditorSceneManager.SaveScene(scene, DiveScenePath))
            {
                Debug.LogError($"{LogTag} シーンの保存に失敗しました: {DiveScenePath}");
                return;
            }

            AssetDatabase.Refresh();

            var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(DiveScenePath);
            if (sceneAsset != null)
            {
                Selection.activeObject = sceneAsset;
                EditorGUIUtility.PingObject(sceneAsset);
            }

            Debug.Log(
                $"{LogTag} ダイブシーンを生成しました: {DiveScenePath}\n" +
                $"カメラリグ: {(usedMetaPrefab ? "OVRCameraRig.prefab" : "簡易カメラ（フォールバック）")}\n" +
                "続けて Livisor > MR Dive > Build Settings に登録 → シーンを検証 を実行してください。",
                sceneAsset);
        }

        // ------------------------------------------------------------------
        // ② Build Settings に登録
        // ------------------------------------------------------------------

        [MenuItem("Livisor/MR Dive/Build Settings に登録", false, 11)]
        public static void RegisterBuildScenes()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(DiveScenePath) == null)
            {
                Notify(
                    "Build Settings に登録",
                    $"{DiveScenePath} がまだありません。" +
                    "先に Livisor > MR Dive > ダイブシーンを生成 を実行してください。");
                return;
            }

            EditorBuildSettingsScene[] current = EditorBuildSettings.scenes;

            // DiveEntry → Main → 既存のその他、の順に組み直す。重複は作らない。
            var ordered = new List<EditorBuildSettingsScene>();
            ordered.Add(new EditorBuildSettingsScene(DiveScenePath, FindEnabledFlag(current, DiveScenePath, true)));

            bool hasMain = AssetDatabase.LoadAssetAtPath<SceneAsset>(MainScenePath) != null;
            if (hasMain)
            {
                ordered.Add(new EditorBuildSettingsScene(MainScenePath, FindEnabledFlag(current, MainScenePath, true)));
            }
            else
            {
                Debug.LogWarning($"{LogTag} {MainScenePath} が見つからないため、Main は登録しません。");
            }

            for (int i = 0; i < current.Length; i++)
            {
                string path = NormalizePath(current[i].path);
                if (path == NormalizePath(DiveScenePath)) continue;
                if (path == NormalizePath(MainScenePath)) continue;
                if (ContainsPath(ordered, path)) continue;
                ordered.Add(current[i]);
            }

            // 変更前後を必ず見せてから確定する。既存の Build Settings を黙って書き換えない。
            var message = new StringBuilder();
            message.AppendLine("【変更前】");
            message.AppendLine(DescribeSceneList(current));
            message.AppendLine();
            message.AppendLine("【変更後】");
            message.AppendLine(DescribeSceneList(ordered.ToArray()));

            bool apply = Confirm(
                "Build Settings に登録",
                message.ToString(),
                "この並びにする",
                "やめる");
            if (!apply) return;

            EditorBuildSettings.scenes = ordered.ToArray();
            Debug.Log($"{LogTag} Build Settings を更新しました。\n{DescribeSceneList(EditorBuildSettings.scenes)}");
        }

        // ------------------------------------------------------------------
        // ③ シーンを検証
        // ------------------------------------------------------------------

        [MenuItem("Livisor/MR Dive/シーンを検証", false, 32)]
        public static void ValidateScene()
        {
            var errors = new List<string>();
            var warnings = new List<string>();

            ValidateDirector(errors, warnings);
            ValidateCamera(errors, warnings);
            ValidatePassthrough(errors, warnings);
            ValidateShaders(errors, warnings);
            ValidateBuildSettings(errors, warnings);

            for (int i = 0; i < warnings.Count; i++) Debug.LogWarning($"{LogTag} {warnings[i]}");
            for (int i = 0; i < errors.Count; i++) Debug.LogError($"{LogTag} {errors[i]}");

            if (errors.Count == 0 && warnings.Count == 0)
            {
                Debug.Log($"{LogTag} ✅ シーンの検証 OK。");
            }
            else
            {
                Debug.Log($"{LogTag} 検証完了: エラー {errors.Count} 件 / 警告 {warnings.Count} 件。");
            }

            // パススルーを実機で出すための設定はシーンではなくプロジェクト側にある。
            // ここを見落とすと、シーンが完璧でも実機で現実が一切見えない。
            Debug.Log(
                $"{LogTag} 続けて Livisor > MR Dive > 実機設定を点検 を実行してください。" +
                "OVRProjectConfig のパススルー対応や ARM64 はシーンではなくプロジェクト設定側にあり、" +
                "未設定だと AndroidManifest に com.oculus.feature.PASSTHROUGH が入りません。");
        }

        // ------------------------------------------------------------------
        // ① の内訳: カメラリグ
        // ------------------------------------------------------------------

        /// <summary>
        /// Meta XR SDK の OVRCameraRig.prefab を検索して置く。
        /// 見つからなければ素の Camera で代用し、その旨を警告で明示する。
        /// </summary>
        static GameObject InstantiateCameraRig(out bool usedMetaPrefab)
        {
            GameObject prefab = FindCameraRigPrefab(out string prefabPath);
            if (prefab != null)
            {
                var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                if (instance != null)
                {
                    instance.transform.position = Vector3.zero;
                    instance.transform.rotation = Quaternion.identity;
                    usedMetaPrefab = true;
                    Debug.Log($"{LogTag} OVRCameraRig を配置しました: {prefabPath}");
                    return instance;
                }
            }

            usedMetaPrefab = false;
            Debug.LogWarning(
                $"{LogTag} Meta XR SDK の OVRCameraRig.prefab が見つからなかったので、簡易カメラで作成しました。\n" +
                "Package Manager で com.meta.xr.sdk.core が解決されているか確認し、解決後に " +
                "Livisor > MR Dive > ダイブシーンを生成 をやり直してください。" +
                "このままでは Quest 上でヘッドトラッキングもパススルーも動きません。");

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.position = new Vector3(0f, 1.6f, 0f);
            camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            return camGo;
        }

        /// <summary>パス直指定ではなく検索で OVRCameraRig.prefab を探す（SDK の配置場所に依存しないため）。</summary>
        static GameObject FindCameraRigPrefab(out string foundPath)
        {
            foundPath = null;

            string[] guids = SafeFindAssets("OVRCameraRig t:Prefab", null);
            if (guids.Length == 0) guids = SafeFindAssets("OVRCameraRig t:Prefab", new[] { "Packages" });
            if (guids.Length == 0) guids = SafeFindAssets("OVRCameraRig t:Prefab", new[] { "Assets" });

            GameObject fallback = null;
            string fallbackPath = null;

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path)) continue;

                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;

                // 完全一致（OVRCameraRig.prefab）を最優先。
                // OVRCameraRigInteraction など名前が前方一致するプレハブに引っかからないように。
                if (string.Equals(System.IO.Path.GetFileNameWithoutExtension(path), "OVRCameraRig",
                        StringComparison.Ordinal))
                {
                    foundPath = path;
                    return go;
                }

                if (fallback == null)
                {
                    fallback = go;
                    fallbackPath = path;
                }
            }

            foundPath = fallbackPath;
            return fallback;
        }

        static string[] SafeFindAssets(string filter, string[] searchInFolders)
        {
            try
            {
                return searchInFolders == null
                    ? AssetDatabase.FindAssets(filter)
                    : AssetDatabase.FindAssets(filter, searchInFolders);
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// CenterEyeAnchor の Camera をパススルー合成向けに設定する。
        /// アルファ 0 の黒でクリアすると、描いていない画素の背面にパススルーが出る。
        /// </summary>
        static Camera ConfigureEyeCamera(GameObject rig)
        {
            Camera camera = FindCenterEyeCamera(rig);
            if (camera == null)
            {
                Debug.LogError($"{LogTag} リグの中に Camera が見つかりません。CenterEyeAnchor の設定をスキップしました。", rig);
                return null;
            }

            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            camera.nearClipPlane = 0.01f;

            // リグに AudioListener が無ければ視点カメラに足しておく（音がまったく鳴らないのを防ぐ）。
            if (rig.GetComponentInChildren<AudioListener>(true) == null)
                camera.gameObject.AddComponent<AudioListener>();

            return camera;
        }

        /// <summary>
        /// 確認ダイアログ。batchmode では Unity がダイアログを出せず必ずキャンセル扱いに
        /// なる（"This should not be called when not controlled by a human"）ので、
        /// CLI / CI から実行できるよう承認扱いにする。
        /// </summary>
        static bool Confirm(string title, string message, string ok, string cancel)
        {
            if (Application.isBatchMode)
            {
                Debug.Log($"{LogTag} [batchmode] 確認を自動承認して続行: {title}");
                return true;
            }
            return EditorUtility.DisplayDialog(title, message, ok, cancel);
        }

        /// <summary>通知だけのダイアログ。batchmode ではログに落とす。</summary>
        static void Notify(string title, string message)
        {
            if (Application.isBatchMode)
            {
                Debug.Log($"{LogTag} {title}: {message}");
                return;
            }
            EditorUtility.DisplayDialog(title, message, "OK");
        }

        /// <summary>名前 → MainCamera タグ → 最初の Camera、の順で視点カメラを特定する。</summary>
        static Camera FindCenterEyeCamera(GameObject rig)
        {
            if (rig == null) return null;

            Camera[] cameras = rig.GetComponentsInChildren<Camera>(true);
            if (cameras.Length == 0) return null;

            for (int i = 0; i < cameras.Length; i++)
            {
                if (string.Equals(cameras[i].gameObject.name, "CenterEyeAnchor", StringComparison.Ordinal))
                    return cameras[i];
            }

            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i].gameObject.CompareTag("MainCamera")) return cameras[i];
            }

            return cameras[0];
        }

        /// <summary>
        /// リグのルートに OVRPassthroughLayer を付け、Underlay / 不透明度 1.0 にする。
        /// シリアライズ名は SDK バージョン差があるため候補を順に試し、全滅したら黙ってスキップする。
        /// </summary>
        static void SetUpPassthroughLayer(GameObject rig)
        {
            if (rig == null) return;

            Type layerType = FindTypeByName(PassthroughLayerTypeName);
            if (layerType == null)
            {
                Debug.LogWarning(
                    $"{LogTag} {PassthroughLayerTypeName} 型が見つかりませんでした（Meta XR SDK 未解決）。" +
                    "パススルーのレイヤーは手で追加してください。");
                return;
            }

            Component layer = rig.GetComponent(layerType);
            if (layer == null) layer = rig.AddComponent(layerType);
            if (layer == null)
            {
                Debug.LogWarning($"{LogTag} {PassthroughLayerTypeName} の AddComponent に失敗しました。", rig);
                return;
            }

            var so = new SerializedObject(layer);

            // Underlay = 「VR の描画より奥にパススルーを出す」。数値は SDK 版で変わりうるので名前で引く。
            SerializedProperty overlayType = FindFirstProperty(so, "overlayType", "currentOverlayType");
            TrySetEnumByName(overlayType, "Underlay");

            SerializedProperty opacity = FindFirstProperty(so, "textureOpacity_", "textureOpacity");
            TrySetFloat(opacity, 1f);

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>OVRManager.isInsightPassthroughEnabled を true にする。</summary>
        static void SetUpOvrManager(GameObject rig, Scene scene)
        {
            Type managerType = FindTypeByName(OvrManagerTypeName);
            if (managerType == null) return;

            Component manager = null;
            if (rig != null) manager = rig.GetComponentInChildren(managerType, true);
            if (manager == null) manager = FindComponentInScene(scene, managerType);
            if (manager == null)
            {
                Debug.LogWarning(
                    $"{LogTag} {OvrManagerTypeName} がシーンに見つかりません。" +
                    "OVRCameraRig の構成を確認してください。");
                return;
            }

            var so = new SerializedObject(manager);
            SerializedProperty insight = FindFirstProperty(so,
                "isInsightPassthroughEnabled", "_isInsightPassthroughEnabled", "isInsightPassthroughEnabled_");
            if (!TrySetBool(insight, true))
            {
                Debug.LogWarning(
                    $"{LogTag} {OvrManagerTypeName} の isInsightPassthroughEnabled が見つかりませんでした。" +
                    "Inspector で Quest Features > Passthrough を手で有効にしてください。", manager);
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------
        // ① の内訳: ダイブの仕掛け
        // ------------------------------------------------------------------

        static void BuildDiveRig(Camera eyeCamera)
        {
            var root = new GameObject("MR Dive");
            root.transform.position = Vector3.zero;

            var director = root.AddComponent<DiveDirector>();

            // 効果音の出し口。2D 固定（spatialBlend = 0）で、Director からだけ鳴らす。
            var audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.loop = false;
            audio.spatialBlend = 0f;

            // 演出は 1 つずつ別の GameObject に。Director が GetComponentsInChildren で拾い、
            // この並び順がそのまま UI のボタン順になる。
            var transitions = new GameObject("Transitions");
            transitions.transform.SetParent(root.transform, false);

            AddTransition<PortalRiftTransition>(transitions, "1 - Portal Rift");
            AddTransition<DigitalDissolveTransition>(transitions, "2 - Digital Dissolve");
            AddTransition<LiquidDiveTransition>(transitions, "3 - Liquid Dive");

            var directorSo = new SerializedObject(director);
            TrySetString(directorSo.FindProperty("targetSceneName"), TargetSceneName);
            TrySetObjectReference(directorSo.FindProperty("audioSource"), audio);
            directorSo.ApplyModifiedPropertiesWithoutUndo();

            BuildUi(root, director);

            if (eyeCamera == null)
            {
                Debug.LogWarning(
                    $"{LogTag} 視点カメラが解決できなかったため、DiveDirector は実行時に Camera.main を探します。");
            }
        }

        static void AddTransition<T>(GameObject parent, string name) where T : Component
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<T>();
        }

        /// <summary>
        /// UI（発射パネルと視線ポインタ）。director 参照のフィールド名は実装側でまだ確定していないので、
        /// 名前候補 → 型一致の順で探し、どちらも当たらなければ黙ってスキップして実行時の自動探索に任せる。
        /// </summary>
        static void BuildUi(GameObject root, DiveDirector director)
        {
            var ui = new GameObject("UI");
            ui.transform.SetParent(root.transform, false);

            var panel = ui.AddComponent<DiveLaunchPanel>();
            var pointer = ui.AddComponent<DiveGazePointer>();

            BindDirector(panel, director);
            BindDirector(pointer, director);
        }

        static void BindDirector(Component target, DiveDirector director)
        {
            if (target == null) return;

            var so = new SerializedObject(target);

            SerializedProperty prop = FindFirstProperty(so, "director", "_director", "diveDirector", "m_Director");
            if (prop == null || prop.propertyType != SerializedPropertyType.ObjectReference)
                prop = FindObjectReferencePropertyOfType(so, nameof(DiveDirector));

            if (TrySetObjectReference(prop, director))
                so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------
        // ① の内訳: 環境
        // ------------------------------------------------------------------

        static void BuildEnvironment()
        {
            // パススルーの上に重ねるので、明るいスカイボックスも強い環境光も邪魔になる。
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.14f, 0.14f, 0.17f, 1f);
            RenderSettings.fog = false;

            var lightGo = new GameObject("Directional Light");
            lightGo.transform.position = new Vector3(0f, 3f, 0f);
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(0.85f, 0.88f, 1f, 1f);
            light.intensity = 0.55f;
            light.shadows = LightShadows.None; // Quest では影は高くつく。入口シーンには要らない。
        }

        // ------------------------------------------------------------------
        // ③ の内訳
        // ------------------------------------------------------------------

        static void ValidateDirector(List<string> errors, List<string> warnings)
        {
            var directors = FindComponentsInLoadedScenes<DiveDirector>();

            if (directors.Count == 0)
            {
                errors.Add("DiveDirector がシーンにありません。Livisor > MR Dive > ダイブシーンを生成 で作り直してください。");
                return;
            }

            if (directors.Count > 1)
                warnings.Add($"DiveDirector が {directors.Count} 個あります。1 つだけにしてください。");

            DiveDirector director = directors[0];

            // 編集時は transitions リストが空でも問題ない（Awake で子から集めるため）。
            // そこで「シリアライズ済みの数」と「子にぶら下がっている数」の多いほうで判定する。
            int serialized = director.Transitions != null ? director.Transitions.Count : 0;
            int inChildren = director.GetComponentsInChildren<DiveTransitionBase>(true).Length;
            int count = Mathf.Max(serialized, inChildren);

            if (count == 0)
                errors.Add("演出（DiveTransitionBase）が 1 つも見つかりません。MR Dive/Transitions の子を確認してください。");
            else if (count != 3)
                warnings.Add($"演出の数が {count} 個です（想定は 3 個: PortalRift / DigitalDissolve / LiquidDive）。");

            if (string.IsNullOrEmpty(director.TargetSceneName))
                errors.Add("DiveDirector.targetSceneName が空です。");
            else if (director.TargetSceneName != TargetSceneName)
                warnings.Add($"DiveDirector.targetSceneName が \"{director.TargetSceneName}\" です（想定は \"{TargetSceneName}\"）。");
        }

        static void ValidateCamera(List<string> errors, List<string> warnings)
        {
            var cameras = FindComponentsInLoadedScenes<Camera>();

            // MainCamera タグの先頭を取ると LeftEyeAnchor を拾ってしまう（OVRCameraRig は
            // 左目にも MainCamera タグを付ける）。実行時の DiveDirector と同じ手順で
            // CenterEyeAnchor を優先する。
            Camera main = null;

            Transform centerEye = OvrLookup.FindCenterEyeAnchor();
            if (centerEye != null) main = centerEye.GetComponent<Camera>();

            if (main == null)
            {
                for (int i = 0; i < cameras.Count; i++)
                {
                    if (!cameras[i].gameObject.CompareTag("MainCamera")) continue;
                    main = cameras[i];
                    break;
                }
            }

            if (main == null)
            {
                errors.Add("MainCamera タグの付いた Camera がありません。OVRCameraRig の CenterEyeAnchor を確認してください。");
                return;
            }

            if (main.clearFlags != CameraClearFlags.SolidColor)
                warnings.Add($"{main.name}.clearFlags が {main.clearFlags} です。パススルー合成には SolidColor が必要です。");

            Color bg = main.backgroundColor;
            if (bg.a > 0.001f || bg.r > 0.001f || bg.g > 0.001f || bg.b > 0.001f)
            {
                warnings.Add(
                    $"{main.name}.backgroundColor が {bg} です。" +
                    "アルファ 0 の黒 (0,0,0,0) でないとパススルーが背面に合成されません。");
            }

            if (main.nearClipPlane > 0.05f)
                warnings.Add($"{main.name}.nearClipPlane が {main.nearClipPlane} です。至近距離の演出には 0.01 を推奨します。");
        }

        static void ValidatePassthrough(List<string> errors, List<string> warnings)
        {
            Type layerType = FindTypeByName(PassthroughLayerTypeName);
            if (layerType == null)
            {
                warnings.Add($"{PassthroughLayerTypeName} 型が見つかりません（Meta XR SDK 未解決）。実機ではパススルーが出ません。");
            }
            else if (FindComponentInLoadedScenes(layerType) == null)
            {
                errors.Add($"{PassthroughLayerTypeName} がシーンにありません。OVRCameraRig のルートに追加してください。");
            }

            Type managerType = FindTypeByName(OvrManagerTypeName);
            if (managerType == null) return;

            Component manager = FindComponentInLoadedScenes(managerType);
            if (manager == null)
            {
                errors.Add($"{OvrManagerTypeName} がシーンにありません。");
                return;
            }

            var so = new SerializedObject(manager);
            SerializedProperty insight = FindFirstProperty(so,
                "isInsightPassthroughEnabled", "_isInsightPassthroughEnabled", "isInsightPassthroughEnabled_");

            if (insight == null || insight.propertyType != SerializedPropertyType.Boolean)
                warnings.Add($"{OvrManagerTypeName} の isInsightPassthroughEnabled を読めませんでした（SDK のシリアライズ名が想定と違う）。");
            else if (!insight.boolValue)
                errors.Add($"{OvrManagerTypeName}.isInsightPassthroughEnabled が false です。Inspector で有効にしてください。");
        }

        static void ValidateShaders(List<string> errors, List<string> warnings)
        {
            var missing = new List<string>();
            for (int i = 0; i < RequiredShaders.Length; i++)
            {
                if (Resources.Load<Shader>(RequiredShaders[i]) == null)
                    missing.Add(RequiredShaders[i]);
            }

            if (missing.Count == 0) return;

            errors.Add(
                $"シェーダーが {missing.Count}/{RequiredShaders.Length} 本見つかりません:\n  " +
                string.Join("\n  ", missing) +
                "\nAssets/MRDive/Resources/ 以下に .shader を配置してください。");
        }

        static void ValidateBuildSettings(List<string> errors, List<string> warnings)
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;

            CheckRegistered(scenes, DiveScenePath, errors, warnings);
            CheckRegistered(scenes, MainScenePath, errors, warnings);
        }

        static void CheckRegistered(EditorBuildSettingsScene[] scenes, string path,
            List<string> errors, List<string> warnings)
        {
            string wanted = NormalizePath(path);
            for (int i = 0; i < scenes.Length; i++)
            {
                if (NormalizePath(scenes[i].path) != wanted) continue;

                if (!scenes[i].enabled)
                    warnings.Add($"{path} は Build Settings に登録されていますが無効化されています。");
                return;
            }

            errors.Add($"{path} が Build Settings にありません。Livisor > MR Dive > Build Settings に登録 を実行してください。");
        }

        // ------------------------------------------------------------------
        // リフレクションの小物（型の探索はここに集約）
        // ------------------------------------------------------------------

        /// <summary>読み込み済みの全アセンブリから型を名前で引く。見つからなければ null。</summary>
        static Type FindTypeByName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;

            Type direct = Type.GetType(typeName, false);
            if (direct != null) return direct;

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type t;
                try { t = assemblies[i].GetType(typeName, false); }
                catch (Exception) { continue; }
                if (t != null) return t;
            }
            return null;
        }

        // ------------------------------------------------------------------
        // SerializedProperty の小物
        // ------------------------------------------------------------------

        /// <summary>候補名を順に試して、最初に見つかったプロパティを返す。</summary>
        static SerializedProperty FindFirstProperty(SerializedObject so, params string[] candidateNames)
        {
            if (so == null || candidateNames == null) return null;

            for (int i = 0; i < candidateNames.Length; i++)
            {
                SerializedProperty p = so.FindProperty(candidateNames[i]);
                if (p != null) return p;
            }
            return null;
        }

        /// <summary>
        /// フィールド名が分からないときの最後の手段。
        /// オブジェクト参照フィールドを総当たりし、型名が一致するものを返す。
        /// （SerializedProperty.type は参照型のとき "PPtr&lt;$DiveDirector&gt;" の形になる）
        /// </summary>
        static SerializedProperty FindObjectReferencePropertyOfType(SerializedObject so, string typeName)
        {
            if (so == null) return null;

            SerializedProperty it = so.GetIterator();
            bool enterChildren = true;
            while (it.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                if (it.name == "m_Script") continue;
                if (it.type != null && it.type.Contains(typeName)) return it.Copy();
            }
            return null;
        }

        /// <summary>
        /// enum を「名前」で設定する。数値は SDK のバージョンで変わりうるので直接入れない。
        /// enumNames の並び順がそのまま enumValueIndex に対応する。
        /// </summary>
        static bool TrySetEnumByName(SerializedProperty prop, params string[] candidateNames)
        {
            if (prop == null || prop.propertyType != SerializedPropertyType.Enum) return false;

            string[] names = prop.enumNames;
            if (names == null || names.Length == 0) return false;

            for (int c = 0; c < candidateNames.Length; c++)
            {
                for (int i = 0; i < names.Length; i++)
                {
                    if (!string.Equals(names[i], candidateNames[c], StringComparison.OrdinalIgnoreCase)) continue;
                    prop.enumValueIndex = i;
                    return true;
                }
            }
            return false;
        }

        static bool TrySetFloat(SerializedProperty prop, float value)
        {
            if (prop == null || prop.propertyType != SerializedPropertyType.Float) return false;
            prop.floatValue = value;
            return true;
        }

        static bool TrySetBool(SerializedProperty prop, bool value)
        {
            if (prop == null || prop.propertyType != SerializedPropertyType.Boolean) return false;
            prop.boolValue = value;
            return true;
        }

        static bool TrySetString(SerializedProperty prop, string value)
        {
            if (prop == null || prop.propertyType != SerializedPropertyType.String) return false;
            prop.stringValue = value;
            return true;
        }

        static bool TrySetObjectReference(SerializedProperty prop, UnityEngine.Object value)
        {
            if (prop == null || prop.propertyType != SerializedPropertyType.ObjectReference) return false;
            prop.objectReferenceValue = value;
            return true;
        }

        // ------------------------------------------------------------------
        // シーン走査の小物
        // ------------------------------------------------------------------

        static Component FindComponentInScene(Scene scene, Type type)
        {
            if (!scene.IsValid() || !scene.isLoaded) return null;

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                Component found = roots[i].GetComponentInChildren(type, true);
                if (found != null) return found;
            }
            return null;
        }

        static Component FindComponentInLoadedScenes(Type type)
        {
            int count = UnityEngine.SceneManagement.SceneManager.sceneCount;
            for (int s = 0; s < count; s++)
            {
                Component found = FindComponentInScene(UnityEngine.SceneManagement.SceneManager.GetSceneAt(s), type);
                if (found != null) return found;
            }
            return null;
        }

        static List<T> FindComponentsInLoadedScenes<T>() where T : Component
        {
            var results = new List<T>();
            int count = UnityEngine.SceneManagement.SceneManager.sceneCount;

            for (int s = 0; s < count; s++)
            {
                Scene scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(s);
                if (!scene.IsValid() || !scene.isLoaded) continue;

                GameObject[] roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                    results.AddRange(roots[i].GetComponentsInChildren<T>(true));
            }
            return results;
        }

        // ------------------------------------------------------------------
        // Build Settings の小物
        // ------------------------------------------------------------------

        static string NormalizePath(string path)
        {
            return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/');
        }

        static bool FindEnabledFlag(EditorBuildSettingsScene[] scenes, string path, bool fallback)
        {
            string wanted = NormalizePath(path);
            for (int i = 0; i < scenes.Length; i++)
            {
                if (NormalizePath(scenes[i].path) == wanted) return scenes[i].enabled;
            }
            return fallback;
        }

        static bool ContainsPath(List<EditorBuildSettingsScene> scenes, string path)
        {
            string wanted = NormalizePath(path);
            for (int i = 0; i < scenes.Count; i++)
            {
                if (NormalizePath(scenes[i].path) == wanted) return true;
            }
            return false;
        }

        static string DescribeSceneList(EditorBuildSettingsScene[] scenes)
        {
            if (scenes == null || scenes.Length == 0) return "  (空)";

            var sb = new StringBuilder();
            for (int i = 0; i < scenes.Length; i++)
            {
                sb.Append("  ").Append(i).Append(": ").Append(scenes[i].path);
                if (!scenes[i].enabled) sb.Append("  [無効]");
                if (i < scenes.Length - 1) sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
