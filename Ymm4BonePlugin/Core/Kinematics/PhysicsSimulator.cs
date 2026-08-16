using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ymm4BonePlugin.Core.Kinematics
{
    /// <summary>
    /// 髪・服などの揺れもの（セカンダリモーション）を減衰バネで計算する。
    /// 親ボーンの動きに対して慣性で遅れて追従する挙動を作る。
    /// </summary>
    public sealed class PhysicsSimulator
    {
        sealed class State
        {
            public float Angle;
            public float Velocity;
            public Vector2 LastParentPosition;
            public float LastParentRotation;
            public bool IsInitialized;
        }

        readonly Dictionary<string, State> states = new Dictionary<string, State>();

        /// <summary>内部状態をリセットする。タイムラインをシークした際に呼ぶ。</summary>
        public void Reset() => states.Clear();

        /// <summary>
        /// 物理設定を持つボーンへ揺れを適用する。
        /// </summary>
        public void Apply(
            Skeleton skeleton,
            Dictionary<string, BoneTransform> transforms,
            IReadOnlyList<BoneDefinition> ordered,
            EvaluationContext context)
        {
            if (!context.EnablePhysics)
                return;

            // 極端なdtでの発散を防ぐ。NaNは Math.Max/Min を素通りするため明示的に弾く。
            var rawDeltaTime = context.DeltaTime;
            if (double.IsNaN(rawDeltaTime) || double.IsInfinity(rawDeltaTime))
                rawDeltaTime = 1.0 / 60.0;
            var dt = (float)Math.Min(Math.Max(rawDeltaTime, 1e-4), 0.1);

            foreach (var bone in ordered)
            {
                var settings = bone.Physics;
                if (settings is null)
                    continue;
                if (!transforms.TryGetValue(bone.Id, out var transform))
                    continue;

                if (!states.TryGetValue(bone.Id, out var state))
                {
                    state = new State();
                    states[bone.Id] = state;
                }

                var parentPosition = Vector2.Zero;
                var parentRotation = 0f;
                if (!bone.IsRoot && transforms.TryGetValue(bone.ParentId!, out var parent))
                {
                    parentPosition = parent.Origin;
                    parentRotation = parent.WorldRotation;
                }

                if (!state.IsInitialized)
                {
                    state.LastParentPosition = parentPosition;
                    state.LastParentRotation = parentRotation;
                    state.IsInitialized = true;
                    continue;
                }

                // 親の移動・回転から慣性による外力を求める
                var parentVelocity = (parentPosition - state.LastParentPosition) / dt;
                var parentAngularVelocity = MathHelper.DeltaDegrees(state.LastParentRotation, parentRotation) / dt;

                // ボーン軸に垂直な方向の速度成分が揺れを生む
                var boneDirection = MathHelper.FromDegrees(transform.WorldRotation);
                var perpendicular = new Vector2(-boneDirection.Y, boneDirection.X);
                var lateralVelocity = Vector2.Dot(parentVelocity, perpendicular);

                // 減衰バネ: a = -k*x - c*v + 外力
                var inertiaForce = -(lateralVelocity * 0.05f + parentAngularVelocity * 0.1f) * settings.Inertia;
                var gravityForce = settings.Gravity * (float)Math.Cos(transform.WorldRotation * MathHelper.Deg2Rad);

                var acceleration = -settings.Stiffness * state.Angle
                                   - settings.Damping * state.Velocity
                                   + inertiaForce
                                   + gravityForce;

                state.Velocity += acceleration * dt;
                state.Angle += state.Velocity * dt;

                // 角度制限と発散防止
                var limit = Math.Abs(settings.AngleLimit);
                if (limit > 0f && Math.Abs(state.Angle) > limit)
                {
                    state.Angle = MathHelper.Clamp(state.Angle, -limit, limit);
                    state.Velocity *= -0.3f; // 制限に当たったら跳ね返す
                }

                if (float.IsNaN(state.Angle) || float.IsInfinity(state.Angle))
                {
                    state.Angle = 0f;
                    state.Velocity = 0f;
                }

                // 揺れ角度をローカル回転へ加算し、子孫へ伝播させる
                var pose = transform.LocalPose;
                pose.Rotation = MathHelper.NormalizeDegrees(pose.Rotation + state.Angle);
                transform.LocalPose = pose;

                RecalculateFrom(transforms, ordered, bone);

                state.LastParentPosition = parentPosition;
                state.LastParentRotation = parentRotation;
            }
        }

        /// <summary>指定ボーンとその子孫のワールド行列を再計算する。</summary>
        static void RecalculateFrom(
            Dictionary<string, BoneTransform> transforms,
            IReadOnlyList<BoneDefinition> ordered,
            BoneDefinition from)
        {
            var affected = new HashSet<string> { from.Id };

            foreach (var bone in ordered)
            {
                if (!affected.Contains(bone.Id))
                {
                    if (!string.IsNullOrEmpty(bone.ParentId) && affected.Contains(bone.ParentId!))
                        affected.Add(bone.Id);
                    else
                        continue;
                }

                if (!transforms.TryGetValue(bone.Id, out var transform))
                    continue;

                var local = transform.LocalPose.ToMatrix();
                if (!bone.IsRoot && transforms.TryGetValue(bone.ParentId!, out var parent))
                {
                    var connection = Matrix3x2.CreateTranslation(parent.Bone.Length, 0f);
                    transform.World = local * connection * parent.World;
                    transform.Opacity = parent.Opacity * transform.LocalPose.Opacity;
                }
                else
                {
                    transform.World = local;
                    transform.Opacity = transform.LocalPose.Opacity;
                }
            }
        }
    }
}
