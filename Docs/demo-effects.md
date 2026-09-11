# DemoSceneのデフォルト演出

DemoSceneで演出を確認する人と、演出の時刻を編集する人向けの説明です。
`Assets/Scenes/DemoScene.unity`を開いてPlay Modeを開始すると、Sharedに定義した演出を曲の再生位置に合わせて実行します。
サーバー、Admin Scene、デバイスへの接続や、演出を発火するキー操作は不要です。

## 演出の形式

定義の正本は、親リポジトリの`Livisor.Shared/DTO/DefaultActionSet.cs`です。
`Create()`が返すC#の`TimelineAction[]`を使います。
各要素の`Time`が時刻、`ActionType.Effect`が演出の操作、`Value`の文字列が演出名です。

| 演出名 | 動作 |
|---|---|
| `confettiOn` | 紙吹雪の放出を開始する。停止するまで舞い続ける |
| `confettiOff` | 新しい紙片の放出を止める。表示中の紙片は自然に消える |
| `lightning` | 客席に雷を1回落とす |
| `silverStreamer` | 銀テープを1回射出する |

演出名の定数は`Livisor.Shared/Common/EffectNames.cs`にあります。
紙吹雪と銀テープは別の演出です。
開始・停止は`effect`の文字列で表し、`TimelineAction`に新しい項目は追加しません。
オンを重ねて指定しても、再生中の紙吹雪を最初からやり直しません。

`DefaultActionSet.Create()`の配列内では、次のように記述します。

```csharp
At("00:00:00:50", ActionType.Effect, EffectNames.ConfettiOn),
At("00:03:30:00", ActionType.Effect, EffectNames.ConfettiOff),
```

この例は、曲の先頭から0.5秒で紙吹雪を開始し、210秒で放出を止めます。
時刻は`HH:mm:ss:ff`（時:分:秒:センチ秒）です。末尾の`50`は0.5秒を表します。
現在の定義に入っている時刻は確認用の仮値です。
音源の終了後は再生位置が進まないため、定義の時刻は音源の終了前に置いてください。

## 実行の流れ

1. `DemoSceneController.Start()`が`DefaultActionSet.Create()`を読み込む。
2. `DefaultActionPlayback`が時刻順に並べる。
3. 曲の再生位置が指定時刻に達すると、`EffectDispatcher`が該当する演出を実行する。

再生位置は音源の`timeSamples`から求めます。シーンを開いてからの経過時間ではありません。
一時停止中は次の演出へ進みません。再開すると、続きの位置から実行します。
同じアクションは1回だけ実行します。最初から確認する場合は、Play Modeを終了してから再開してください。

デフォルト演出は、音楽やステージの既存演出に追加して実行します。
曲終了時の既存の銀テープも残っているため、Sharedの定義による射出と曲終了時の射出はそれぞれ行われます。
銀テープは1回800本で、同時に表示できる上限も800本です。前の銀テープが残っていると、追加の射出数が減ります。
粒子の寿命は最大12秒で、射出要求から粒子が出るまで0.6秒待ちます。次の射出要求までは13秒以上を空けてください。
現在の音源は約235秒で終了し、既存の終了時射出は音楽238秒相当の位置にあります。
確認用の射出は220秒に置き、終了時の射出要求が届く前に粒子が自然に消えるようにしています。

## 定義を変更する

1. Play Modeを終了する。
2. 親リポジトリの`Livisor.Shared/DTO/DefaultActionSet.cs`を編集する。
3. 親リポジトリのルートで`make shared/sync`を実行する。
4. Unityのファイル更新とコンパイルが完了するまで待つ。
5. DemoSceneを再生する。

Unityは`Packages/com.livisor.shared.unity`にあるSharedの複製を参照します。
`make shared/sync`で正本の変更を反映します。複製だけを編集すると、次の同期で上書きされます。
Play Mode中にスクリプトが再コンパイルされると、初期化状態が失われる場合があります。
編集前にPlay Modeを終了し、コンパイル完了後に再生してください。

## 動作を確認する

Unity Editorで次のメニューを実行します。

| メニュー | 確認内容 |
|---|---|
| `Livisor > Default Actions Check` | 時刻の解析、実行順序、二重実行の防止、Sharedの定義 |
| `Livisor > Demo Effects Check` | DemoSceneを約4分半再生し、指定時刻の実行、紙吹雪の自然停止・再オン、雷の再生、銀テープの定義・終了時・再射出、一時停止・再開を確認 |

全曲の検証は音をミュートして進み、完了するとPlay Modeを終了します。
結果は`Logs/demo-effects-check.json`に出力されます。最新の実行が完了してから`ok`と`errors`を確認してください。
確認用オブジェクトをシーンへ保存せず、検証後はDemoSceneを開き直してください。

Editorを開いている場合は、ClientのルートからUnity CLIでも起動できます。

```sh
unity command eval --code 'DefaultActionPlaybackCheck.Run();' --json
unity command eval --code 'DefaultActionPlaybackCheck.RunDemo();' --json
```

2つ目のコマンドは検証を開始した時点で戻ります。全曲の検証完了は、Play Modeの終了と結果ファイルで確認します。
複数のEditorを開いている場合は、`--project-path`に対象Clientの絶対パスを指定してください。

ConsoleのError・Exception・Warningは、シーン読み込み時から終了時まで確認します。
自動検証に加えてGameビューの描画も確認してください。
客席に出る雷や銀テープは、**Cキー**でUnitychan視点に切り替えると見渡せます。
**Tキー**で銀テープ、**Lキー**で雷を追加再生できます。**Eキー**は演出を終了します。
自動検証中は、操作キー（S・P・T・L・E）を押さないでください。

この検証の対象はDemoSceneの単独実行です。サーバー配信やAdminからの予約との結合、Quest実機での表示は別に検証します。
Adminの`Effect`入力欄は演出名を指定できる文字列欄になっています。Adminからの疎通は、このDemo検証には含みません。
