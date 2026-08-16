using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Numerics;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using Ymm4BonePlugin.Core;

namespace Ymm4BonePlugin.Shape
{
    /// <summary>
    /// アイテム編集エリアに表示される、ボーン1本の設定項目。
    /// YMM4の <see cref="Animation"/> を使うことで、回転・位置・拡大率などが
    /// 標準のキーフレーム補間機能でアニメーションできるようになる。
    /// </summary>
    public class BoneItem : Animatable
    {
        #region 基本情報

        /// <summary>ボーンの識別子。親子関係の参照に使うため変更しない。</summary>
        public string Id { get => id; set => Set(ref id, value); }
        string id = System.Guid.NewGuid().ToString("N");

        [Display(GroupName = "ボーン", Name = "名前", Description = "ボーンの名前")]
        [TextEditor]
        public string Name { get => name; set => Set(ref name, value); }
        string name = "ボーン";

        /// <summary>親ボーンのID。TreeViewのドラッグ＆ドロップで変更される。</summary>
        public string ParentId { get => parentId; set => Set(ref parentId, value); }
        string parentId = string.Empty;

        [Display(GroupName = "ボーン", Name = "長さ", Description = "子ボーンの接続位置とIK計算に使用します")]
        [TextBoxSlider("F1", "px", 1, 500)]
        [DefaultValue(100d)]
        [Range(1, 100000)]
        public double Length { get => length; set => Set(ref length, value); }
        double length = 100;

        #endregion

        #region 画像

        [Display(GroupName = "画像", Name = "", Description = "このボーンに表示する画像（差分登録可）")]
        [ImageSlotEditor(PropertyEditorSize = PropertyEditorSize.FullWidth)]
        public System.Collections.Immutable.ImmutableList<BoneImageSlot> ImageSlots
        {
            get => imageSlots;
            set => Set(ref imageSlots, value);
        }
        System.Collections.Immutable.ImmutableList<BoneImageSlot> imageSlots = [new BoneImageSlot()];

        [Display(GroupName = "画像", Name = "差分番号", Description = "表示する差分画像の番号。口パク・目パチ設定がある場合はそちらが優先されます")]
        [AnimationSlider("F0", "番", 0, 10)]
        public Animation SlotIndex { get; } = new Animation(0, 0, 100);

        [Display(GroupName = "画像", Name = "アンカーX", Description = "画像の回転中心。0で左端、0.5で中央、1で右端")]
        [TextBoxSlider("F2", "", 0, 1)]
        [DefaultValue(0.5d)]
        [Range(-10, 10)]
        public double AnchorX { get => anchorX; set => Set(ref anchorX, value); }
        double anchorX = 0.5;

        [Display(GroupName = "画像", Name = "アンカーY", Description = "画像の回転中心。0で上端、0.5で中央、1で下端")]
        [TextBoxSlider("F2", "", 0, 1)]
        [DefaultValue(0.5d)]
        [Range(-10, 10)]
        public double AnchorY { get => anchorY; set => Set(ref anchorY, value); }
        double anchorY = 0.5;

        #endregion

        #region トランスフォーム（キーフレーム補間対応）

        [Display(GroupName = "変形", Name = "X", Description = "親ボーンから見たX位置")]
        [AnimationSlider("F1", "px", -500, 500)]
        public Animation X { get; } = new Animation(0, -100000, 100000);

        [Display(GroupName = "変形", Name = "Y", Description = "親ボーンから見たY位置")]
        [AnimationSlider("F1", "px", -500, 500)]
        public Animation Y { get; } = new Animation(0, -100000, 100000);

        [Display(GroupName = "変形", Name = "回転", Description = "親ボーンから見た相対回転")]
        [AnimationSlider("F1", "°", -180, 180)]
        public Animation Rotation { get; } = new Animation(0, -36000, 36000);

        [Display(GroupName = "変形", Name = "拡大率X")]
        [AnimationSlider("F1", "%", 0, 200)]
        public Animation ScaleX { get; } = new Animation(100, 0, 10000);

        [Display(GroupName = "変形", Name = "拡大率Y")]
        [AnimationSlider("F1", "%", 0, 200)]
        public Animation ScaleY { get; } = new Animation(100, 0, 10000);

        [Display(GroupName = "変形", Name = "伸縮", Description = "Squash & Stretch。100%より大きいと軸方向に伸び、垂直方向が縮みます")]
        [AnimationSlider("F1", "%", 50, 200)]
        public Animation Stretch { get; } = new Animation(100, 1, 10000);

        [Display(GroupName = "変形", Name = "不透明度")]
        [AnimationSlider("F1", "%", 0, 100)]
        public Animation Opacity { get; } = new Animation(100, 0, 100);

        [Display(GroupName = "変形", Name = "描画順", Description = "大きいほど手前に描画されます。アニメーションさせるとパーツの前後を入れ替えられます")]
        [AnimationSlider("F1", "", -100, 100)]
        public Animation ZOrder { get; } = new Animation(0, -10000, 10000);

        [Display(GroupName = "変形", Name = "基本描画順", Description = "描画順の基準値")]
        [TextBoxSlider("F0", "", -100, 100)]
        [DefaultValue(0)]
        [Range(-10000, 10000)]
        public int BaseZOrder { get => baseZOrder; set => Set(ref baseZOrder, value); }
        int baseZOrder = 0;

        #endregion

        #region IK

        [Display(GroupName = "IK", Name = "IKを使用", Description = "このボーンを末端としたIKを有効にします")]
        [ToggleSlider]
        public bool IsIkEnabled { get => isIkEnabled; set => Set(ref isIkEnabled, value); }
        bool isIkEnabled = false;

        [Display(GroupName = "IK", Name = "チェーン長", Description = "IKに含めるボーン数。2で腕・脚の2ボーンIK")]
        [TextBoxSlider("F0", "本", 2, 5)]
        [DefaultValue(2)]
        [Range(2, 20)]
        public int IkChainLength { get => ikChainLength; set => Set(ref ikChainLength, value); }
        int ikChainLength = 2;

        [Display(GroupName = "IK", Name = "ターゲットX")]
        [AnimationSlider("F1", "px", -500, 500)]
        public Animation IkTargetX { get; } = new Animation(100, -100000, 100000);

        [Display(GroupName = "IK", Name = "ターゲットY")]
        [AnimationSlider("F1", "px", -500, 500)]
        public Animation IkTargetY { get; } = new Animation(0, -100000, 100000);

        [Display(GroupName = "IK", Name = "曲げ方向反転", Description = "肘・膝の曲がる向きを反転します")]
        [ToggleSlider]
        public bool IkFlipBend { get => ikFlipBend; set => Set(ref ikFlipBend, value); }
        bool ikFlipBend = false;

        [Display(GroupName = "IK", Name = "影響度", Description = "FKの姿勢とIKの結果をブレンドする割合")]
        [AnimationSlider("F1", "%", 0, 100)]
        public Animation IkWeight { get; } = new Animation(100, 0, 100);

        #endregion

        #region 物理演算

        [Display(GroupName = "物理", Name = "揺れものにする", Description = "髪や服のように、親の動きに遅れて揺れるようにします")]
        [ToggleSlider]
        public bool IsPhysicsEnabled { get => isPhysicsEnabled; set => Set(ref isPhysicsEnabled, value); }
        bool isPhysicsEnabled = false;

        [Display(GroupName = "物理", Name = "硬さ", Description = "大きいほど元の姿勢へ強く戻ります")]
        [TextBoxSlider("F1", "", 1, 50)]
        [DefaultValue(12d)]
        [Range(0.1, 1000)]
        public double Stiffness { get => stiffness; set => Set(ref stiffness, value); }
        double stiffness = 12;

        [Display(GroupName = "物理", Name = "減衰", Description = "大きいほど早く揺れが収まります")]
        [TextBoxSlider("F1", "", 0, 20)]
        [DefaultValue(3.5d)]
        [Range(0, 1000)]
        public double Damping { get => damping; set => Set(ref damping, value); }
        double damping = 3.5;

        [Display(GroupName = "物理", Name = "慣性", Description = "親の動きに対する追従の遅れ量")]
        [TextBoxSlider("F1", "", 0, 5)]
        [DefaultValue(1d)]
        [Range(0, 100)]
        public double Inertia { get => inertia; set => Set(ref inertia, value); }
        double inertia = 1;

        [Display(GroupName = "物理", Name = "重力", Description = "垂れ下がる強さ")]
        [TextBoxSlider("F1", "", -50, 50)]
        [DefaultValue(0d)]
        [Range(-1000, 1000)]
        public double Gravity { get => gravity; set => Set(ref gravity, value); }
        double gravity = 0;

        [Display(GroupName = "物理", Name = "角度制限", Description = "揺れる角度の上限")]
        [TextBoxSlider("F1", "°", 0, 180)]
        [DefaultValue(45d)]
        [Range(0, 360)]
        public double AngleLimit { get => angleLimit; set => Set(ref angleLimit, value); }
        double angleLimit = 45;

        #endregion

        #region 口パク・目パチ

        [Display(GroupName = "口パク・目パチ", Name = "口パク連動", Description = "ボイスの音量に合わせて差分画像を切り替えます")]
        [ToggleSlider]
        public bool IsLipSyncEnabled { get => isLipSyncEnabled; set => Set(ref isLipSyncEnabled, value); }
        bool isLipSyncEnabled = false;

        [Display(GroupName = "口パク・目パチ", Name = "口パク差分", Description = "開いた状態→閉じた状態の順に、差分画像の名前をカンマ区切りで指定します")]
        [TextEditor]
        public string LipSyncSlotNames { get => lipSyncSlotNames; set => Set(ref lipSyncSlotNames, value); }
        string lipSyncSlotNames = string.Empty;

        [Display(GroupName = "口パク・目パチ", Name = "口パク縦伸縮", Description = "口の開き具合を縦の拡大率へ反映する量")]
        [TextBoxSlider("F2", "", 0, 1)]
        [DefaultValue(0d)]
        [Range(-10, 10)]
        public double LipSyncScaleInfluence { get => lipSyncScaleInfluence; set => Set(ref lipSyncScaleInfluence, value); }
        double lipSyncScaleInfluence = 0;

        [Display(GroupName = "口パク・目パチ", Name = "目パチ連動", Description = "一定間隔で自動的にまばたきします")]
        [ToggleSlider]
        public bool IsBlinkEnabled { get => isBlinkEnabled; set => Set(ref isBlinkEnabled, value); }
        bool isBlinkEnabled = false;

        [Display(GroupName = "口パク・目パチ", Name = "目パチ差分", Description = "開いた状態→閉じた状態の順に、差分画像の名前をカンマ区切りで指定します")]
        [TextEditor]
        public string BlinkSlotNames { get => blinkSlotNames; set => Set(ref blinkSlotNames, value); }
        string blinkSlotNames = string.Empty;

        [Display(GroupName = "口パク・目パチ", Name = "まばたき間隔")]
        [TextBoxSlider("F1", "秒", 1, 10)]
        [DefaultValue(4d)]
        [Range(0.2, 600)]
        public double BlinkInterval { get => blinkInterval; set => Set(ref blinkInterval, value); }
        double blinkInterval = 4;

        [Display(GroupName = "口パク・目パチ", Name = "まばたき時間")]
        [TextBoxSlider("F2", "秒", 0.05, 1)]
        [DefaultValue(0.16d)]
        [Range(0.02, 10)]
        public double BlinkDuration { get => blinkDuration; set => Set(ref blinkDuration, value); }
        double blinkDuration = 0.16;

        #endregion

        public BoneItem()
        {
        }

        public BoneItem(string name, string parentId = "")
        {
            this.name = name;
            this.parentId = parentId;
        }

        /// <summary>コピーコンストラクタ。設定の一時保存・復元に使用する。</summary>
        public BoneItem(BoneItem source)
        {
            id = source.Id;
            name = source.Name;
            parentId = source.ParentId;
            length = source.Length;
            anchorX = source.AnchorX;
            anchorY = source.AnchorY;
            baseZOrder = source.BaseZOrder;
            imageSlots = [.. source.ImageSlots.Select(s => new BoneImageSlot(s))];

            isIkEnabled = source.IsIkEnabled;
            ikChainLength = source.IkChainLength;
            ikFlipBend = source.IkFlipBend;

            isPhysicsEnabled = source.IsPhysicsEnabled;
            stiffness = source.Stiffness;
            damping = source.Damping;
            inertia = source.Inertia;
            gravity = source.Gravity;
            angleLimit = source.AngleLimit;

            isLipSyncEnabled = source.IsLipSyncEnabled;
            lipSyncSlotNames = source.LipSyncSlotNames;
            lipSyncScaleInfluence = source.LipSyncScaleInfluence;
            isBlinkEnabled = source.IsBlinkEnabled;
            blinkSlotNames = source.BlinkSlotNames;
            blinkInterval = source.BlinkInterval;
            blinkDuration = source.BlinkDuration;

            X.CopyFrom(source.X);
            Y.CopyFrom(source.Y);
            Rotation.CopyFrom(source.Rotation);
            ScaleX.CopyFrom(source.ScaleX);
            ScaleY.CopyFrom(source.ScaleY);
            Stretch.CopyFrom(source.Stretch);
            Opacity.CopyFrom(source.Opacity);
            ZOrder.CopyFrom(source.ZOrder);
            SlotIndex.CopyFrom(source.SlotIndex);
            IkTargetX.CopyFrom(source.IkTargetX);
            IkTargetY.CopyFrom(source.IkTargetY);
            IkWeight.CopyFrom(source.IkWeight);
        }

        /// <summary>
        /// この設定項目から、Core層の <see cref="BoneDefinition"/> を生成する。
        /// アニメーションしない静的な設定のみを反映する。
        /// </summary>
        public BoneDefinition ToBoneDefinition()
        {
            var bone = new BoneDefinition
            {
                Id = Id,
                Name = Name,
                ParentId = string.IsNullOrEmpty(ParentId) ? null : ParentId,
                Length = (float)Length,
                AnchorPoint = new Vector2((float)AnchorX, (float)AnchorY),
                BaseZOrder = BaseZOrder,
            };

            foreach (var slot in ImageSlots)
            {
                bone.ImageSlots.Add(new ImageSlot
                {
                    Name = slot.Name,
                    FilePath = slot.FilePath,
                });
            }

            if (IsPhysicsEnabled)
            {
                bone.Physics = new PhysicsSettings
                {
                    Stiffness = (float)Stiffness,
                    Damping = (float)Damping,
                    Inertia = (float)Inertia,
                    Gravity = (float)Gravity,
                    AngleLimit = (float)AngleLimit,
                };
            }

            if (IsLipSyncEnabled)
            {
                bone.LipSync = new LipSyncSettings
                {
                    SlotNames = SplitNames(LipSyncSlotNames),
                    ScaleInfluence = (float)LipSyncScaleInfluence,
                };
            }

            if (IsBlinkEnabled)
            {
                bone.Blink = new BlinkSettings
                {
                    IntervalSeconds = (float)BlinkInterval,
                    DurationSeconds = (float)BlinkDuration,
                    SlotNames = SplitNames(BlinkSlotNames),
                };
            }

            if (IsIkEnabled)
            {
                bone.Ik = new IkSettings
                {
                    IsEnabled = true,
                    ChainLength = IkChainLength,
                    FlipBend = IkFlipBend,
                };
            }

            return bone;
        }

        /// <summary>
        /// 指定フレームのアニメーション値を評価して <see cref="BonePose"/> を作る。
        /// </summary>
        public BonePose GetPose(int frame, int length, int fps)
        {
            return new BonePose
            {
                Position = new Vector2(
                    (float)X.GetValue(frame, length, fps),
                    (float)Y.GetValue(frame, length, fps)),
                Rotation = (float)Rotation.GetValue(frame, length, fps),
                ScaleX = (float)(ScaleX.GetValue(frame, length, fps) / 100.0),
                ScaleY = (float)(ScaleY.GetValue(frame, length, fps) / 100.0),
                Stretch = (float)(Stretch.GetValue(frame, length, fps) / 100.0),
                Opacity = (float)(Opacity.GetValue(frame, length, fps) / 100.0),
                ZOrder = (float)ZOrder.GetValue(frame, length, fps),
            };
        }

        /// <summary>指定フレームのIKターゲット位置を評価する。</summary>
        public Vector2 GetIkTarget(int frame, int length, int fps)
            => new Vector2(
                (float)IkTargetX.GetValue(frame, length, fps),
                (float)IkTargetY.GetValue(frame, length, fps));

        /// <summary>指定フレームのIK影響度(0〜1)を評価する。</summary>
        public float GetIkWeight(int frame, int length, int fps)
            => (float)(IkWeight.GetValue(frame, length, fps) / 100.0);

        /// <summary>指定フレームの差分画像番号を評価する。</summary>
        public int GetSlotIndex(int frame, int length, int fps)
            => (int)System.Math.Round(SlotIndex.GetValue(frame, length, fps));

        static List<string> SplitNames(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return new List<string>();
            return value
                .Split(new[] { ',', '、', '\n' }, System.StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();
        }

        protected override IEnumerable<IAnimatable> GetAnimatables() =>
        [
            X, Y, Rotation, ScaleX, ScaleY, Stretch, Opacity, ZOrder,
            SlotIndex, IkTargetX, IkTargetY, IkWeight,
        ];
    }
}
