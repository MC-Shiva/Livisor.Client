# 銀テープ演出

Unity 6 / URP 用。1粒子を12分割の帯メッシュとして描き、頂点シェーダーで波とねじりを加えます。粒子ごとの乱数と経過率で動きを変え、非スケール時間でライブ停止後も完走します。

## 呼び出し

```csharp
// StageDirector は Resources/SilverStreamer.prefab を一度生成します。
stageDirector.FireSilverStreamers();

// 独立して使う場合はPrefabを配置し、そのコンポーネントを参照します。
streamers.SetSpreadArea(new Vector3(0, 2.5f, 4.5f), new Vector3(9, 7, 12));
streamers.Fire();
streamers.Clear();
```

`EndPerformance()` はライブ終了時に一度発火します。`StartMusic()` で終了時発火のラッチをリセットします。手動の `Fire()` は繰り返し利用でき、既存粒子は維持しつつ最大粒子数を守ります。コンポーネントが有効なPlay Mode中に呼んでください。

DemoScene: **T** = 手動発火、**E** = 終了して発火、S/P = ライブ再開/一時停止。銀テープはPによる停止中も動作します。

## 調整

`Resources/SilverStreamer.prefab` の Inspector でPlay Mode開始前に設定します。シーン固有の調整はPrefabをシーンに置き、StageDirectorの `Silver Streamers` に指定します。

- Launch Positions: 射出口のローカル座標。初期値は左右2箇所。
- Count Per Launcher / Max Particles: 1射出口100本、同時最大800本。
- Launch Height: 抵抗のない弾道の高さの目安。重力、抵抗、乱数により実際の高さは変わります。
- Area Center / Area Size: 範囲制限用の領域。Restrict To Areaがオンの場合に使用します。射出方向・広がりはLaunch Anglesで設定します。
- Restrict To Area: 帯の長さと変形量の余白を含め、範囲を外れる粒子を消します。壁に跳ね返る表現ではありません。射出口も余白を含めて範囲内に置いてください。高さもこのモードでは制限します。
- Floor Height: 補助床のローカル高さ。Use Fallback Floorが有効な場合のみ使用します。通常は実際の床のMeshColliderで衝突判定します。
- Sway: 軌道の横揺れ。Length / Width / Segments: 帯の寸法と分割数。
- Bend / Twist / Flutter Speed: GPU側の曲がり、ねじり、速度。

座標の単位はm。ルートのスケールは1、上下方向はワールドYに合わせてください。Gizmosで範囲と射出口を表示します。`SetSpreadArea` は実行中にも反映され、制限を有効にすると既存粒子にも適用されます。

帯は両面の不透明描画、影なし。反射プローブとメインライトを使用します。形状変形のCPU更新はなく、通常の床衝突はParticle SystemのCollisionモジュールで処理し、補助床・範囲の判定のみ再利用配列で粒子を走査します。Resources参照によりPrefabとシェーダーをビルドに含めます。

## 確認項目

1. DemoSceneでTを押し、左右から射出後に落下し、帯が曲がること。
2. Pの後にTを押し、停止中も帯の移動・形状変形が続くこと。
3. Eを2回押して自動発火が重複しないこと。その後Tでは追加発火できること。
4. Tを連打して800本を超えず、寿命経過で全粒子が消えること。
5. Prefabの範囲を変更し、Gizmosと飛散範囲を確認。制限ありでは範囲外の帯が残らないこと。
6. Quest実機の両眼描画とCPU/GPUフレーム時間を確認。必要なら本数と分割数を減らすこと。

C#はUnity 6000.3.11f1の参照アセンブリを用いてコンパイル確認。Unity Editorはライセンス初期化エラーで起動できず、シェーダーの実機コンパイル・見た目・Play Mode動作は未確認です。

## 調整済みの初期値

- 長さ0.325m（従来の半分）。曲がり幅も0.045mに半減して比率を維持。
- 前方はローカル+Z（観客側）。幅9m、奥行き12m、中心Z=4.5m。後端Z=-1.5mを保ち、前端を5.5mから10.5mへ拡大。範囲制限用の領域です。射出方向はLaunch Anglesで設定します。
- 重力倍率0.12（従来0.35）、寿命12秒（従来9秒）。
- URPの物理ベース金属反射を使用。Metallic=1、Smoothness=0.94。追加ライトと反射プローブに加え、暗いステージでも黒くならない銀色の補助光と疑似スタジオ反射を加算します。帯の曲がり・回転・視点に応じて明暗の帯と鋭いハイライトが動きます。シェーダーの Silver Fill / Glint Intensity で明るさを調整できます。

## 床をすり抜けないための実装・調整

### 実装

- Stage.prefabの `Hall`（ホール形状）と `Steps`（段差）、Visualizer.prefab（発光床）に、表示に使うメッシュと同じ形の **MeshCollider** を追加しました。Convex / Is Triggerはオフです。ステージの実行時生成時に一緒に配置されます。
- Particle Systemの **World / 3D / High品質** の衝突判定で、床に触れた粒子の寿命を0にします。床に積もらせる処理や反発はありません。
- 帯の回転とGPU変形を覆うようメッシュのBoundsを広げ、その判定球にも余白を付けています。先端が突き抜けるのを避ける保守的な判定なので、姿勢によって床より少し上で消えます。
- 再生前にPhysics.SyncTransforms()を呼ぶため、終了時のtimeScale=0でも生成済みの静的床位置を反映します。移動するRigidbody付きの床は対象外です。

### Inspectorでの調整

`Assets/SilverStreamer/Resources/SilverStreamer.prefab` のSilverStreamerControllerを選択し、**Play Mode開始前**に変更します。

| 項目 | 初期値 | 調整方法 |
| --- | --- | --- |
| Collision Layers | レイヤー20のみ | 新しい床を追加する場合、その床のLayerも含めます。見た目だけの床では衝突しないため、BoxColliderまたはMeshColliderも必要です。 |
| Collision Radius Scale | 1.05 | 先端がめり込むなら1.1〜1.2へ。早く消えすぎるなら1.0へ。最低1.0で変形分の余白は残します。 |
| Use Fallback Floor | オフ | Colliderのない領域にも無限の水平床を置いた扱いにしたいときだけオンにします。 |
| Floor Height | 0m | 補助床のローカルY。低い観客席や段差があるシーンでは、補助床が実際の床より上にならないよう注意してください。 |

床の高さ・段差そのものは `Stage.prefab` のHall / Steps、または `Visualizer.prefab` のTransformとメッシュで決まります。描画とColliderは同じTransformなので高さは追従します。新しい単純な床には厚みのあるBoxCollider、複雑な段差にはMeshColliderを使い、Is Triggerをオフにしてください。

発射口は床から判定球の半径以上離してください。発射直後に消える場合、Launch PositionsのYを少し上げます。衝突判定球はUnityのParticle System InspectorのScene表示でも確認できます。

### 動作確認

DemoSceneでTを押してステージ・客席へ落下させ、各床の上で消えることを確認します。Pで停止後にTを押した場合も同じ結果になること、段差の縁で貫通しないこと、発射直後に消えないことを確認してください。MeshCollider追加によるQuest実機の負荷とPlay Modeの見た目は未検証です。

## 射出角度と空中での広がり

PrefabのInspectorで設定します。既存の4箇所の射出口・Launch Height=20などの調整値は維持しています。

| 項目 | 初期値 | 内容 |
| --- | --- | --- |
| Launch Elevation | 60° | 水平からの仰角。小さくすると前に飛び、90°で真上。 |
| Launch Azimuth | 0° | ローカル+Zが0°、右が+90°、左が−90°。 |
| Horizontal Spread | 90° | 左右の全拡散角。中心から±45°。 |
| Vertical Spread | 24° | 上下の全拡散角。中心から±12°、最終仰角は0〜90°に制限。 |
| Air Spread Acceleration | 0.6m/s² | 粒子ごとに異なる横加速度。大きくすると空中で広がる。0で追加の拡散力なし。 |

初速の向きを角度から計算し、粒子ごとに方向をばらつかせます。上昇直後は空気抵抗を弱めて扇状の広がりを保ち、その後は横方向の力を加えて拡散させます。終盤は横方向の力を弱め、抵抗を増してふわふわ落下させます。粒子の移動は標準モジュールで処理するため床の衝突判定も継続します。

Launch Heightは中心方向の抵抗なしの高さの目安です。仰角を低くすると同じ高さのための初速は大きくなります。遠くへ飛びすぎる場合はLaunch Heightも下げてください。

角度と拡散角は次回Fireから反映されます。Air Spread AccelerationはPlay Mode開始前に調整してください。コードからは `streamers.SetLaunchAngles(55f, 15f); streamers.Fire();` と呼べます。Gizmosの中央線と4本の境界線で方向を確認できます。Area Center / Area Sizeは現在は範囲制限専用です。

確認: Tで広がりを確認し、Horizontal Spread=0 / Vertical Spread=0 / Air Spread Acceleration=0では直線的に射出されること、Azimuth=90では右へ射出されること、Pによる停止中も拡散し床で消えることを確認します。
