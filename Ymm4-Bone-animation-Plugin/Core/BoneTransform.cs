using System.Numerics;

namespace Ymm4BoneAnimationPlugin.Core
{
    /// <summary>
    /// FK/IK/物理を適用した後の、あるボーンの最終的なワールド情報。
    /// レンダラーはこの情報のみを見て描画する。
    /// </summary>
    public sealed class BoneTransform
    {
        public BoneTransform(BoneDefinition bone)
        {
            Bone = bone;
        }

        /// <summary>対応するボーン定義。</summary>
        public BoneDefinition Bone { get; }

        /// <summary>ルート空間へのワールド行列。</summary>
        public Matrix3x2 World { get; internal set; } = Matrix3x2.Identity;

        /// <summary>評価に使用したローカル姿勢（物理・IK適用後）。</summary>
        public BonePose LocalPose { get; internal set; } = BonePose.Identity;

        /// <summary>親から受け継いだ累積不透明度。</summary>
        public float Opacity { get; internal set; } = 1f;

        /// <summary>描画順の最終評価値。大きいほど手前に描画される。</summary>
        public float ZOrder { get; internal set; }

        /// <summary>ボーンの始点（ワールド座標）。</summary>
        public Vector2 Origin => MathHelper.GetTranslation(World);

        /// <summary>ボーンの終点（ワールド座標）。子ボーンの既定接続位置。</summary>
        public Vector2 Tip => MathHelper.Transform(new Vector2(Bone.Length, 0f), World);

        /// <summary>ワールド上での回転角(度)。</summary>
        public float WorldRotation => MathHelper.GetRotationDegrees(World);

        /// <summary>ワールド上でのスケール。</summary>
        public Vector2 WorldScale => MathHelper.GetScale(World);

        /// <summary>このフレームで選択されている画像スロットの添字。</summary>
        public int ActiveSlotIndex { get; internal set; }

        /// <summary>このフレームで選択されている画像スロット。存在しない場合はnull。</summary>
        public ImageSlot? ActiveSlot
            => ActiveSlotIndex >= 0 && ActiveSlotIndex < Bone.ImageSlots.Count
                ? Bone.ImageSlots[ActiveSlotIndex]
                : null;

        public override string ToString()
            => $"{Bone.Name}: Origin={Origin}, Rot={WorldRotation:F2}, Z={ZOrder:F2}, Opacity={Opacity:F2}";
    }
}
