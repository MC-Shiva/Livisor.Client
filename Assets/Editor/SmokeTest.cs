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
