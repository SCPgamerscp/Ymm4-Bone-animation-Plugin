using System;
using System.Numerics;

namespace Ymm4BoneAnimationPlugin.Core
{
    /// <summary>
    /// あるフレームにおいて評価済みのボーンローカル姿勢。
    /// YMM4の <c>Animation</c> から取り出した数値をこの構造体へ詰めてからCoreへ渡す。
    /// </summary>
    public struct BonePose : IEquatable<BonePose>
    {
        /// <summary>親ボーン空間における位置(px)。</summary>
        public Vector2 Position;

        /// <summary>親ボーンからの相対回転(度)。</summary>
        public float Rotation;

        /// <summary>ボーン軸方向のスケール。</summary>
        public float ScaleX;

        /// <summary>ボーン軸に垂直な方向のスケール。</summary>
        public float ScaleY;

        /// <summary>Squash &amp; Stretch の伸縮率(1で等倍)。軸方向に伸ばし垂直方向を体積保存的に縮める。</summary>
        public float Stretch;

        /// <summary>不透明度(0〜1)。</summary>
        public float Opacity;

        /// <summary>描画順の評価値。大きいほど手前。</summary>
        public float ZOrder;

        public static BonePose Identity => new BonePose
        {
            Position = Vector2.Zero,
            Rotation = 0f,
            ScaleX = 1f,
            ScaleY = 1f,
            Stretch = 1f,
            Opacity = 1f,
            ZOrder = 0f,
        };

        /// <summary>不正値(NaN等)を安全な値に丸めた姿勢を返す。</summary>
        public BonePose Sanitized()
        {
            var pose = this;
            if (!MathHelper.IsFinite(pose.Position))
                pose.Position = Vector2.Zero;
            if (float.IsNaN(pose.Rotation) || float.IsInfinity(pose.Rotation))
                pose.Rotation = 0f;
            pose.ScaleX = Sanitize(pose.ScaleX, 1f);
            pose.ScaleY = Sanitize(pose.ScaleY, 1f);
            pose.Stretch = Math.Max(0.0001f, Sanitize(pose.Stretch, 1f));
            pose.Opacity = MathHelper.Clamp01(Sanitize(pose.Opacity, 1f));
            pose.ZOrder = Sanitize(pose.ZOrder, 0f);
            return pose;
        }

        static float Sanitize(float value, float fallback)
            => float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;

        /// <summary>
        /// このローカル姿勢を表す行列を生成する。
        /// 変換順は 移動 → 回転 → (Squash&amp;Stretchを含む)スケール。
        /// </summary>
        public Matrix3x2 ToMatrix()
        {
            var pose = Sanitized();

            // Squash & Stretch: 軸方向に Stretch 倍、垂直方向に 1/sqrt(Stretch) 倍して質量感を保つ。
            var stretchX = pose.Stretch;
            var stretchY = pose.Stretch <= 0f ? 1f : 1f / (float)Math.Sqrt(pose.Stretch);

            var scale = Matrix3x2.CreateScale(pose.ScaleX * stretchX, pose.ScaleY * stretchY);
            var rotation = Matrix3x2.CreateRotation(pose.Rotation * MathHelper.Deg2Rad);
            var translation = Matrix3x2.CreateTranslation(pose.Position);

            return scale * rotation * translation;
        }

        /// <summary>2つの姿勢を補間する。</summary>
        public static BonePose Lerp(BonePose a, BonePose b, float t)
        {
            t = MathHelper.Clamp01(t);
            return new BonePose
            {
                Position = Vector2.Lerp(a.Position, b.Position, t),
                Rotation = MathHelper.LerpDegrees(a.Rotation, b.Rotation, t),
                ScaleX = MathHelper.Lerp(a.ScaleX, b.ScaleX, t),
                ScaleY = MathHelper.Lerp(a.ScaleY, b.ScaleY, t),
                Stretch = MathHelper.Lerp(a.Stretch, b.Stretch, t),
                Opacity = MathHelper.Lerp(a.Opacity, b.Opacity, t),
                ZOrder = MathHelper.Lerp(a.ZOrder, b.ZOrder, t),
            };
        }

        public bool Equals(BonePose other)
            => Position.Equals(other.Position)
            && Rotation.Equals(other.Rotation)
            && ScaleX.Equals(other.ScaleX)
            && ScaleY.Equals(other.ScaleY)
            && Stretch.Equals(other.Stretch)
            && Opacity.Equals(other.Opacity)
            && ZOrder.Equals(other.ZOrder);

        public override bool Equals(object? obj) => obj is BonePose other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = Position.GetHashCode();
                hash = hash * 397 ^ Rotation.GetHashCode();
                hash = hash * 397 ^ ScaleX.GetHashCode();
                hash = hash * 397 ^ ScaleY.GetHashCode();
                hash = hash * 397 ^ Stretch.GetHashCode();
                hash = hash * 397 ^ Opacity.GetHashCode();
                hash = hash * 397 ^ ZOrder.GetHashCode();
                return hash;
            }
        }

        public override string ToString()
            => $"Pos={Position}, Rot={Rotation:F2}, Scale=({ScaleX:F2},{ScaleY:F2}), Stretch={Stretch:F2}, Opacity={Opacity:F2}, Z={ZOrder:F2}";
    }
}
