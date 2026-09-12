# カットモードで途中から再生する

LiveSceneとDemoSceneの音楽・ダンス・口パクを、指定した曲内時刻から開始できます。

## 事前設定

1. Play Modeを終了する。
2. Projectウィンドウで `Assets/Common/PerformancePlaybackConfig.asset` を選ぶ。
3. Inspectorの **Cut Mode** をONにする。
4. **Start Seconds** に曲の先頭からの秒数を入力する。例: `90` は1分30秒。
5. DemoSceneでPlay Modeを開始するか、LiveSceneで再生操作を行う。

両シーンのStage Directorは同じ設定アセットを参照します。初期値はCut Mode OFF、Start Seconds 90です。
OFFでは先頭から通常再生し、ONかつ0秒も先頭から再生します。
別々に設定する場合は、Create > Livisor > Performance Playback Configで別アセットを作り、
対象シーンのStage DirectorのPlayback Configへ割り当ててください。参照が未設定なら通常再生です。

開始位置は0以上、音源の長さ未満にします。範囲外、NaN、Infinityや音源未設定の場合は再生を開始せず、
Consoleにエラーを出します。Demoの画面にも設定エラーを表示します。
設定は最初の再生操作で確定します。一時停止後の再開では停止位置から続き、設定アセットの変更を適用し直しません。
別の開始位置を試すときはPlay Modeを終了してから設定します。
この設定はビルドに含まれます。ビルド済みアプリ内で編集・保存する画面はありません。

## 再生と演出

既存の導入演出を約2秒再生した後、指定位置から音楽を開始します。
Mainと4つの解析用音源、StageDirector、ダンス、口パクの位置を合わせます。
StageDirectorの音楽開始Animation Eventの時刻を使って、曲内時刻からアニメーション時刻へ変換します。
曲末の暗転と終了処理も進めた位置から続きます。音源終了後の既存のフィナーレまでの間も残ります。

| 開始位置より前の予約 | カット開始時の扱い |
|---|---|
| 雷・銀テープ | 飛ばす |
| 紙吹雪ON/OFF・音量 | 最後の有効な状態だけを復元する |
| 再生・停止 | 飛ばす |
| 開始位置ちょうど | 開始時に1回実行する |

予約の時刻は元の曲の先頭からの位置です。90秒から始めた場合、130秒の予約は約40秒後に実行されます。
カメラと小道具は開始位置での状態を復元します。ランダムなカメラ選択、既に舞っていた紙片、音声解析の履歴までは再現しません。
Demoの表示は曲内時刻を維持し、カットのON/OFFと開始位置を表示します。復元した紙吹雪は新規発火の履歴には追加しません。

Liveで一覧が再受信されても、カットした区間の予約は発火しません。
開始後の区間に過去時刻の予約が追加された場合は、従来どおりその場で実行します。
初回の状態復元後、再受信だけで音量や紙吹雪の初期状態へ戻ることはありません。

外部デバイス・サーバー・管理画面の通信仕様は変更していません。
開始位置はUnity内だけに適用され、デバイスの再生位置や別端末へ配信する設定は同期されません。

## 検証

Unity Editorの `Livisor > Tests > Timeline Action Playback` で予約の境界、状態復元、再受信を検証できます。
`Livisor > Tests > Cut Playback` には、Demoの90秒開始・曲末・通常再生・0秒開始と、Liveの90秒開始の検証があります。
Liveの検証は受信済みの状態を注入し、実際のサーバーや外部デバイスへは接続しません。
検証中は音をミュートし、終了時に戻します。結果は `Logs/cut-*.json` に保存します。
検証用の設定とオブジェクトはシーンへ保存せず、終了後にシーンを開き直してください。

Editor起動中のCLI例:

```sh
unity command eval --code 'TestTimelineActionPlayback.Run();' --json
unity command eval --code 'TestCutPlayback.Demo90();' --json
unity command eval --code 'TestCutPlayback.DemoFinale();' --json
unity command eval --code 'TestCutPlayback.DemoNormal();' --json
unity command eval --code 'TestCutPlayback.DemoZero();' --json
unity command eval --code 'TestCutPlayback.Live90();' --json
```

各シーン検証は開始時点でコマンドが戻ります。結果ファイルとPlay Modeの終了を確認してから次を実行してください。
