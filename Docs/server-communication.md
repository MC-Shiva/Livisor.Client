# サーバーとの通信（クライアント実装ガイド）

観客クライアントと管理者画面を実装する人向け。先に「考え方」と「やること」を書き、細かい仕様は後ろにまとめる。
用語は `Livisor.Server/Docs/Rules/glossary.md` に合わせる。

## 演出機能の対応範囲

| 対象 | 現在の対応 |
|---|---|
| Server | 起動時にSharedの定義を読み、デフォルト演出とAdminの追加予約を一覧で配信する |
| DemoScene | Serverに接続せず、QuestまたはPCのX/Y/Aで雷・銀テープ・音量を操作する |
| Admin | `Effect`を文字列で入力し、全行をまとめて追加できる。デフォルト演出を編集する画面はない |
| LiveScene | Serverの一覧に従い、紙吹雪の開始・停止、雷、銀テープを実行する。既存のシーン固定演出も動く |

Demoの操作と検証は[DemoSceneの操作](demo-effects.md)を参照。
LiveSceneはServerから受信した一覧を使う。DemoSceneの演出はボタン操作で実行する。

## 1. まず押さえること

1. サーバーは room ごとに「今の状態」を持っている。状態は 2 種類ある。**トランスポート**（再生中か、いつ始めたか、予約は何か）と、**状態同期**（音量や心拍数などの値）。
2. 管理者が操作すると、サーバーは状態を更新し、同じ room の全員に「新しい状態」を配る。「PLAY が押された」という操作そのものは配らない。
3. 音量などの状態同期では、通知に含まれる項目だけを上書きする。通知にない項目は前の値を保つ。
4. 予約を実行するのはクライアント。LiveSceneでは曲の再生位置が予約時刻に達するまで待つ。
5. 通信の入口は `RoomClient` 1 つ。MagicOnion を直接触らない。

## 2. 言葉

| 言葉 | 意味 |
|---|---|
| room | 状態を共有する単位。管理者と観客が同じ roomId で参加する。いまは `"room1"` |
| 参加 | Hub で room に入ること。入った時点の状態同期の全項目が返る |
| トランスポート | 「再生中か」「再生開始のサーバー時刻」「通知のサーバー時刻」「予約アクション」をまとめた `TransportState` |
| 状態同期 | 音量や心拍数など、変わり続ける値の共有。変化した項目だけが `RoomStatePatch` で届く |
| 予約アクション | 「曲の先頭から t 秒の位置で、これをする」の1件。Serverのキューには複数件を保持する |
| 相対時間 | 曲の先頭を `00:00:00:00` とした再生位置。`HH:mm:ss:ff`（時:分:秒:センチ秒）。`00:01:30:00` は 90 秒後 |
| 発火 | 予約アクションの時刻になり、クライアントがそれを実行すること |
| 配信 | サーバーが同じ room の全接続へ通知を送ること。送った本人にも届く |

## 3. 仕組み

経路は 2 つ。**操作は Unary で送り、配信は StreamingHub で受ける。**

| 経路 | 何をするか | 契約 |
|---|---|---|
| Unary サービス | 管理者が操作を送る（再生開始・停止・予約の登録・予約の取消）。応答は送った本人だけに返る | `ITimelineService` |
| StreamingHub | room に参加する。音量などの値を送る。サーバーからの通知（トランスポート・状態同期）を受ける | `IRoomStateHub`、受信側 `IRoomStateHubReceiver` |

```mermaid
sequenceDiagram
    participant A as 管理者画面
    participant S as サーバー
    participant C as 観客クライアント
    C->>S: 参加 JoinAsync("room1")
    S-->>C: 状態同期の全項目（volume など）
    C->>S: GetTransportAsync("room1")
    S-->>C: TransportState
    A->>S: PlayAsync("room1")
    S-->>A: TransportState（応答）
    S->>A: OnTransportChanged(TransportState)
    S->>C: OnTransportChanged(TransportState)
```

- 観客クライアントが Unary を使うのは、参加直後に `GetTransportAsync` を 1 回呼ぶときだけ。参加の応答にはトランスポートが入っていないため。
- 管理者が Unary で操作すると、応答が管理者に返ると同時に、同じ内容が `OnTransportChanged` で room の全員（管理者自身を含む）に届く。
- 音量は Hub の `PublishAsync` で送る。サーバーが room の状態に重ね、変化した項目だけを `OnStateChanged` で全員に配る。

## 4. 観客クライアントを作る

実装は `Assets/Timeline/Scripts/TimelineReceiver.cs`。再生・停止・音量予約の処理を以下に示す。

### 4.1 接続する

```csharp
[SerializeField] ServerConfig _config;      // Assets/Network/ServerConfig.asset（http://localhost:5210）
IRoomClient _client;

async void Start()
{
    _client = new RoomClient();
    _client.TransportChanged += OnTransport;   // トランスポートが届く
    _client.StateChanged += OnState;           // 状態同期が届く
    await _client.ConnectAsync(_config.ServerAddress, "room1");
}
```

`ConnectAsync` は「Hub に接続 → 参加 → トランスポートを 1 回取得」までやる。接続直後に `StateChanged` と `TransportChanged` が 1 回ずつ来るので、初期表示はそれで作る。

### 4.2 届いたものをメインスレッドへ渡す

通知は Unity のメインスレッド以外で届く。イベントの中で UI やシーンを触らず、キューに積んで `Update` で処理する。

```csharp
readonly ConcurrentQueue<TransportState> _transports = new();
readonly ConcurrentQueue<RoomStatePatch> _states = new();

void OnTransport(TransportState s) => _transports.Enqueue(s);
void OnState(RoomStatePatch p) => _states.Enqueue(p);

void Update()
{
    while (_states.TryDequeue(out var p)) ApplyState(p);
    while (_transports.TryDequeue(out var t)) ApplyTransport(t);
}
```

### 4.3 状態同期を反映する

届くのは変化した項目だけ。使うキーだけ拾い、知らないキーは無視する。

```csharp
void ApplyState(RoomStatePatch patch)
{
    foreach (var e in patch.Entries)
        if (e.Key == RoomStateKeys.Volume) _player.ChangeVolume(e.Value);   // e.Value.Number に音量
}
```

| 届く例 | 意味 |
|---|---|
| 参加の応答 `{ volume: 80, heartRate: 72 }` | 参加時点の全項目 |
| `{ volume: 30 }` | 音量だけ変わった。心拍数は前のまま |
| `{ heartRate: 95 }` | 心拍数だけ変わった。音量は前のまま |

### 4.4 トランスポートを反映する

`TransportState.Actions`はデフォルト演出と追加予約の全件を持つ。
`TimelineReceiver`は受信した一覧を`TimelineActionPlayback.Load()`へ渡し、`Playing`で再生・停止を切り替える。
毎フレーム`Advance()`へ曲の再生位置を渡し、到達したものを一度ずつ実行する。

LiveSceneは`StageDirector.MusicTimeSeconds`を使う。曲が始まる前と一時停止中は進まない。
予約時刻は曲の先頭からの位置で、追加時に過ぎていた位置なら1回すぐに実行する。
STOPは曲を一時停止し、PLAYは続きから再開する。実行済みの演出は繰り返さない。

同じ時刻・Action・Valueは同じ予約として扱う。予約IDは持たない。
追加予約を再実行する場合は、CANCELの完了後にSCHEDULEする。
CANCELしてもデフォルト演出は残り、実行済みのデフォルト演出は再実行しない。

`StageDirector`のないSandboxでは、受信時の経過時間を次の式で求め、その後の実時間を加えて進める。

```text
受信時の経過時間（秒） = (ServerTimeMs − StartedAtServerMs) / 1000
```

停止中は進めず、PLAY後は新しいサーバー開始時刻を基準にする。
LiveSceneとSandboxでは時刻の基準が異なるため、曲に合わせた演出はLiveSceneで確認する。

### 4.5 発火する

```csharp
async void Fire(TimelineAction action)
{
    switch (action.Action)
    {
        case ActionType.Play:                       // 値は bool。true = 再生、false = 停止
            if (action.Value.Kind == ActionValueKind.Bool) _player.Play(action.Value.Bool);
            break;
        case ActionType.VolumeChange:               // 値は int
            _player.ChangeVolume(action.Value);
            // 予約で変えた音量は状態同期に書き戻す。全員が同じ予約を持つので、全員が同じ値を送る
            await _client.PublishStateAsync(new RoomStateEntry { Key = RoomStateKeys.Volume, Value = action.Value });
            break;
        case ActionType.Effect:
            if (action.Value.Kind == ActionValueKind.Text) _effects?.Fire(action.Value.Text);
            break;
    }
}
```

書き戻した音量は `OnStateChanged` で自分にも戻り、同じ値を `ChangeVolume` にもう一度渡すことになる。結果は変わらない。

### 4.6 終了する

```csharp
async void OnDestroy() => await _client.DisposeAsync();
```

### 再生・音量の実行先

`IMediaPlayer` に `Play(bool)` と `ChangeVolume(ActionValue)` がある。

| 実装 | 用途 |
|---|---|
| `StageMediaPlayer` | ライブシーンの `StageDirector` を動かす本実装。再生・停止は音源と演出をまとめて切り替え、音量は Main 音源だけに効く |
| `LoggingMediaPlayer` | ログを出すだけ。`StageDirector` が無いシーン用 |

`TimelineReceiver` は `StageDirector` があれば `StageMediaPlayer`、無ければ `LoggingMediaPlayer` を使う。`Assets/Scenes/LiveScene.unity` に `TimelineReceiver` を置くと本実装で動く。

## 5. 管理者画面を作る

完成形は `Assets/Admin/Scripts/AdminConsoleView.cs`（`Assets/Admin/Scenes/Admin.unity`）。接続と通知の受け方は観客クライアントと同じ。違いはボタンで Unary を呼ぶこと。

### 5.1 ボタンと呼び出し

| ボタン | 呼び出し | 送る内容 | 戻り |
|---|---|---|---|
| CONNECT | `ConnectAsync(address, roomId)` | アドレス、roomId | 状態同期の全項目とトランスポートがイベントで届く |
| PLAY | `PlayAsync()` | roomId | `TransportState` |
| STOP | `StopAsync()` | roomId | `TransportState` |
| SCHEDULE | `ScheduleActionsAsync(actions)` | roomId と `TimelineAction[]` | `TransportState` |
| CANCEL | `CancelScheduledActionsAsync()` | roomId | `TransportState` |
| VOLUME | `PublishStateAsync(entry)` | `{ volume: N }` の 1 項目 | 無し。`OnStateChanged` で自分にも戻る |
| DISCONNECT | `DisposeAsync()` | 無し | 無し |

- PLAY / STOP は時刻を送らない。開始時刻はサーバーが決める。再生中に PLAY を押し直しても開始時刻は動かない。
- SCHEDULE は全入力行を時刻順に並べ、まとめて追加する。既存のキューは残る。CANCEL は追加予約をすべて取り消す。
- VOLUME は roomId を送らない。参加時の room をサーバーが覚えている。

### 5.2 例

```csharp
// 再生開始の 10 秒後に音量を 30 にする
var state = await _client.ScheduleActionsAsync(new TimelineAction
{
    Time = "00:00:10:00",
    Action = ActionType.VolumeChange,
    Value = ActionValue.From(30),
});
// state.Actions にデフォルト演出と追加予約が入る

// 再生開始の 10 秒後に停止する
await _client.ScheduleActionsAsync(new TimelineAction
{
    Time = "00:00:10:00",
    Action = ActionType.Play,
    Value = ActionValue.From(false),
});

// 音量を今すぐ 80 にする
await _client.PublishStateAsync(new RoomStateEntry { Key = RoomStateKeys.Volume, Value = ActionValue.From(80) });
```

### 5.3 表示の更新

Unary の応答と、その直後に届く `OnTransportChanged` は同じ値。応答で表示を更新し、通知でも同じ更新をしてよい。通知は非メインスレッドで届くので、観客クライアントと同じくキューに積んで `Update` で反映する。

### 5.4 演出を予約する入力形式

`Action`に`Effect`を選ぶと、値の入力欄が文字列欄になる。演出名は[演出の形式](demo-effects.md#演出の形式)を参照。
例えば、紙吹雪の停止要求は次の入力で表す。

| Time | Action | Value |
|---|---|---|
| `00:00:10:00` | `Effect` | `confettiOff` |

`SCHEDULE`は全行を追加する。開始と停止の2行を入れると、両方がキューへ追加される。
`CANCEL`は予約を取り消す操作であり、実行済みの紙吹雪へ停止要求を送る操作ではない。
この入力の送信先はサーバー。DemoSceneはAdminへ接続せず、ボタンで演出を操作する。
LiveSceneで実行するには、AdminとClientを同じサーバー・roomに接続し、PLAY後にSCHEDULEする。
停止中に予約した場合は、PLAYで再生を始めてから実行する。
過ぎた時刻を予約すると1回すぐに実行する。今すぐ紙吹雪を止めたい場合は、時刻を`00:00:00:00`、値を`confettiOff`にする。

## 6. データの形

`TransportState`（4項目）

| 項目 | 型 | 意味 |
|---|---|---|
| `Playing` | bool | 再生中か |
| `StartedAtServerMs` | long | 再生開始のサーバー時刻（UTC ミリ秒）。停止中は 0 |
| `ServerTimeMs` | long | この通知を作ったサーバー時刻（送信時刻） |
| `Actions` | `TimelineAction[]` | デフォルト演出と追加予約を時刻順に並べた全件 |

MessagePackのキーは0〜3。Key 3を単一予約から配列へ変更したため、ServerとClientは同時に更新する。

`TimelineAction`

| 項目 | 型 | 意味 |
|---|---|---|
| `Time` | string | 相対時間 `HH:mm:ss:ff` |
| `Action` | `ActionType` | `Play`、`VolumeChange`、`Effect` |
| `Value` | `ActionValue` | `Play`ならbool、`VolumeChange`ならint、`Effect`なら演出名のstring |

`RoomStatePatch` は `Entries`（`RoomStateEntry[]`）と `ServerTimeMs`。`RoomStateEntry` は `Key`（string）と `Value`（`ActionValue`）。
`ActionValue` は int / bool / string のどれか 1 つを持つ。`ActionValue.From(30)` のように作る。`Kind` で種類、`Number` / `Bool` / `Text` で値を読む。

状態同期のキー

| キー | 型 | 備考 |
|---|---|---|
| `RoomStateKeys.Volume`（`"volume"`） | int | 音量 |
| `RoomStateKeys.HeartRate`（`"heartRate"`） | int | 心拍数 |
| それ以外 | int / bool / string | 照明の色など、増やしてよい。`playing` は送らない（再生状態はトランスポートが正） |

## 7. サーバーに拒否されるとき

呼び出しが `RpcException` になる。`StatusCode` で見分ける。

| 状況 | `StatusCode` | 直し方 |
|---|---|---|
| roomId が空または空白 | `InvalidArgument` | 空でない roomId を渡す |
| `Time` が `HH:mm:ss:ff` でない（`"00:00:10"`、`"00:00:60:00"` など） | `InvalidArgument` | 4 区切り、各欄 0–23 / 0–59 / 0–59 / 0–99 |
| `Action` と `Value` の種類が合わない（`Play` に int など） | `InvalidArgument` | `Play`はbool、`VolumeChange`はint、`Effect`はstring |
| `Effect`の値が空または空白 | `InvalidArgument` | `EffectNames`にある演出名を入れる。サーバーは名前の存在までは検証しない |
| `volume` / `heartRate` に int 以外 | `InvalidArgument` | int を送る |
| 状態同期のキーが空 | `InvalidArgument` | キーを入れる |
| 参加する前に `PublishAsync` | `FailedPrecondition` | 先に `ConnectAsync`（参加）を終える |
| サーバーに届かない（未起動、ポート、`https://` にしている） | 接続や呼び出しが `RpcException` | `http://ホスト:5210` を確認 |

## 8. 決まりごと

- roomごとにデフォルト演出と追加予約を保持する。追加時は全件を検証し、1件でも不正なら追加しない。CANCELは追加予約だけを除く。
- 再生中に同じ時刻・Action・Valueを再通知しても、実行済みなら繰り返さない。同じ内容を再実行するときはCANCELしてからSCHEDULEする。Serverの件数には重複分も含まれる。
- 予約の時刻は曲の先頭からの位置。絶対時刻を入れても形式が同じなので拒否されず、別の時刻で発火する。
- 音量などの値の範囲（0〜100 など）はサーバーで検証しない。画面側で制限する。
- LiveSceneは各Clientの曲の位置を使う。通信遅延によるClient間の再生開始のずれは補正しない。
- サーバーの room はメモリ上にある。サーバーを再起動すると追加予約は消え、デフォルト演出から始まる。Hubの接続も切れる。
- 自動再接続は無い。切断を知るには Hub の `WaitForDisconnectAsync()` を待つ（`RoomClient` はまだ公開していないので、必要なら足す）。再接続は `DisposeAsync` してから `ConnectAsync` をやり直す。

## 9. 環境

| 項目 | 内容 |
|---|---|
| サーバーのアドレス | `http://ホスト:5210`。HTTP/2 だけを受ける。`Assets/Network/ServerConfig.asset` で設定 |
| 通信ライブラリ | MagicOnion 7.10.2（`Assets/Packages`）。シリアライズは MessagePack |
| 通信契約 | `Livisor.Shared`。Unityは`Packages/com.livisor.shared.unity`の埋め込みパッケージを参照。正本の編集後に親リポジトリで`make shared/sync`を実行 |
| チャネルの初期化 | `Assets/Scripts/MagicOnionInitializer.cs` がシーン読み込み前に行う。HTTP/2 専用の `YetAnotherHttpHandler` |
| IL2CPP（Meta Quest） | `Assets/Scripts/MagicOnionClientInitializer.cs` の `[MagicOnionClientGeneration(typeof(ITimelineService), typeof(IRoomStateHub))]` が必要。無いと接続時に落ちる。エディタでは無くても動くので気づきにくい |

## 10. 動作確認

サーバーを起動してから、Unity のメニューで実行する。結果は `SmokeTestResult.json` / `SmokeTestAdminResult.json`（Git 管理外）。

| メニュー | 確認する内容 |
|---|---|
| `Livisor/Tests/Timeline` | `Assets/Scenes/Sandbox.unity` の `TimelineReceiver` が再生・予約・音量を受けて反映する |
| `Livisor/Tests/Admin` | 管理者画面のボタンが送信し、応答で表示が変わる |
| `Livisor/Tests/Admin (all features)` | 管理者画面の全機能と、別接続の観客クライアントへの配信 |
| `Livisor/Tests/Live Effects` | Admin → Server → LiveSceneで紙吹雪の開始・停止、雷、銀テープ、デフォルトと複数追加予約の共存、再配信・取消・一時停止、音量を確認 |

起動メニューは`Assets/Tests/Editor/TestScenes.cs`、Play Mode中の検証処理は`Assets/Tests/Runtime/Test*Driver.cs`に置く。

LiveSceneのテストは`http://127.0.0.1:5210`のローカルサーバーを使い、約1分半で終了する。
接続先とroomをテスト中だけ変更し、デバイス通信を止める。シーンや設定アセットは保存しない。
結果は`Logs/live-effects-check.json`の`ok`・`checks`・`errors`で確認する。
テスト後はシーンを開き直し、一時設定を破棄する。

手で確かめるときは、Sandbox を再生してから Admin の画面で操作し、Console に次のログが出ることを見る。

```
[Receiver] joined room 'room1'
[Receiver] transport: playing=False actions=6  ← デフォルト6件を取得
[Receiver] transport: playing=True actions=6   ← PLAYの通知
```

## 11. よくある間違い

| 間違い | 何が起きるか | 直し方 |
|---|---|---|
| 通知のイベントの中で UI やシーンを触る | 別スレッドからの操作で例外や不定動作 | キューに積んで `Update` で反映する（4.2） |
| トランスポートが届くたびに実行済みの記録を消す | 予約が二重に発火する | 同じTimelineActionPlaybackへLoadする（4.4） |
| 予約の時刻に絶対時刻を入れる | 拒否されず、別の時刻で発火する | 曲の先頭からの位置で入れる |
| `Action`と値の型を取り違える | `InvalidArgument`で拒否 | `Play`はbool、`VolumeChange`はint、`Effect`はstring |
| 参加する前に音量を送る | `FailedPrecondition` で拒否 | `ConnectAsync` の完了を待つ |
| 状態同期に `playing` を送る | 拒否はされないが、再生状態が二重になり食い違う | 再生状態はトランスポートだけを見る |
| IL2CPP で `[MagicOnionClientGeneration]` に契約を書き忘れる | Quest 実機で接続時に落ちる。エディタでは動く | 使う契約をすべて列挙する |
| シーン開始からの経過時間で曲の演出を待つ | 曲の開始待ちや一時停止で演出がずれる | LiveSceneは`MusicTimeSeconds`を使う |

## 12. チェックリスト

- [ ] `ConnectAsync` の直後に `StateChanged` と `TransportChanged` が 1 回ずつ来ることを前提に初期表示を作った
- [ ] 通知はキューに積み、`Update` で反映している
- [ ] トランスポートが届くたびに一覧をLoadし、Playingで再生を切り替えている
- [ ] LiveSceneは曲の位置、音源のないSandboxはサーバーの経過時間と受信後の実時間でAdvanceを呼ぶ
- [ ] 予約の `Time` は曲の先頭からの位置を `HH:mm:ss:ff` で送っている
- [ ] `Play`はbool、`VolumeChange`はint、`Effect`は空白でないstringを入れている
- [ ] `VolumeChange` の発火後に `volume` を書き戻している
- [ ] 知らない状態キーを無視している
- [ ] `MagicOnionClientInitializer` に使う契約を列挙した
- [ ] 終了時に `DisposeAsync` している
