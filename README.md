# Livisor.Client

Livisor for Client

ライブScene: `Assets/Scenes/LiveScene.unity`

通信なしのデモScene: `Assets/Scenes/DemoScene.unity`

観客ClientとAdminの接続、予約の形式、現在の対応範囲は
[サーバーとの通信](Docs/server-communication.md)を参照してください。

## DemoSceneでライブ演出を確認

`DemoScene` はサーバー、Admin Scene、ラズパイへ接続せず、ライブの見た目と音楽を確認するためのSceneです。
Play Modeを開始すると自動再生し、Gameビューで **S** を押すと再開、**P** を押すと一時停止します。
音楽は本番と同じAnimation Eventによって演出開始の約2秒後に再生されます。
DemoSceneは本番のBuild Settingsには含めません。

雷はClientの`DemoLightningSchedule.cs`、紙吹雪・銀テープはSharedの事前定義から自動再生します。
演出の形式、編集手順、検証方法は[DemoSceneのデフォルト演出](Docs/demo-effects.md)を参照してください。

再生中にGameビューで **C** を押すと、観客席とUnitychan目線を切り替えます。
Unitychan目線では目の位置に追従して客席を見渡せます。
Questでは **B / Y** で切り替え、**A / X**（Editorでは **R**）で視線の正面を合わせ直せます。
Editorでは滑らかに移動し、HMD接続時は即座に切り替わります。

観客席では自分用ペンライトがQuestの右手コントローラーへ追従します。
Unitychan目線へ切り替えると非表示になり、観客席へ戻ると現在の手元へ再表示されます。
左手で使用する場合は `PlayerPenlight` Prefabの `Controller Node` を `Left Hand` に変更します。

## Unity CLIで疎通確認

このディレクトリを対象に実行する（別の場所にある同名プロジェクトと取り違えないこと）。
Unity 6000.3.11f1とUnity CLIが必要。CLI用のPipelineパッケージはmanifestに追加済み。

Editorを閉じた状態で、EC2のRPCとラズパイのmDNS/TCP pingをまとめて確認:

```sh
unity run . --timeout 180 -- -nographics \
  -executeMethod Livisor.Editor.ConnectivityDiagnostics.RunBatch \
  -logFile Logs/connectivity-batch.log
```

実行後の `Logs/connectivity-report.json` に各段階の成否・応答・エラーを保存する。
全成功でUnityは0、一部失敗で1、起動引数などのエラーで2を返す
（Unity CLI自体はUnityの非ゼロ終了を別の非ゼロコードに変換する場合がある）。
非同期処理の完了を待って終了するため、`-quit` は付けない。

専用コマンドをバッチ起動する形式も使用できる（CLIの終了コードではなく診断結果を確認）:

```sh
unity run . --timeout 180 --command livisor_check_connectivity -- --host raspberrypi.local
```

ホスト名が異なる場合は、上記のUnity引数に次を追加:

```sh
-livisorDeviceHost livisor-pi.local -livisorDevicePort 9901
```

サーバーの変更には `-livisorServer http://57.183.27.205:5210` を使う。

Editorを開いている場合は専用コマンドを使う:

```sh
unity status --json
unity command livisor_check_connectivity --timeout 30 --json
# ホスト名を変更する場合
unity command livisor_check_connectivity --host livisor-pi.local --port 9901 --json
```

複数のEditorがある場合は `--project-path` でこのプロジェクトの絶対パスを指定する。
CLIの実行成功とは別に、診断結果の `success`、`serverSuccess`、`mdnsSuccess`、
`deviceSuccess` を確認する。Editorのメニュー `Livisor > Connectivity > Open Diagnostics`
からも接続先を入力して実行できる。シーンへのコンポーネント追加やPlay開始は不要。

## 確認内容

- EC2: `http://57.183.27.205:5210` にHTTP/2でMagicOnionの
  `IMyFirstService.SumAsync(100, 200)` を呼び、戻り値が `300` であることを確認。期限は10秒。
- ラズパイ: 既定の `raspberrypi.local` をmDNSのAレコードで解決（5秒）、
  解決したIPv4のTCP 9901へ `{"action":{"ping":1}}` を送信（4秒）。
  `ok: true`、`result.action: "ping"`、`result.pong: true` を検証する。
  再生・停止・音量変更は行わない。
- mDNSはホスト名の解決を行う。`_livisor._tcp` から任意のホストを自動選択する機能ではない。
- `SampleScene` の接続先もEC2を既定値とし、同じRPC確認処理を利用する。

ラズパイが見つからない場合は、Mac/Questと同じLANに接続されていること、
実際のホスト名、Avahiの稼働、Wi-Fiの端末間通信制限を確認する。
同じLAN内ではTailscaleは不要。Deviceリポジトリの `ssh Surf10@raspberrypi` は
Tailscale用の手順であり、ここではmDNS名の `raspberrypi.local` を使用する。
macOSでは `dns-sd -G v4 raspberrypi.local` と `dns-sd -B _livisor._tcp local.`
でも調べられる（終了はCtrl+C）。mDNS成功後にpingが失敗する場合は、Pi側の
`player-ctl.py` が9901番ポートで起動しているか、ping対応版かを確認する。

Android ManifestにはINTERNETとCHANGE_WIFI_MULTICAST_STATE権限を追加済み。
Quest実機でのLAN接続・mDNS通信は、Editorの確認とは別に実機で確認すること。

## 実機疎通の確認記録（2026-09-06）

Macとラズパイを同じiPhoneテザリングに接続し、Tailscale停止状態で
Unity CLIの `RunBatch` が終了コード0で完了した。
EC2の `SumAsync(100, 200)` は `300`、mDNSは
`raspberrypi.local → 172.20.10.8`、TCP 9901は
`{"ok":true,"scheduled":false,"result":{"action":"ping","pong":true}}` を確認。
IPは確認時のDHCPアドレスなので固定設定には使用しない。
初回のmDNS確認はタイムアウトし、再実行で成功した。
この確認で再生・停止・音量変更は行っていない。
