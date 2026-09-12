using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// DemoScene の単独再生と、サーバー・クライアントの疎通テストの入口。
/// 対象シーンを開き、対応する Test*Driver を置いて Play Mode に入る。
/// Sandbox のバッチ実行: Unity -batchmode -nographics -projectPath . -executeMethod TestScenes.Run -logFile log.txt
/// エディタ上: メニュー Livisor / Tests
/// Run の結果は SmokeTestResult.json（プロジェクト直下）に書かれ、バッチ実行では終了コードにもなる。
/// </summary>
public static class TestScenes
{
    [MenuItem("Livisor/Tests/Record Effects")]
    public static void RunRecordEffects()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        EditorSceneManager.OpenScene("Assets/Scenes/RecordScene.unity");
        new GameObject("TestRecordEffectsDriver").AddComponent<TestRecordEffectsDriver>();
        EditorApplication.EnterPlaymode();
    }

    /// <summary>DemoScene の事前定義をサーバーなしで全曲検証する。</summary>
    [MenuItem("Livisor/Tests/Demo Effects")]
    public static void RunDemoEffects()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        EditorSceneManager.OpenScene("Assets/Scenes/DemoScene.unity");
        new GameObject("TestDemoEffectsDriver").AddComponent<TestDemoEffectsDriver>();
        EditorApplication.EnterPlaymode();
    }

    /// <summary>Admin → localhost:5210 → LiveScene の演出予約を検証する。シーンと接続設定の変更は保存しない。</summary>
    [MenuItem("Livisor/Tests/Live Effects")]
    public static void RunLiveEffects()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene("Assets/Scenes/LiveScene.unity");
        Object.FindFirstObjectByType<StageDirector>().useDirectorCameraInEditor = true;
        foreach (var device in Object.FindObjectsByType<Livisor.Device.DeviceCommandExample>(FindObjectsSortMode.None))
            device.enabled = false;
        var config = ScriptableObject.CreateInstance<ServerConfig>();
        var configObject = new SerializedObject(config);
        configObject.FindProperty("_serverAddress").stringValue = "http://127.0.0.1:5210";
        configObject.ApplyModifiedPropertiesWithoutUndo();
        var receiver = new SerializedObject(Object.FindFirstObjectByType<TimelineReceiver>());
        receiver.FindProperty("_serverConfig").objectReferenceValue = config;
        receiver.FindProperty("_roomId").stringValue = "smoke-live-effects";
        receiver.FindProperty("_device").objectReferenceValue = null;
        receiver.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.OpenScene("Assets/Admin/Scenes/Admin.unity", OpenSceneMode.Additive);
        var admin = new SerializedObject(Object.FindFirstObjectByType<AdminConsoleView>());
        admin.FindProperty("_serverConfig").objectReferenceValue = config;
        admin.FindProperty("_roomId").stringValue = "smoke-live-effects";
        admin.ApplyModifiedPropertiesWithoutUndo();
        new GameObject("TestLiveEffectsDriver").AddComponent<TestLiveEffectsDriver>();
        EditorApplication.EnterPlaymode();
    }

    [MenuItem("Livisor/Tests/Live and Device (5 minutes)")]
    public static void RunLiveDevice()
    {
        EditorSceneManager.OpenScene("Assets/Admin/Scenes/Admin.unity");
        new GameObject("TestLiveDeviceDriver").AddComponent<TestLiveDeviceDriver>();
        EditorApplication.EnterPlaymode();
    }

    [MenuItem("Livisor/Tests/Timeline")]
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Sandbox.unity");
        var go = new GameObject("TestTimelineDriver");
        go.AddComponent<TestTimelineDriver>();
        EditorApplication.EnterPlaymode();
    }

    /// <summary>管理者画面のボタンを押して配信する版。Admin シーンを開いて Play モードに入る。</summary>
    [MenuItem("Livisor/Tests/Admin")]
    public static void RunAdmin()
    {
        EditorSceneManager.OpenScene("Assets/Admin/Scenes/Admin.unity");
        var go = new GameObject("TestAdminDriver");
        go.AddComponent<TestAdminDriver>();
        EditorApplication.EnterPlaymode();
    }

    /// <summary>管理者画面の全機能（入力チェック・再生・二重再生・音量スライダーの範囲と反映・予約・取消・停止・再開・切断・再接続）を通す版。</summary>
    [MenuItem("Livisor/Tests/Admin (all features)")]
    public static void RunAdminFull()
    {
        EditorSceneManager.OpenScene("Assets/Admin/Scenes/Admin.unity");
        var go = new GameObject("TestAdminFullDriver");
        go.AddComponent<TestAdminFullDriver>();
        EditorApplication.EnterPlaymode();
    }
}
