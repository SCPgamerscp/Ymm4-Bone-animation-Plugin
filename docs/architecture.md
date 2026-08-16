# アーキテクチャ

このドキュメントは、プラグイン内部の構造・座標系・評価パイプラインの詳細を説明します。
プラグインを改造したい方、挙動の理由を知りたい方向けです。

---

## 1. レイヤ構成

```
┌──────────────────────────────────────────────┐
│ Views/     WPF編集UI（TreeView・差分一覧・テンプレートIO）│
├──────────────────────────────────────────────┤
│ Shape/     YMM4プラグインAPI連携                     │
│            IShapePlugin / ShapeParameterBase        │
│            IShapeSource2 / Animatable               │
├──────────────────────────────────────────────┤
│ Rendering/ Direct2D画像キャッシュ                    │
├──────────────────────────────────────────────┤
│ Core/      ★ YMM4・Direct2D非依存の純粋ロジック       │
│            ボーン階層 / FK / IK / 物理 / 差分選択 / JSON │
└──────────────────────────────────────────────┘
```

**依存の方向は上から下への一方向**です。`Core/` は上位レイヤを一切知りません。

### なぜCore層を分離したか

1. **テスト可能にするため**
   YMM4のDLLはWindows上のYMM4インストールフォルダにしか存在せず、
   Direct2Dの初期化にはGPUデバイスが必要です。
   これらに依存したままでは、CI環境やLinuxサンドボックスでロジックを検証できません。

   Core層はYMM4・Vortice・WPFを一切参照しないため、
   テストプロジェクトが `Core/**/*.cs` を直接 `<Compile Include>` するだけで
   `net8.0` としてビルド・実行できます（実際にLinux上で56件のテストが通っています）。

2. **バグを早期に発見するため**
   実際に、テストによって以下の2つの実バグが検出・修正されました。
   - IKの肘の回転符号が逆で、目標と正反対の方向へ腕が伸びていた
   - `Math.Max(NaN, x)` が `NaN` を返す性質により、デルタタイムがNaNのとき
     物理演算の全状態がNaNに汚染されていた

3. **YMM4のAPI変更から隔離するため**
   YMM4のプラグインAPIが変わっても、影響範囲は `Shape/` と `Views/` に限定されます。

---

## 2. 座標系と変換

### 2.1 単位と向き

| 項目 | 規約 |
| --- | --- |
| 位置 | ピクセル（px） |
| 回転 | 度（degree）。内部計算時のみラジアンへ変換 |
| 角度0度の向き | **+X方向（右）** |
| 回転の正方向 | Direct2Dの座標系に従う |
| 拡大率 | UI上は%（100 = 等倍）、Core層では倍率（1.0 = 等倍） |

### 2.2 ボーンの構造

各ボーンは「原点（Origin）」と「先端（Tip）」を持ちます。

```
     Origin                       Tip
        ●━━━━━━━━━━━━━━━━━━━━━▶
        └──────── Length ────────┘
```

- **Origin** … そのボーンのローカル原点。回転の中心
- **Tip** … `Origin` からローカル+X方向へ `Length` だけ進んだ点
- **子ボーンは親の Tip に接続される**

### 2.3 ローカル行列の合成

`BonePose.ToMatrix()` は次の順序で行列を作ります。

```
local = Scale × Rotation × Translation
```

`System.Numerics.Matrix3x2` は行ベクトル（`v * M`）規約なので、
この掛け順で「拡大 → 回転 → 平行移動」の順に適用されます。

### 2.4 ワールド行列の合成

```csharp
// 子は親の「先端」に接続されるため、親の長さぶんの平行移動を挟む
var connection = Matrix3x2.CreateTranslation(parent.Bone.Length, 0f);
transform.World = local * connection * parent.World;
```

ルートボーンの場合は `connection` も `parent.World` も不要なので、
`transform.World = local` となります。

### 2.5 Squash & Stretch

`Stretch` は**体積を保ったまま伸び縮み**させる値です。
ボーンの軸方向（X）に `S` 倍したら、垂直方向（Y）は `1/√S` 倍します。

```csharp
var stretchX = pose.Stretch;
var stretchY = pose.Stretch <= 0f ? 1f : 1f / (float)Math.Sqrt(pose.Stretch);
var scale = Matrix3x2.CreateScale(pose.ScaleX * stretchX, pose.ScaleY * stretchY);
```

`Stretch = 4` なら横4倍・縦1/2倍になり、面積は `4 × 0.5 = 2`……
ではなく、2D表現としての見た目の自然さを優先し `1/√S` を採用しています。
（厳密な面積保存なら `1/S` ですが、それでは潰れすぎて絵として破綻します）

`Stretch <= 0` のときは0除算・√の定義域外になるため `1` にフォールバックします。

### 2.6 描画時のアンカーポイント適用

画像はアンカーポイントが原点に来るよう、描画直前にオフセットします。

```csharp
var anchorOffset = Matrix3x2.CreateTranslation(
    -size.Width  * bone.AnchorPoint.X,
    -size.Height * bone.AnchorPoint.Y);

dc.Transform = anchorOffset * transform.World;
```

`AnchorPoint` は正規化座標（0〜1）です。画像サイズが変わっても
相対位置が保たれるため、差分画像のサイズが揃っていなくても破綻しません。

---

## 3. 評価パイプライン

`SkeletonEvaluator.Evaluate(skeleton, poseProvider, context)` が
毎フレーム以下の4段階を実行します。

```
     ┌─────────┐
     │ 1. FK   │  トポロジカル順に親→子でワールド行列を合成
     └────┬────┘
          ▼
     ┌─────────┐
     │ 2. IK   │  IK有効なチェーンを解き、影響度でFK結果とブレンド
     └────┬────┘
          ▼
     ┌─────────┐
     │ 3. 物理 │  揺れもの設定のあるボーンへ減衰バネを適用し子孫へ伝播
     └────┬────┘
          ▼
     ┌─────────┐
     │4.Z順ソート│  奥→手前へ安定ソート
     └─────────┘
```

### 3.1 FK（順運動学）

`Skeleton.GetTopologicalOrder()` が「親が必ず子より先に来る」順序を返すため、
一度のループでワールド行列を確定できます。

不透明度も同時に累積されます。

```csharp
transform.Opacity = parent.Opacity * pose.Opacity;
```

親を半透明にすると子もまとめて半透明になる、直感的な挙動になります。

> **トポロジカル順序の安全策**: データが壊れて循環参照が残っていた場合、
> 素朴な実装では無限ループします。`GetTopologicalOrder()` は
> 訪問済み集合で管理し、走査後に**未訪問のボーンを末尾へ追加**することで
> 必ず全ボーンを返して停止します。

### 3.2 IK（逆運動学）

`IkSolver` は2つのアルゴリズムを使い分けます。

| チェーン長 | アルゴリズム | 理由 |
| --- | --- | --- |
| 2 | **解析解（余弦定理）** | 腕・脚は2ボーンがほとんど。反復不要で厳密かつ高速 |
| 3以上 | **CCD法** | 尻尾・触手など任意長に対応。反復回数は `Iterations` |

#### 2ボーン解析解

三角形の3辺（`upperLength`, `lowerLength`, `distance`）から余弦定理で角度を求めます。
到達可能性によって3つに分岐します。

```csharp
if (distance >= maxReach)      { rootAngle = 0f; elbowAngle = 0f; }    // 届かない → まっすぐ伸ばす
else if (distance <= minReach) { rootAngle = 0f; elbowAngle = 180f; }  // 近すぎる → 折りたたむ
else { /* 余弦定理で厳密に計算 */ }
```

> **過去のバグ**: 当初は `distance` を `[minReach, maxReach]` にクランプしてから
> 余弦定理に入れていましたが、届かない場合に微小な角度が残り
> 先端が目標方向からずれていました。明示的な分岐に書き換えて解決しています。

肘の回転は**負号**を付けます。
これは根本を目標方向へ向けたあと、肘を逆向きに折る必要があるためです。

```csharp
var newEndRotation = MathHelper.NormalizeDegrees(-elbowAngle * bend);
```

`bend` は `FlipBend` に応じた `+1 / -1` で、肘・膝の曲がる向きを切り替えます。

#### CCD法

末端から根本へ向かって各関節を順に「先端が目標を向く」よう回転させる操作を繰り返します。
`(Tip - Target).LengthSquared() < 0.01f` になった時点で早期終了します。

#### 影響度ブレンド

`ApplyRotation` がFK結果とIK結果を `Weight` で線形補間します。
`Weight = 0` なら完全にFK、`1` なら完全にIKです。
角度の補間には `MathHelper.LerpDegrees`（最短経路）を使うため、
179度と-179度の間で大回りしません。

### 3.3 物理演算（揺れもの）

`PhysicsSimulator` は**減衰バネ（damped spring）**モデルです。
各ボーンは以下の状態を保持します。

```csharp
class State {
    float   Angle;                 // 現在の揺れ角度
    float   Velocity;              // 角速度
    Vector2 LastParentPosition;    // 前フレームの親位置
    float   LastParentRotation;    // 前フレームの親回転
    bool    IsInitialized;
}
```

#### 力の計算

親の移動量のうち、**ボーンの軸に対して垂直な成分だけ**が揺れの力になります。

```
         親の移動 →→→→
              │
    ┌─────────┼─────────┐
    │  平行成分 │ 垂直成分 │
    │  (無視)  │ (揺れる) │
    └─────────┴─────────┘
```

これは物理的に正しい挙動です。髪の毛が伸びている方向へ引っ張っても
横には揺れません。実際にこの性質を確認するテスト
（`Physics_DoesNotSway_WhenParentMovesAlongBoneAxis`）を用意しています。

#### 発散への防御

デルタタイムは必ずクランプします。

```csharp
var rawDeltaTime = context.DeltaTime;
// Math.Max(NaN, x) は NaN を返すため、先に明示的に弾く
if (double.IsNaN(rawDeltaTime) || double.IsInfinity(rawDeltaTime))
    rawDeltaTime = 1.0 / 60.0;
var dt = (float)Math.Min(Math.Max(rawDeltaTime, 1e-4), 0.1);
```

`dt` が大きすぎるとオイラー積分が発散し、`0` だと0除算になります。
上限0.1秒・下限0.0001秒に制限することで、
極端に低いフレームレートでも数値的に安定します。

角度が `AngleLimit` を超えた場合は、壁に当たったように反発させます。

```csharp
state.Velocity *= -0.3f;   // 反発係数
```

#### シーク時のリセット

`BoneShapeSource.Update()` はフレーム番号の連続性を見ています。

```csharp
if (frame != lastFrame + 1)
    evaluator.ResetPhysics();
```

タイムラインを飛ばした場合、前フレームの親位置との差が巨大になり
髪が吹き飛びます。連続再生でない移動を検出したらリセットします。

### 3.4 Z順ソート

描画順は**安定ソート**で決定します。

```csharp
.OrderBy(x => x.t.ZOrder).ThenBy(x => x.index)
```

同じZ順のボーンはボーンリストの並び順（＝TreeViewの上下）どおりに描画されます。
`ThenBy(index)` を入れないとLINQの `OrderBy` は安定ですが、
意図を明示するために書いています。

`ZOrder` はアニメーション可能なので、キーフレームで
「腕を体の後ろへ回す」といった前後の入れ替えができます。

---

## 4. 差分画像の選択

`SlotSelector` が使用するスロットを決めます。優先順位は次のとおりです。

```
1. 手動指定（差分番号）  ← 口パク・目パチが両方無効なボーンのみ
2. 口パク連動
3. 目パチ連動
```

### 目パチの決定性

まばたきは**同じ時刻なら必ず同じ結果**になるよう実装されています。

```csharp
// 疑似乱数はシードとサイクル番号から決定的に生成する
float PseudoRandom(int seed, int cycle) { ... }
```

`Random` インスタンスの状態に依存すると、シークするたびに
まばたきのタイミングが変わってしまい、プレビューと出力が一致しません。
シードとサイクル番号のみから計算することで再現性を保証しています。

まばたきの開閉カーブは三角波です（閉じる → 開く）。

---

## 5. Direct2Dリソース管理

プレビュー中、`IShapeSource.Update()` は**絶えず呼び出されます**。
ここでリソースを毎回確保するとすぐにパフォーマンスが破綻します。

### 5.1 使い回すもの（コンストラクタで生成）

| リソース | 用途 |
| --- | --- |
| `ID2D1SolidColorBrush` ×3 | ボーン線・関節・IKターゲットのガイド描画 |
| `Effects.Opacity` | 不透明度の適用 |
| `ID2D1Image` (opacityOutput) | 上記エフェクトの出力 |

### 5.2 毎フレーム作り直すもの

| リソース | 理由 |
| --- | --- |
| `ID2D1CommandList` | 内容が毎フレーム変わるため。作り直す前に必ず `Dispose()` |

### 5.3 YMM4/Direct2Dの必須ルール

```csharp
dc.Target = commandList;
dc.BeginDraw();
// ... 描画 ...
dc.EndDraw();
dc.Target = null;     // ← Targetは必ずnullに戻す
commandList.Close();  // ← EndDrawの後に必ずCloseする
```

これを守らないとYMM4本体の描画が壊れます。

**Effectの注意点**が2つあります。

```csharp
// 1. Effect.Output はゲッターを呼ぶたびに新しい参照を返す。
//    Effect側では解放されないので、取得した側が必ずDisposeする。
opacityOutput = opacityEffect.Output;   // コンストラクタで1回だけ取得
// ...
opacityOutput.Dispose();                // Disposeで解放

// 2. Effectが入力画像を掴んだままにならないよう、使い終わったらnullを入れる
opacityEffect.SetInput(0, null, true);
```

### 5.4 画像キャッシュ

`BoneImageCache` はファイルパスをキーに `IImageFileSource` をキャッシュします。

- キーは**大文字小文字を区別しない**（Windowsのパス規約に合わせる）
- **読み込み失敗もキャッシュする**
  存在しないファイルを毎フレーム開こうとするとプレビューが固まるため
- `TrimExcept(keepPaths)` で、参照されなくなった画像を解放

---

## 6. YMM4連携の実装ポイント

### 6.1 キーフレーム対応

キーフレーム補間したいプロパティは `Animation` 型にし、
`GetAnimatables()` から返す必要があります。

```csharp
// BoneItem.cs
protected override IEnumerable<IAnimatable> GetAnimatables()
    => [X, Y, Rotation, ScaleX, ScaleY, Stretch, Opacity, ZOrder,
        SlotIndex, IkTargetX, IkTargetY, IkWeight];

// BoneShapeParameter.cs — ボーン自体がAnimatableなのでそのまま返す
protected override IEnumerable<IAnimatable> GetAnimatables() => Bones;
```

値の取得は `animation.GetValue(frame, length, fps)` です。

### 6.2 引数なしコンストラクタ

`ShapeParameterBase` の派生クラスには**必ず引数なしコンストラクタが必要**です。
これがないとプロジェクトファイルの読み込みに失敗します。

```csharp
public BoneShapeParameter() : this(null) { }
```

### 6.3 複数アイテム選択への対応

YMM4では複数のアイテムを同時に選択して編集できます。
カスタムエディタは受け取った `ItemProperty[]` の**すべて**へ値を書き込みます。

```csharp
void Commit(List<BoneItem> updated)
{
    foreach (var property in properties)
        property.SetValue(/* 各アイテムごとにコピーを作る */);
}
```

**重要**: 各アイテムには必ず**別インスタンスのコピー**を渡します。
同じ `Animation` インスタンスを共有すると、片方を編集したときに
もう片方も変わってしまいます。

### 6.4 SharedData

図形の種類を切り替えて戻したときに設定が消えないよう、
`SaveSharedData` / `LoadSharedData` で一時保存します。
ここでも `Animation` は参照ではなくコピーを保持します。

### 6.5 プレビュー制御点

`IShapeSource2` を実装すると `Controllers` プロパティが使えます。

```csharp
new ControllerPoint(
    new Vector3(origin.X, origin.Y, 0),
    arg => {
        // arg.Delta はワールド座標系での移動量
        var delta = ToLocalDelta(new Vector2(arg.Delta.X, arg.Delta.Y), transform);
        item.X.AddToEachValues(delta.X);
        item.Y.AddToEachValues(delta.Y);
    });
```

親が回転していると、ワールド座標のドラッグ量をそのまま
ローカル座標の `X`/`Y` に足すと見た目と違う方向へ動きます。
`ToLocalDelta` がワールド行列の**線形部分のみ**（平行移動を除く）を
逆変換することで、見た目どおりにドラッグできるようにしています。

```csharp
var linear = new Matrix3x2(world.M11, world.M12, world.M21, world.M22, 0, 0);
var inverted = MathHelper.InvertOrIdentity(linear);
var local = Vector2.Transform(worldDelta, inverted);
```

`InvertOrIdentity` は逆行列が存在しない（スケール0など）場合に
単位行列を返すため、例外で落ちません。

### 6.6 口パク値の取得

YMM4のバージョンによって口パク情報の取得手段が異なるため、
リフレクションで候補プロパティを順に探索し、
見つからなければ0（口を閉じた状態）にフォールバックします。

```csharp
foreach (var name in new[] { "Kuchipaku", "LipSync", "VoiceVolume" })
{
    var property = type.GetProperty(name);
    if (property?.GetValue(description) is double value)
        return Math.Clamp(value, 0.0, 1.0);
}
```

例外は握りつぶします。口パクが動かないだけで済ませ、
プラグイン全体をクラッシュさせないためです。

---

## 7. 循環参照への対策

TreeViewのドラッグ＆ドロップでは、簡単に「自分の子孫を自分の親にする」
操作ができてしまいます。これを放置すると無限ループでYMM4ごとフリーズします。

3段階で防御しています。

### 段階1: 変更時に拒否する

```csharp
// Skeleton.SetParent()
if (boneId == newParentId) return false;              // 自己参照
if (IsDescendantOf(newParentId, boneId)) return false; // 循環
```

`BoneTreeEditorViewModel.SetParent()` はCore層で検証してから
初めて実際のプロパティへ書き込みます。
UI側は戻り値が `false` ならメッセージを出して変更を取り消します。

### 段階2: ツリー構築時に検出する

プロジェクトファイルが壊れていた場合に備え、`RebuildTree()` は
`FindCircularIds()` で循環しているボーンを事前に洗い出し、
それらを強制的にルート扱いにします。ツリー構築がハングしません。

### 段階3: 評価時に停止を保証する

`GetTopologicalOrder()` は未訪問ボーンを末尾に追加するため、
どんな壊れたデータでも必ず全ボーンを返して終了します。

---

## 8. 主要クラス早見表

### Core

| クラス | 責務 |
| --- | --- |
| `MathHelper` | 角度正規化・最短経路補間・行列分解・NaN判定 |
| `BonePose` | 1フレーム分のローカル姿勢（構造体）。`ToMatrix()` / `Sanitized()` |
| `BoneDefinition` | ボーンの静的定義。アニメーション値は持たない |
| `Skeleton` | ボーン集合と階層。`SetParent` / `GetTopologicalOrder` / `GetChain` |
| `BoneTransform` | 評価結果。`World` / `Origin` / `Tip` / `ZOrder` / `ActiveSlot` |
| `SkeletonEvaluator` | 4段階パイプラインの実行 |
| `EvaluationContext` | 時刻・デルタタイム・口パク値・各種有効フラグ |
| `IkSolver` | 2ボーン解析解 + CCD |
| `PhysicsSimulator` | 減衰バネ。ボーンごとに状態を保持 |
| `SlotSelector` | 差分画像の選択 |
| `SkeletonTemplate` | JSON永続化 |

### Shape

| クラス | 責務 |
| --- | --- |
| `BoneShapePlugin` | `IShapePlugin`。YMM4への登録 |
| `BoneShapeParameter` | `ShapeParameterBase`。設定項目とSharedData |
| `BoneShapeSource` | `IShapeSource2`。Direct2D描画とプレビュー制御点 |
| `BoneItem` | `Animatable`。キーフレーム対応のボーン設定 |
| `BoneImageSlot` | `Animatable`。差分画像1枚分 |

### Views

| クラス | 責務 |
| --- | --- |
| `BoneTreeEditor` | ドラッグ＆ドロップ対応TreeView（View） |
| `BoneTreeEditorViewModel` | ボーンの追加・削除・並べ替え・親子繋ぎ替え |
| `BoneTreeNodeViewModel` | ツリーの1ノード。展開・選択状態を保持 |
| `ImageSlotEditor` (+VM) | 差分画像スロット一覧 |
| `TemplateIoEditor` | JSONテンプレートの保存・読み込み |
| `*Attribute` | `PropertyEditorAttribute2` の実装。UIとプロパティを結びつける |
