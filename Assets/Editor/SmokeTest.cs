using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// サーバーとクライアントの疎通を Unity から確かめる入口。
/// Sandbox シーン（受信側 TimelineReceiver）を開き、管理者役の SmokeTestDriver を置いて Play モードに入る。
/// バッチ実行: Unity -batchmode -nographics -projectPath . -executeMethod SmokeTest.Run -logFile log.txt
/// エディタ上: メニュー Livisor / Smoke Test
/// 結果は SmokeTestResult.json（プロジェクト直下）に書かれ、バッチ実行では終了コードにもなる。
/// </summary>
public static class SmokeTest
{
    /// <summary>Admin → localhost:5210 → LiveScene の演出予約を検証する。シーンと接続設定の変更は保存しない。</summary>
    [MenuItem("Livisor/Smoke Test (LiveScene scheduled effects)")]
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
        new GameObject("SmokeTestLiveEffectsDriver").AddComponent<SmokeTestLiveEffectsDriver>();
        EditorApplication.EnterPlaymode();
    }

    [MenuItem("Livisor/Smoke Test (Live and Device, 5 minutes)")]
    public static void RunLiveDevice()
    {
        EditorSceneManager.OpenScene("Assets/Admin/Scenes/Admin.unity");
        new GameObject("SmokeTestLiveDeviceDriver").AddComponent<SmokeTestLiveDeviceDriver>();
        EditorApplication.EnterPlaymode();
    }

    [MenuItem("Livisor/Smoke Test")]
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Sandbox.unity");
        var go = new GameObject("SmokeTestDriver");
        go.AddComponent<SmokeTestDriver>();
        EditorApplication.EnterPlaymode();
    }

    /// <summary>管理者画面のボタンを押して配信する版。Admin シーンを開いて Play モードに入る。</summary>
    [MenuItem("Livisor/Smoke Test (Admin)")]
    public static void RunAdmin()
    {
        EditorSceneManager.OpenScene("Assets/Admin/Scenes/Admin.unity");
        var go = new GameObject("SmokeTestAdminDriver");
        go.AddComponent<SmokeTestAdminDriver>();
        EditorApplication.EnterPlaymode();
    }

    /// <summary>管理者画面の全機能（入力チェック・再生・二重再生・音量の拒否と反映・予約・取消・停止・再開・切断・再接続）を通す版。</summary>
    [MenuItem("Livisor/Smoke Test (Admin, all features)")]
    public static void RunAdminFull()
    {
        EditorSceneManager.OpenScene("Assets/Admin/Scenes/Admin.unity");
        var go = new GameObject("SmokeTestAdminFullDriver");
        go.AddComponent<SmokeTestAdminFullDriver>();
        EditorApplication.EnterPlaymode();
    }
}
