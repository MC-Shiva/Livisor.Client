# DemoSceneの演出を調整する

`Assets/Scenes/DemoScene.unity`を開いてPlay Modeを開始すると、曲の再生位置に合わせて演出を実行します。サーバーやAdminへの接続は不要です。
画面左上に再生位置、紙吹雪の状態、直近5件の演出と実行時刻を表示します。

## 雷の時刻と位置

`Assets/DemoScene/Scripts/DemoLightningSchedule.cs`の`Create()`を編集します。
各行には、曲の先頭からの秒数と、名前または座標のどちらか一方を指定します。

```csharp
new(15, "unity-chan"), // 15秒にUnityちゃんの足元へ
new(30, "audience"),   // 30秒に観客席中央へ
new(45, 3, 0, 2),      // 45秒にステージ基準の (3, 0, 2) mへ
new(90, "stage"),      // 90秒にステージ中央へ
```

| 名前 | 着弾点 |
|---|---|
| `unity-chan` | 発火時の腰の位置を床面へ投影した点。ダンス中の移動に追従する |
| `audience` | `Lightning Audience`の位置 |
| `stage` | `Lightning Stage`の位置 |

座標は`Lightning Origin`からの相対位置です。Xは右、Yは上、Zは前、単位はmです。
原点の位置と回転を反映し、TransformのScaleは座標の単位に影響しません。
基準点・固定点と`DemoSceneController`の参照はInspectorで調整できます。

現在は3〜210秒の3秒間隔で70本を定義しています。
既存の14本に加え、ステージの左右、前方、客席中央・奥など56か所をXYZ座標で指定しています。
座標の範囲はX=-12〜12m、Z=-1〜20mです。追加分のYは床面の0mに揃えています。
行は読み込み時に時刻順へ並べます。同時刻の行はすべて実行します。
各行は1回だけ実行し、一時停止中は進みません。時刻は音源の終了前に置いてください。

1. Play Modeを終了する。
2. `DemoLightningSchedule.cs`の秒数・名前・座標を編集する。
3. Unityのコンパイルが完了してから、DemoSceneを再生する。

雷の調整にSharedの編集や同期は不要です。

## 紙吹雪と銀テープ

既存のShared定義から、0.5秒に紙吹雪を開始し、210秒に放出を停止し、220秒に銀テープを射出します。
停止後の紙片は自然に消えます。曲終了時の既存の銀テープも再生します。
Sharedの雷はDemoでは除外し、`DemoLightningSchedule`の一覧だけを使います。

紙吹雪・銀テープの時刻を変更する場合は、親リポジトリの`Livisor.Shared/Common/DefaultTimeline.cs`を編集して`make shared/sync`を実行します。
この共有定義はServerも利用します。Demo専用の雷の一覧はLiveSceneへ配信されません。

## 動作確認

Unityのメニュー`Livisor > Tests > Demo Effects`を実行します。
約4分半、音をミュートして全曲を再生し、次を確認します。

- 雷70本の発火時刻、着弾位置、VFXの再生
- 紙吹雪の開始・自然停止・再オン、銀テープの再射出
- 一時停止・再開、画面の演出履歴、実行エラー

結果は`Logs/demo-effects-check.json`です。完了後に`ok`、`lightningPositionsMatch`、`errors`を確認します。
雷の最初の3本は`Logs/lightning-position-1.png`〜`3.png`にも保存します。
検証用のオブジェクトはシーンへ保存せず、終了後はDemoSceneを開き直してください。

Editorを開いている場合は、ClientのルートからUnity CLIでも開始できます。

```sh
unity command eval --code 'TestScenes.RunDemoEffects();' --json
```

座標変換・移動追従・同時発火の確認は、通常のDemoSceneをPlay Modeにして次を実行します。全曲テストと同時には実行しません。

```sh
unity command eval --code 'return TestLightningPositions.Run(UnityEngine.Object.FindFirstObjectByType<DemoSceneController>());' --json
```

テスト用コードは`Assets/Tests`にまとめ、ファイル名・クラス名に`Test`を付けています。
Gameビューの描画とConsoleも確認してください。Cキーで視点を切り替えられます。
