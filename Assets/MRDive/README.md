# MR Dive — パススルーから VR ライブへ飛び込む

Quest 3 のパススルー（現実が見えている状態）でボタンを押すと、アニメでよくある「VR 世界へダイブする」演出を挟んで
`Assets/Scenes/Main.unity`（ライブ会場）へ遷移する。演出は 3 パターン用意してある。

---

## 1. Unity で最初にやること

この一式は Unity を開かずに書いたが、`.meta` は既存プロジェクトと同じ形式で用意してあり、
GUID も採番済み（既存アセットとの衝突が無いことを確認済み）。そのまま開いて参照が壊れることはない。

手順は Editor メニューの `Livisor > MR Dive > ...` に入っている。上から順に実行する。

| メニュー | やること |
|---|---|
| **ダイブシーンを生成** | `Assets/Scenes/DiveEntry.unity` を作り、OVRCameraRig・演出 3 種・UI・ライティングを全部組む。既存ファイルがあれば上書き確認が出る |
| **Build Settings に登録** | `DiveEntry` を先頭、`Main` をその次に並べ替える。変更前後の一覧をダイアログで確認してから適用 |
| **シーンを検証** | シーンの点検。カメラ設定・パススルー・シェーダー 7 本の読み込み・Build Settings をまとめて確認し、Console に結果を出す |
| **実機設定を点検** | プロジェクト設定の点検。**シーンをどれだけ正しく組んでも、ここが未設定だと実機でパススルーが一切出ない**（後述） |
| **実機設定を修正** | 上の点検で見つかった必須項目を自動で直す。何を書き換えるか確認ダイアログが出る |

生成されたシーンを再生すると、目の前にパネルが出る。3 つのボタンのどれかを選ぶとダイブが始まり、`Main` へ遷移する。

### 操作方法

- **注視（ゲイズ）** — ボタンを 1.6 秒見つめると発火。コントローラが無くても操作できる
- **コントローラ** — トリガーまたは A/X ボタンで、注視中のボタンを即発火
- **Editor 上のデバッグ** — Space / Enter で発火、1 / 2 キーでも操作できる

---

## 2. 3 つの演出

| # | 名前 | 尺 | 画 |
|---|---|---|---|
| 1 | **PORTAL RIFT** | 3.4s | 正面の空間に縦の光の亀裂が走り、円形ポータルに開く。中は藍色の渦と星屑。ポータルが急接近して視界を覆い、白閃光で通過する |
| 2 | **DIGITAL DISSOLVE** | 3.6s | 走査線が現実を舐め、青いワイヤーグリッドが焼き付く。RGB 分離とブロックグリッチで現実が壊れ、データの奔流が流れ込んで黒へ落ちる |
| 3 | **LIQUID DIVE** | 3.8s | 波紋が広がり、部屋が水で満たされる。水面が頭を越えて沈み、気泡と光の揺らぎの中を深い藍へ降りていく。3 つの中で唯一ゆっくり進む |

各演出は `DiveEntry` シーンの `MR Dive/Transitions/` 配下にコンポーネントとして載っている。
Inspector に尺・色・密度などの調整項目が並んでいるので、実機で見ながら詰められる。

演出の終わり際の色は、遷移先でそのまま明けてくる（`ArrivalFadeColor`）。
PORTAL RIFT は白飛びから、DIGITAL DISSOLVE は黒から、LIQUID DIVE は深い藍から `Main` が現れる。

---

## 3. 実機に持っていく前のチェック

`シーンを検証` と `実機設定を点検` の 2 つを実行すれば、必要な確認はほぼ済む。

**シーン側**（`シーンを検証` が見る）

- CenterEyeAnchor の Camera が `Clear Flags = Solid Color` / `Background = (0,0,0,0)`（アルファ 0 の黒）
  — シーン生成時に設定済みだが、リグを差し替えたら確認する
- `OVRPassthroughLayer` の存在、`OVRManager.isInsightPassthroughEnabled`
- シェーダー 7 本が Resources から読めること、Build Settings の並び

**プロジェクト側**（`実機設定を点検` が見る。`実機設定を修正` で直せる）

- `OVRProjectConfig.insightPassthroughSupport` が `None` 以外
  — **これが最重要。`None` のままだと AndroidManifest に `com.oculus.feature.PASSTHROUGH` が入らず、実機で現実が一切見えない**
- `systemLoadingScreenBackground` が `ContextualPassthrough`（起動時の黒画面を避ける）
- Android ターゲットアーキテクチャが **ARM64**
- スプラッシュスクリーンが無効

**手で直すもの**（自動変更すると他機能に波及するので、点検では報告のみ）

- **OpenXR の "Meta XR Feature"** が Android ビルドターゲットで有効になっていること
  — `Project Settings > XR Plug-in Management > OpenXR (Android)`
- ビルドターゲットが Android になっていること

---

## 4. 構成

```
Assets/MRDive/
├── Scripts/
│   ├── Core/                    土台。演出からは独立していて、ここだけで完結する
│   │   ├── DiveDirector.cs      全体の仕切り。舞台道具を揃え、演出を 1 本走らせ、シーンを切り替える
│   │   ├── DiveOverlay.cs       視界を覆うレイヤーの置き場。全天球 / 正面の板を生やす
│   │   ├── PassthroughBridge.cs Meta SDK への依存をここ 1 点に閉じ込める（後述）
│   │   ├── DiveTransitionBase.cs 演出 1 パターンの基底
│   │   ├── DiveContext.cs       演出に渡す舞台道具一式
│   │   ├── DiveArrivalFade.cs   シーンを跨いで生き残り、遷移先で明けてくる
│   │   ├── DiveEase.cs          イージングと区間リマップ
│   │   └── MRDiveShaders.cs     シェーダーの読み込み口
│   ├── Transitions/             演出 3 種。Core にしか依存しない
│   ├── UI/                      ワールド空間パネルと注視ポインタ。全部コードで組み立てる
│   └── Input/XRDiveInput.cs     UnityEngine.XR の薄いラッパー
├── Resources/MRDive/Shaders/    演出シェーダー 7 本
├── Editor/MRDiveSceneBuilder.cs シーン生成・Build Settings 登録・検証
└── README.md
```

---

## 5. 設計で意図的にそうしてあるところ

**Meta SDK にリフレクション経由でしか触らない。**
`PassthroughBridge` だけが `OVRPassthroughLayer` / `OVRManager` を知っている。しかも型名を文字列で引く。
SDK が未インポートでもバージョンが変わってもプロジェクト全体のコンパイルは壊れず、SDK が見つからない環境では
「現実が見えている度合い」をカメラの背景色で代替して、Editor 上でも演出の進行を確認できる。
`hidden` と `isInsightPassthroughEnabled` は v203 ではプロパティではなく public フィールドなので、
プロパティとフィールドの両方を探すようになっている。

**フルスクリーン効果をポストプロセスでやらない。**
`OnRenderImage` 方式は Single Pass Instanced と相性が悪く、Built-in RP の VR では壊れやすい。
代わりに「頭を包む半径 5m の球」を置いて、その内側にシェーダーで描いている。
球を大きく取るのは、目の間隔 6cm に対して十分遠くして左右の視差を消すため。近い板は VR では目が疲れる。
深度は `ZTest Always` なので、遠くに置いても必ず最前面に出る。

**UI をプレハブにしない。**
`DiveLaunchPanel` が実行時に Canvas からボタンまで全部組み立てる。
シーンが壊れても、コンパイルさえ通れば Editor メニューから作り直せる。

**遷移先を演出中に先読みする。**
`Main` は Timeline とキャラクターで重いので、`LoadSceneAsync` を `allowSceneActivation = false` で走らせておき、
演出が終わった瞬間に切り替える。演出の尺がそのままロード時間の隠れ蓑になる。

---

## 6. 演出を足すには

`DiveTransitionBase` を継承して `DiveEntry` シーンの `MR Dive/Transitions/` の下に置くだけでよい。
`DiveDirector` は子から自動で集めるし、UI のボタンもその数だけ自動で増える。

```csharp
public sealed class MyTransition : DiveTransitionBase
{
    public override string DisplayName => "MY DIVE";
    public override string Tagline => "一行の説明";
    public override float Duration => 3f;
    public override Color AccentColor => Color.cyan;
    public override Color ArrivalFadeColor => Color.black;

    OverlayLayer _sky;

    public override void OnPrepare(DiveContext ctx)
    {
        _sky = ctx.Overlay.AddSphere(MRDiveShaders.Load(MRDiveShaders.Fade));
    }

    public override IEnumerator Run(DiveContext ctx)
    {
        yield return Sweep(t =>
        {
            _sky.Alpha = Span(t, 0.3f, 1f);          // 区間 0.3〜1.0 を 0→1 に伸ばす
            ctx.Passthrough.Opacity = 1f - DiveEase.InOutCubic(t);
        });
    }

    public override void OnCleanup(DiveContext ctx) { }
}
```

シェーダーを足す場合は `Assets/MRDive/Resources/MRDive/Shaders/` に置き、`MRDiveShaders` に定数を足す。
Resources に置いているのは、シーンから参照されないシェーダーがビルドから落ちるのを防ぐため。
既存の `MRDive_Fade.shader` が最小の雛形になっている。Single Pass Instanced のマクロは 1 つでも欠けると右目が壊れるので、
必ず雛形からコピーすること。

---

## 7. 既知の制約

### 遷移先 `Main.unity` は現状 Quest では動かない（要判断）

`Main.unity` は Sony 空間再現ディスプレイ（SRDisplayUnityPlugin）向けに組まれたシーンで、

- ネイティブプラグインが `Assets/SRDisplayUnityPlugin/Plugins/x86_64/xr_runtime_unity_wrapper.dll` の **Windows x86_64 版 1 本のみ**。
  Android（Quest）では `DllNotFoundException` になる
- `OVRCameraRig` が無いので、着地後はヘッドトラッキングも VR カメラも効かない
- `MainCamera` タグは `SRDisplayManager` プレハブ内のカメラに付いている（着地フェード自体は動く）

ダイブ演出そのものは遷移先に依存しないので、**取りうる選択肢は 3 つ**:

1. Quest 用のライブ会場シーンを別途用意し、`DiveDirector` の `targetSceneName` をそれに向ける（推奨。変更はこの 1 項目だけ）
2. `Main.unity` に `OVRCameraRig` を足し、`SRDisplayManager` を Android では無効化する
3. デモで演出だけ見せるなら `DiveDirector` の `loadTargetScene` を外す（遷移せず、演出後に現実へ戻る）

### その他

- **パススルー映像そのものには触れない。** Quest のパススルーはコンポジタ側で合成されるので、シェーダーからサンプルできない
  （`GrabPass` も効かない）。LIQUID DIVE の「屈折」は明暗のリングによる擬似表現
- **演出中に頭を大きく動かすと、全天球に固定した模様との関係がずれる。** 演出は 3〜4 秒なので実用上は問題にならない
- **日本語テキストは Quest OS のフォントフォールバックに依存する。** レガシー `Text` + 組み込みフォントを使っているため、
  実機で豆腐（□）になっていないかを最初に確認すること。なった場合は日本語フォントを同梱して差し替える
- 全天球レイヤーが 2 枚重なる区間が各パターンに 0.5〜2 秒ある。Quest 3 のフィルレートで厳しければ、
  各 Transition の Inspector で密度系のパラメータを下げる
