using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ymm4BonePlugin.Core
{
    /// <summary>
    /// ボーン1本の静的な定義（階層・画像スロット・物理設定）。
    /// アニメーション値そのものは持たず、フレーム毎に <see cref="BonePose"/> を与えて評価する。
    /// </summary>
    public sealed class BoneDefinition
    {
        /// <summary>一意なID。親子関係やIKターゲットの参照に使用する。</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>UI表示用の名前。</summary>
        public string Name { get; set; } = "Bone";

        /// <summary>親ボーンのID。ルートの場合はnullまたは空文字。</summary>
        public string? ParentId { get; set; }

        /// <summary>
        /// ボーンの長さ(px)。子ボーンの既定接続位置とIK計算に使用する。
        /// </summary>
        public float Length { get; set; } = 100f;

        /// <summary>
        /// 画像の回転中心。画像左上を(0,0)とし、(0.5,0.5)で画像中心。
        /// プレビュー上でドラッグ操作して調整する。
        /// </summary>
        public Vector2 AnchorPoint { get; set; } = new Vector2(0.5f, 0.5f);

        /// <summary>
        /// このボーンに紐づく画像スロット一覧（表情・手などの差分）。
        /// </summary>
        public List<ImageSlot> ImageSlots { get; set; } = new List<ImageSlot>();

        /// <summary>基本の描画順。同値の場合は階層順で安定ソートされる。</summary>
        public int BaseZOrder { get; set; }

        /// <summary>物理演算(揺れもの)設定。nullの場合は物理無効。</summary>
        public PhysicsSettings? Physics { get; set; }

        /// <summary>口パク連動の設定。nullの場合は連動しない。</summary>
        public LipSyncSettings? LipSync { get; set; }

        /// <summary>目パチ連動の設定。nullの場合は連動しない。</summary>
        public BlinkSettings? Blink { get; set; }

        /// <summary>IK設定。nullの場合はFKのみ。</summary>
        public IkSettings? Ik { get; set; }

        /// <summary>ルートボーンかどうか。</summary>
        public bool IsRoot => string.IsNullOrEmpty(ParentId);

        public BoneDefinition Clone()
        {
            var clone = new BoneDefinition
            {
                Id = Id,
                Name = Name,
                ParentId = ParentId,
                Length = Length,
                AnchorPoint = AnchorPoint,
                BaseZOrder = BaseZOrder,
                Physics = Physics?.Clone(),
                LipSync = LipSync?.Clone(),
                Blink = Blink?.Clone(),
                Ik = Ik?.Clone(),
                ImageSlots = new List<ImageSlot>(ImageSlots.Count),
            };
            foreach (var slot in ImageSlots)
                clone.ImageSlots.Add(slot.Clone());
            return clone;
        }
    }

    /// <summary>差分画像のスロット。</summary>
    public sealed class ImageSlot
    {
        /// <summary>スロット名（"通常", "笑顔" 等）。</summary>
        public string Name { get; set; } = "Default";

        /// <summary>画像ファイルパス。</summary>
        public string FilePath { get; set; } = string.Empty;

        public ImageSlot Clone() => new ImageSlot { Name = Name, FilePath = FilePath };
    }

    /// <summary>揺れもの物理（減衰バネ・振り子）の設定。</summary>
    public sealed class PhysicsSettings
    {
        /// <summary>バネの強さ。大きいほど元の姿勢へ強く戻る。</summary>
        public float Stiffness { get; set; } = 12f;

        /// <summary>減衰。大きいほど早く揺れが収まる。</summary>
        public float Damping { get; set; } = 3.5f;

        /// <summary>慣性の強さ。親の動きに対する追従の遅れ量。</summary>
        public float Inertia { get; set; } = 1f;

        /// <summary>重力による垂れ下がりの強さ(度/秒^2相当)。</summary>
        public float Gravity { get; set; }

        /// <summary>揺れ角度の上限(度)。</summary>
        public float AngleLimit { get; set; } = 45f;

        public PhysicsSettings Clone() => new PhysicsSettings
        {
            Stiffness = Stiffness,
            Damping = Damping,
            Inertia = Inertia,
            Gravity = Gravity,
            AngleLimit = AngleLimit,
        };
    }

    /// <summary>口パク連動の設定。</summary>
    public sealed class LipSyncSettings
    {
        /// <summary>口の開き具合に対応させる画像スロット名（開→閉の順に並べる）。</summary>
        public List<string> SlotNames { get; set; } = new List<string>();

        /// <summary>口の開き具合を縦スケールへ反映する量(0で無効)。</summary>
        public float ScaleInfluence { get; set; }

        public LipSyncSettings Clone() => new LipSyncSettings
        {
            SlotNames = new List<string>(SlotNames),
            ScaleInfluence = ScaleInfluence,
        };
    }

    /// <summary>目パチ連動の設定。</summary>
    public sealed class BlinkSettings
    {
        /// <summary>まばたきの間隔(秒)。</summary>
        public float IntervalSeconds { get; set; } = 4f;

        /// <summary>まばたき1回の長さ(秒)。</summary>
        public float DurationSeconds { get; set; } = 0.16f;

        /// <summary>開いた状態→閉じた状態の順に並べた画像スロット名。</summary>
        public List<string> SlotNames { get; set; } = new List<string>();

        public BlinkSettings Clone() => new BlinkSettings
        {
            IntervalSeconds = IntervalSeconds,
            DurationSeconds = DurationSeconds,
            SlotNames = new List<string>(SlotNames),
        };
    }

    /// <summary>IK(逆運動学)の設定。</summary>
    public sealed class IkSettings
    {
        /// <summary>IKを有効にするか。</summary>
        public bool IsEnabled { get; set; }

        /// <summary>IKチェーンに含めるボーン数（このボーンを末端として親方向へ数える）。2で2ボーンIK。</summary>
        public int ChainLength { get; set; } = 2;

        /// <summary>ターゲット位置（ルート空間）。</summary>
        public Vector2 Target { get; set; }

        /// <summary>2ボーンIKの曲げ方向を反転するか（肘/膝の向き）。</summary>
        public bool FlipBend { get; set; }

        /// <summary>CCD法の反復回数。ChainLengthが3以上の場合に使用する。</summary>
        public int Iterations { get; set; } = 12;

        /// <summary>IKの影響度(0〜1)。FK結果とのブレンド率。</summary>
        public float Weight { get; set; } = 1f;

        public IkSettings Clone() => new IkSettings
        {
            IsEnabled = IsEnabled,
            ChainLength = ChainLength,
            Target = Target,
            FlipBend = FlipBend,
            Iterations = Iterations,
            Weight = Weight,
        };
    }
}
