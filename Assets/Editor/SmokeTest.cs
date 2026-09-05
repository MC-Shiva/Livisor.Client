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
    [MenuItem("Livisor/Smoke Test")]
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Sandbox.unity");
        var go = new GameObject("SmokeTestDriver");
        go.AddComponent<SmokeTestDriver>();
        EditorApplication.EnterPlaymode();
    }
}
