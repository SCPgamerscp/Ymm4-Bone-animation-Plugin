using System;
using System.Numerics;

namespace Ymm4BoneAnimationPlugin.Core
{
    /// <summary>
    /// ボーン計算で共通利用する数学ヘルパー。
    /// YMM4 / Direct2D への依存を持たないため単体テスト可能。
    /// </summary>
    public static class MathHelper
    {
        public const float Deg2Rad = (float)(Math.PI / 180.0);
        public const float Rad2Deg = (float)(180.0 / Math.PI);

        /// <summary>角度(度)を -180〜180 の範囲へ正規化する。</summary>
        public static float NormalizeDegrees(float degrees)
        {
            if (float.IsNaN(degrees) || float.IsInfinity(degrees))
                return 0f;

            degrees %= 360f;
            if (degrees > 180f)
                degrees -= 360f;
            else if (degrees < -180f)
                degrees += 360f;
            return degrees;
        }

        /// <summary>2つの角度(度)の最短差分を返す。</summary>
        public static float DeltaDegrees(float from, float to)
            => NormalizeDegrees(to - from);

        /// <summary>角度(度)を最短経路で線形補間する。</summary>
        public static float LerpDegrees(float from, float to, float t)
            => NormalizeDegrees(from + DeltaDegrees(from, to) * Clamp01(t));

        public static float Clamp01(float value)
            => value < 0f ? 0f : value > 1f ? 1f : value;

        public static float Clamp(float value, float min, float max)
        {
            if (min > max)
                (min, max) = (max, min);
            return value < min ? min : value > max ? max : value;
        }

        public static float Lerp(float from, float to, float t)
            => from + (to - from) * t;

        /// <summary>ゼロ除算とNaNを避けた安全な除算。</summary>
        public static float SafeDivide(float numerator, float denominator, float fallback = 0f)
        {
            if (Math.Abs(denominator) < 1e-9f)
                return fallback;
            var result = numerator / denominator;
            return float.IsNaN(result) || float.IsInfinity(result) ? fallback : result;
        }

        /// <summary>ベクトルの角度(度)を返す。ゼロベクトルの場合は0。</summary>
        public static float ToDegrees(Vector2 vector)
        {
            if (vector.LengthSquared() < 1e-12f)
                return 0f;
            return (float)Math.Atan2(vector.Y, vector.X) * Rad2Deg;
        }

        /// <summary>角度(度)と長さから方向ベクトルを生成する。</summary>
        public static Vector2 FromDegrees(float degrees, float length = 1f)
        {
            var rad = degrees * Deg2Rad;
            return new Vector2((float)Math.Cos(rad) * length, (float)Math.Sin(rad) * length);
        }

        /// <summary>NaN/Infinityを含まない有限なベクトルかどうか。</summary>
        public static bool IsFinite(Vector2 vector)
            => !float.IsNaN(vector.X) && !float.IsInfinity(vector.X)
            && !float.IsNaN(vector.Y) && !float.IsInfinity(vector.Y);

        /// <summary>行列から平行移動成分を取り出す。</summary>
        public static Vector2 GetTranslation(in Matrix3x2 matrix)
            => new Vector2(matrix.M31, matrix.M32);

        /// <summary>行列から回転角(度)を取り出す。X軸ベクトルの向きを基準にする。</summary>
        public static float GetRotationDegrees(in Matrix3x2 matrix)
            => ToDegrees(new Vector2(matrix.M11, matrix.M12));

        /// <summary>行列からスケール成分(絶対値)を取り出す。</summary>
        public static Vector2 GetScale(in Matrix3x2 matrix)
        {
            var x = new Vector2(matrix.M11, matrix.M12).Length();
            var y = new Vector2(matrix.M21, matrix.M22).Length();
            return new Vector2(x, y);
        }

        /// <summary>点を行列で変換する。</summary>
        public static Vector2 Transform(Vector2 point, in Matrix3x2 matrix)
            => Vector2.Transform(point, matrix);

        /// <summary>逆行列を取得する。取得できない場合は単位行列。</summary>
        public static Matrix3x2 InvertOrIdentity(in Matrix3x2 matrix)
            => Matrix3x2.Invert(matrix, out var inverted) ? inverted : Matrix3x2.Identity;
    }
}
