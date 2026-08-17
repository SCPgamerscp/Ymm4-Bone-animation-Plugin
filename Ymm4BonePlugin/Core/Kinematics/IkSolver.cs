using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ymm4BonePlugin.Core.Kinematics
{
    /// <summary>
    /// 逆運動学ソルバー。
    /// 2ボーンの場合は余弦定理による解析解、3ボーン以上はCCD法で解く。
    /// </summary>
    public static class IkSolver
    {
        /// <summary>
        /// IKチェーンを解き、各ボーンのローカル回転を更新する。
        /// </summary>
        /// <param name="chain">[末端, 親, 祖父...] の順に並んだチェーン</param>
        /// <param name="transforms">FK評価済みの変換辞書（更新される）</param>
        /// <param name="settings">IK設定</param>
        public static void Solve(
            IReadOnlyList<BoneDefinition> chain,
            Dictionary<string, BoneTransform> transforms,
            IkSettings settings)
        {
            if (chain is null || chain.Count < 2 || transforms is null || settings is null)
                return;

            if (!MathHelper.IsFinite(settings.Target))
                return;

            if (chain.Count == 2)
                SolveTwoBone(chain, transforms, settings);
            else
                SolveCcd(chain, transforms, settings);
        }

        /// <summary>
        /// 2ボーンIK（腕・脚）を余弦定理で解く。
        /// </summary>
        static void SolveTwoBone(
            IReadOnlyList<BoneDefinition> chain,
            Dictionary<string, BoneTransform> transforms,
            IkSettings settings)
        {
            var endBone = chain[0];
            var midBone = chain[1];

            if (!transforms.TryGetValue(endBone.Id, out var endTransform))
                return;
            if (!transforms.TryGetValue(midBone.Id, out var midTransform))
                return;

            // 上腕の付け根（ワールド）と、その親のワールド回転
            var rootPosition = midTransform.Origin;
            var parentRotation = 0f;
            if (!midBone.IsRoot && transforms.TryGetValue(midBone.ParentId!, out var parentTransform))
                parentRotation = parentTransform.WorldRotation;

            var upperLength = Math.Max(0.0001f, midBone.Length);
            var lowerLength = Math.Max(0.0001f, endBone.Length);

            var toTarget = settings.Target - rootPosition;
            var distance = toTarget.Length();
            if (distance < 1e-5f)
                return;

            var minReach = Math.Abs(upperLength - lowerLength);
            var maxReach = upperLength + lowerLength;

            float rootAngle;
            float elbowAngle;

            if (distance >= maxReach)
            {
                // 到達不能: 完全に伸ばしきってターゲット方向を向く
                rootAngle = 0f;
                elbowAngle = 0f;
            }
            else if (distance <= minReach)
            {
                // 近すぎる: 折りたたみ限界
                rootAngle = 0f;
                elbowAngle = 180f;
            }
            else
            {
                // 余弦定理: 付け根における、ターゲット方向と上腕のなす角
                var cosRoot = MathHelper.Clamp(
                    (upperLength * upperLength + distance * distance - lowerLength * lowerLength)
                        / (2f * upperLength * distance),
                    -1f, 1f);
                rootAngle = (float)Math.Acos(cosRoot) * MathHelper.Rad2Deg;

                // 余弦定理: 肘の内角から、下腕の上腕に対する相対角を求める
                var cosElbow = MathHelper.Clamp(
                    (upperLength * upperLength + lowerLength * lowerLength - distance * distance)
                        / (2f * upperLength * lowerLength),
                    -1f, 1f);
                elbowAngle = 180f - (float)Math.Acos(cosElbow) * MathHelper.Rad2Deg;
            }

            var bend = settings.FlipBend ? -1f : 1f;
            var targetDirection = MathHelper.ToDegrees(toTarget);

            // 上腕をターゲット方向から rootAngle だけ開き、下腕は逆向きに折り返して先端をターゲットに合わせる。
            // ワールド角度 → ローカル角度へ変換する。
            var newMidRotation = MathHelper.NormalizeDegrees(targetDirection + rootAngle * bend - parentRotation);
            var newEndRotation = MathHelper.NormalizeDegrees(-elbowAngle * bend);

            var weight = MathHelper.Clamp01(settings.Weight);
            ApplyRotation(midTransform, newMidRotation, weight);
            ApplyRotation(endTransform, newEndRotation, weight);
        }

        /// <summary>
        /// CCD法（Cyclic Coordinate Descent）で多関節チェーンを解く。
        /// </summary>
        static void SolveCcd(
            IReadOnlyList<BoneDefinition> chain,
            Dictionary<string, BoneTransform> transforms,
            IkSettings settings)
        {
            var iterations = Math.Max(1, settings.Iterations);
            var weight = MathHelper.Clamp01(settings.Weight);
            var endBone = chain[0];

            if (!transforms.TryGetValue(endBone.Id, out var endTransform))
                return;

            for (var iteration = 0; iteration < iterations; iteration++)
            {
                // 末端に近い順（chain[1]から根本方向）に回転を調整する
                for (var i = 1; i < chain.Count; i++)
                {
                    if (!transforms.TryGetValue(chain[i].Id, out var jointTransform))
                        continue;

                    var jointPosition = jointTransform.Origin;
                    var effectorPosition = endTransform.Tip;

                    var toEffector = effectorPosition - jointPosition;
                    var toTarget = settings.Target - jointPosition;

                    if (toEffector.LengthSquared() < 1e-8f || toTarget.LengthSquared() < 1e-8f)
                        continue;

                    var delta = MathHelper.DeltaDegrees(
                        MathHelper.ToDegrees(toEffector),
                        MathHelper.ToDegrees(toTarget));

                    var pose = jointTransform.LocalPose;
                    pose.Rotation = MathHelper.NormalizeDegrees(pose.Rotation + delta * weight);
                    jointTransform.LocalPose = pose;

                    // このジョイント以下のワールド行列を更新する
                    UpdateChainWorld(chain, transforms, i);
                }

                // 十分近づいたら終了
                if ((endTransform.Tip - settings.Target).LengthSquared() < 0.01f)
                    break;
            }
        }

        /// <summary>チェーン内のワールド行列を、指定インデックスから末端方向へ更新する。</summary>
        static void UpdateChainWorld(
            IReadOnlyList<BoneDefinition> chain,
            Dictionary<string, BoneTransform> transforms,
            int fromIndex)
        {
            for (var i = fromIndex; i >= 0; i--)
            {
                var bone = chain[i];
                if (!transforms.TryGetValue(bone.Id, out var transform))
                    continue;

                var local = transform.LocalPose.ToMatrix();

                if (!bone.IsRoot && transforms.TryGetValue(bone.ParentId!, out var parent))
                {
                    var connection = Matrix3x2.CreateTranslation(parent.Bone.Length, 0f);
                    transform.World = local * connection * parent.World;
                }
                else
                {
                    transform.World = local;
                }
            }
        }

        /// <summary>FK結果とIK結果を重み付きでブレンドして適用する。</summary>
        static void ApplyRotation(BoneTransform transform, float ikRotation, float weight)
        {
            var pose = transform.LocalPose;
            pose.Rotation = MathHelper.LerpDegrees(pose.Rotation, ikRotation, weight);
            transform.LocalPose = pose;
        }
    }
}
