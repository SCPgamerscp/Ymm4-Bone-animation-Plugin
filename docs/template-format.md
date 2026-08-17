# JSONテンプレート フォーマット仕様

「テンプレート」欄の `保存` / `読み込み` で扱うJSONファイルの仕様です。
手書きで編集したり、スクリプトで生成したりする場合の参考にしてください。

対応する実装は [`Ymm4-Bone-animation-Plugin/Core/SkeletonTemplate.cs`](../Ymm4-Bone-animation-Plugin/Core/SkeletonTemplate.cs) です。

---

## 1. 全体構造

```json
{
  "Version": 1,
  "Name": "MyCharacter",
  "Bones": [
    { "Id": "...", "Name": "体", "ParentId": null, ... },
    { "Id": "...", "Name": "頭", "ParentId": "...", ... }
  ]
}
```

| フィールド | 型 | 既定値 | 説明 |
| --- | --- | --- | --- |
| `Version` | int | `1` | テンプレート形式のバージョン |
| `Name` | string | `"Skeleton"` | テンプレート名 |
| `Bones` | array | `[]` | ボーンの配列（フラット。階層は `ParentId` で表現） |

**ボーンの階層はネストではなくフラットな配列**で表現し、
親子関係は `ParentId` の参照で表します。

> **配列の順序は親子関係に影響しません。**
> 読み込み処理は「まず全ボーンを親なしで登録し、その後に親子関係を張り直す」
> という2パス方式なので、子が親より先に書かれていても正しく復元されます。

---

## 2. ボーン（`Bones[]` の要素）

```json
{
  "Id": "a1b2c3d4e5f6...",
  "Name": "上腕",
  "ParentId": "0011223344...",
  "Length": 100.0,
  "AnchorX": 0.5,
  "AnchorY": 0.5,
  "BaseZOrder": 5,
  "ImageSlots": [ ... ],
  "Physics": { ... },
  "LipSync": { ... },
  "Blink":   { ... },
  "Ik":      { ... }
}
```

| フィールド | 型 | 既定値 | 説明 |
| --- | --- | --- | --- |
| `Id` | string | 自動生成 | 一意なID。空文字の場合は読み込み時にGUIDが自動採番されます |
| `Name` | string | `"Bone"` | UI表示名 |
| `ParentId` | string / null | `null` | 親ボーンのID。ルートは `null` または省略 |
| `Length` | float | `100.0` | ボーンの長さ(px)。子はこの先端に接続されます |
| `AnchorX` | float | `0.5` | 画像内の回転中心X（0=左端, 1=右端） |
| `AnchorY` | float | `0.5` | 画像内の回転中心Y（0=上端, 1=下端） |
| `BaseZOrder` | int | `0` | 基準の描画順。大きいほど手前 |
| `ImageSlots` | array | `[]` | 差分画像スロット |
| `Physics` | object / null | `null` | 揺れもの設定。`null` で無効 |
| `LipSync` | object / null | `null` | 口パク設定。`null` で無効 |
| `Blink` | object / null | `null` | 目パチ設定。`null` で無効 |
| `Ik` | object / null | `null` | IK設定。`null` でFKのみ |

> **`Vector2` を展開している理由**
> `AnchorPoint` や IK の `Target` は内部では `System.Numerics.Vector2` ですが、
> JSON上では `AnchorX` / `AnchorY`、`TargetX` / `TargetY` に分解しています。
> `Vector2` を直接シリアライズすると .NET のバージョンによって
> 出力形式が変わる可能性があるため、互換性を優先しました。

### 保存されないもの

テンプレートに含まれるのは**静的な構造**のみです。
以下は保存されません。

- キーフレーム / アニメーションの値（`X`, `Y`, `回転`, `不透明度` など）
- 全体設定（`物理演算を有効化`, `目パチシード`, `ボーンを表示` など）

テンプレートは「骨組みの再利用」を目的としているためです。

---

## 3. `ImageSlots[]`

```json
"ImageSlots": [
  { "Name": "閉じ",   "FilePath": "C:\\char\\mouth_close.png" },
  { "Name": "半開き", "FilePath": "C:\\char\\mouth_half.png" },
  { "Name": "開き",   "FilePath": "C:\\char\\mouth_open.png" }
]
```

| フィールド | 型 | 既定値 | 説明 |
| --- | --- | --- | --- |
| `Name` | string | `"Default"` | スロット名。`LipSync` / `Blink` の `SlotNames` から参照されます |
| `FilePath` | string | `""` | 画像ファイルのパス |

> **`FilePath` は絶対パスで保存されます。**
> 別のPCや別のフォルダ構成でテンプレートを読み込むと画像が見つかりません。
> その場合は読み込み後に各スロットのファイルを選び直してください。
> JSONをテキストエディタで一括置換するのも有効です。

---

## 4. `Physics`（揺れもの）

```json
"Physics": {
  "Stiffness":  12.0,
  "Damping":     3.5,
  "Inertia":     1.0,
  "Gravity":     0.0,
  "AngleLimit": 45.0
}
```

| フィールド | 型 | 既定値 | 説明 |
| --- | --- | --- | --- |
| `Stiffness` | float | `12.0` | バネの強さ。大きいほど元の姿勢へ速く戻る |
| `Damping` | float | `3.5` | 減衰。大きいほど揺れが速く収まる |
| `Inertia` | float | `1.0` | 慣性。親の動きをどれだけ揺れへ変換するか |
| `Gravity` | float | `0.0` | 重力による垂れ下がりの強さ |
| `AngleLimit` | float | `45.0` | 揺れ角度の上限(度)。超えると反発します |

このオブジェクト自体が存在する（`null` でない）ことが「物理演算が有効」を意味します。

---

## 5. `LipSync`（口パク）

```json
"LipSync": {
  "SlotNames": ["閉じ", "半開き", "開き"],
  "ScaleInfluence": 0.0
}
```

| フィールド | 型 | 既定値 | 説明 |
| --- | --- | --- | --- |
| `SlotNames` | string[] | `[]` | 使用するスロット名。**閉じた状態から順**に並べます |
| `ScaleInfluence` | float | `0.0` | 口の開き具合を縦スケールへ反映する量。0で無効 |

`SlotNames` の各要素は `ImageSlots[].Name` と一致させてください。

---

## 6. `Blink`（目パチ）

```json
"Blink": {
  "IntervalSeconds": 4.0,
  "DurationSeconds": 0.16,
  "SlotNames": ["開き", "半目", "閉じ"]
}
```

| フィールド | 型 | 既定値 | 説明 |
| --- | --- | --- | --- |
| `IntervalSeconds` | float | `4.0` | まばたきの間隔(秒) |
| `DurationSeconds` | float | `0.16` | まばたき1回の長さ(秒) |
| `SlotNames` | string[] | `[]` | 使用するスロット名。**開いた状態から順**に並べます |

---

## 7. `Ik`（逆運動学）

```json
"Ik": {
  "IsEnabled": true,
  "ChainLength": 2,
  "TargetX": 120.0,
  "TargetY": 80.0,
  "FlipBend": false,
  "Iterations": 12,
  "Weight": 1.0
}
```

| フィールド | 型 | 既定値 | 説明 |
| --- | --- | --- | --- |
| `IsEnabled` | bool | `false` | IKを有効にするか |
| `ChainLength` | int | `2` | チェーンに含めるボーン数。`2` で解析解、`3`以上でCCD法 |
| `TargetX` | float | `0.0` | ターゲット位置X（初期値。実際はキーフレームで上書きされます） |
| `TargetY` | float | `0.0` | ターゲット位置Y |
| `FlipBend` | bool | `false` | 肘・膝の曲がる向きを反転 |
| `Iterations` | int | `12` | CCD法の反復回数（`ChainLength` が3以上のとき使用） |
| `Weight` | float | `1.0` | FKとIKのブレンド率（0〜1） |

> `TargetX` / `TargetY` / `Weight` は実行時にはキーフレームの値で
> 上書きされます。テンプレート上の値は初期値としての意味しか持ちません。

---

## 8. 完全な例

体 → 頭 → 髪、および 体 → 上腕 → 前腕（IK） という構成です。

```json
{
  "Version": 1,
  "Name": "SimpleCharacter",
  "Bones": [
    {
      "Id": "body",
      "Name": "体",
      "ParentId": null,
      "Length": 250.0,
      "AnchorX": 0.5,
      "AnchorY": 0.9,
      "BaseZOrder": 0,
      "ImageSlots": [
        { "Name": "通常", "FilePath": "C:\\char\\body.png" }
      ]
    },
    {
      "Id": "head",
      "Name": "頭",
      "ParentId": "body",
      "Length": 80.0,
      "AnchorX": 0.5,
      "AnchorY": 0.9,
      "BaseZOrder": 10,
      "ImageSlots": [
        { "Name": "通常", "FilePath": "C:\\char\\head.png" }
      ]
    },
    {
      "Id": "hair",
      "Name": "後ろ髪",
      "ParentId": "head",
      "Length": 150.0,
      "AnchorX": 0.5,
      "AnchorY": 0.1,
      "BaseZOrder": -20,
      "ImageSlots": [
        { "Name": "通常", "FilePath": "C:\\char\\hair_back.png" }
      ],
      "Physics": {
        "Stiffness": 10.0,
        "Damping": 3.0,
        "Inertia": 1.2,
        "Gravity": 8.0,
        "AngleLimit": 40.0
      }
    },
    {
      "Id": "eye",
      "Name": "目",
      "ParentId": "head",
      "Length": 10.0,
      "AnchorX": 0.5,
      "AnchorY": 0.5,
      "BaseZOrder": 15,
      "ImageSlots": [
        { "Name": "開き", "FilePath": "C:\\char\\eye_open.png" },
        { "Name": "半目", "FilePath": "C:\\char\\eye_half.png" },
        { "Name": "閉じ", "FilePath": "C:\\char\\eye_close.png" }
      ],
      "Blink": {
        "IntervalSeconds": 4.0,
        "DurationSeconds": 0.16,
        "SlotNames": ["開き", "半目", "閉じ"]
      }
    },
    {
      "Id": "mouth",
      "Name": "口",
      "ParentId": "head",
      "Length": 10.0,
      "AnchorX": 0.5,
      "AnchorY": 0.5,
      "BaseZOrder": 15,
      "ImageSlots": [
        { "Name": "閉じ",   "FilePath": "C:\\char\\mouth_close.png" },
        { "Name": "半開き", "FilePath": "C:\\char\\mouth_half.png" },
        { "Name": "開き",   "FilePath": "C:\\char\\mouth_open.png" }
      ],
      "LipSync": {
        "SlotNames": ["閉じ", "半開き", "開き"],
        "ScaleInfluence": 0.2
      }
    },
    {
      "Id": "armUpper",
      "Name": "上腕",
      "ParentId": "body",
      "Length": 100.0,
      "AnchorX": 0.1,
      "AnchorY": 0.5,
      "BaseZOrder": 5,
      "ImageSlots": [
        { "Name": "通常", "FilePath": "C:\\char\\arm_l_upper.png" }
      ]
    },
    {
      "Id": "armLower",
      "Name": "前腕",
      "ParentId": "armUpper",
      "Length": 90.0,
      "AnchorX": 0.1,
      "AnchorY": 0.5,
      "BaseZOrder": 6,
      "ImageSlots": [
        { "Name": "通常", "FilePath": "C:\\char\\arm_l_lower.png" }
      ],
      "Ik": {
        "IsEnabled": true,
        "ChainLength": 2,
        "TargetX": 150.0,
        "TargetY": 60.0,
        "FlipBend": false,
        "Iterations": 12,
        "Weight": 1.0
      }
    }
  ]
}
```

---

## 9. 読み込み時の挙動

| 状況 | 挙動 |
| --- | --- |
| JSONの構文が壊れている | `null` を返し、読み込みを中止（クラッシュしません） |
| `Id` が空文字 | GUIDを自動採番 |
| `Id` が重複している | 最初の1件のみ採用、以降は無視 |
| `ParentId` が存在しないIDを指す | そのボーンはルート扱い |
| 親子関係が循環している | `SetParent` が拒否し、循環する側はルート扱い |
| `Bones` が空 | ボーン0本のスケルトンになります |
| 未知のフィールドがある | 無視されます |
| フィールドが欠けている | 既定値が使われます |

プロパティ名の**大文字小文字は区別されません**（`PropertyNameCaseInsensitive = true`）。
`"name"` でも `"Name"` でも読み込めます。

`null` のフィールドは書き出し時に省略されます
（`DefaultIgnoreCondition = WhenWritingNull`）。
そのため、物理設定のないボーンには `"Physics"` キー自体が現れません。
