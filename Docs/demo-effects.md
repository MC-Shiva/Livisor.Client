# DemoSceneの操作

`Assets/Scenes/DemoScene.unity`を開いてPlay Modeを開始すると、音楽とダンスを自動再生します。
ServerやAdminへの接続は不要です。雷・銀テープはボタンを押したときに出します。

| 操作 | Meta Quest | PCキーボード |
|---|---|---|
| ステージ上に雷を1本落とす | 左手 X | X |
| 銀テープを射出する | 左手 Y | Y |
| 音量を30%／100%に切り替える | 右手 A | A |
| 観客席／Unitychan目線の切替 | 右手 B | C |
| 視線の正面を合わせ直す | — | R |
| 音楽を一時停止／再開する | — | P／S |

PCではGameビューにフォーカスを当てて操作します。
X・Y・Aは押した瞬間に1回実行します。押し直すと再実行します。
雷・銀テープは音楽の一時停止中や終了後にも操作できます。
音量切替はMain音源と接続済みの振動デバイスに適用します。ステージの演出用音源には影響しません。

## 振動デバイス

`DemoSceneController`の`Device`から、同じLANのラズパイへ直接TCP/JSONを送ります。
接続先は`DeviceCommandExample`の`Mdns Host Name`（既定: `raspberrypi.local`）、ポートは`9901`です。
IPを指定する場合は`Manual Ip`に設定します。`TimelineReceiver`はDemoSceneでは無効です。

- 音楽が鳴り始めたときに`start`、一時停止・曲終了時に`stop`を送ります。
- 初回接続とAの音量切替で`volumeChange`を送ります。
- `Stop On Disable`により、デバイスの無効化・通常のシーン終了時にも停止を送ります。

デバイス側はLiveと同じ再生制御です。雷や銀テープ専用の振動コマンドは送りません。
再生位置を送る形式ではないため、途中接続や一時停止後の再開で曲位置が一致する保証はありません。
デバイスが未接続でもDemoは再生を続けます。接続状態は画面左上に表示します。
接続エラー後は、接続先を確認して`DeviceCommandExample`を無効化・再有効化すると再接続します。
デバイスを使わない場合は、このコンポーネントを無効にします。

## 雷の範囲

`DemoSceneController`の`Lightning Stage Point`を中心に、幅・奥行きが各4mの範囲からランダムに選びます。
高さは中心点の高さです。`Lightning Area Size`で幅と奥行きを調整できます。

紙吹雪を含む時刻指定の演出と、曲終了時の自動銀テープはDemoSceneでは実行しません。
画面左上には再生位置、音量、操作方法、直近5件の操作履歴を表示します。

## 動作確認

Unityの`Livisor > Tests > Demo Effects`を実行します。
通信を使わず、約30秒で次を確認します。

- 自動発火の停止と、ステージ上のランダム範囲
- 押しっぱなしでの重複防止、雷・銀テープの再発火と描画
- 音量30%／100%の切替と、音楽の一時停止・再開

結果は`Logs/demo-effects-check.json`、演出画像は`Logs/demo-buttons-lightning.png`と`Logs/demo-buttons-silver.png`です。
テストではQuestとキーボードが共用するボタン処理へ入力を渡します。実機のボタン入力・描画はQuestでも確認してください。

`Livisor > Tests > Demo Device (loopback)`では、ローカルのTCP受信先で再生・停止・音量・再接続を検証します。
結果は`Logs/demo-device-check.json`です。シーンの接続設定は保存せず、実機には送信しません。

Editorを開いている場合は、ClientのルートからCLIでも実行できます。

```sh
unity command eval --code 'TestScenes.RunDemoEffects();' --json
```
