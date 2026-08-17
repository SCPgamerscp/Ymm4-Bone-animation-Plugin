using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Ymm4BonePlugin.Core.Kinematics;

namespace Ymm4BonePlugin.Core
{
    /// <summary>
    /// 骨組み全体の姿勢を評価するエンジン。
    /// FK → IK → 物理 → Zオーダー確定 の順に処理し、レンダラー向けの結果を返す。
    /// YMM4/Direct2Dへ依存しないため単体テスト可能。
    /// </summary>
    public sealed class SkeletonEvaluator
    {
        readonly PhysicsSimulator physics = new PhysicsSimulator();

        /// <summary>直近の評価結果（ボーンID → 変換）。</summary>
        public IReadOnlyDictionary<string, BoneTransform> LastResult { get; private set; }
            = new Dictionary<string, BoneTransform>();

        /// <summary>物理シミュレーションの内部状態をリセットする。シーク時に呼ぶ。</summary>
        public void ResetPhysics() => physics.Reset();

        /// <summary>
        /// 1フレーム分の姿勢を評価する。
        /// </summary>
        /// <param name="skeleton">骨組み</param>
        /// <param name="poseProvider">ボーンIDに対応するローカル姿勢を返す関数</param>
        /// <param name="context">フレーム情報（時間・口パク量等）</param>
        /// <returns>描画順（奥→手前）に並んだ変換結果</returns>
        public IReadOnlyList<BoneTransform> Evaluate(
            Skeleton skeleton,
            Func<BoneDefinition, BonePose> poseProvider,
            EvaluationContext context)
        {
            if (skeleton is null)
                throw new ArgumentNullException(nameof(skeleton));
            if (poseProvider is null)
                throw new ArgumentNullException(nameof(poseProvider));

            var ordered = skeleton.GetTopologicalOrder();
            var transforms = new Dictionary<string, BoneTransform>(ordered.Count);
            var results = new List<BoneTransform>(ordered.Count);

            // --- 1. FK: 親から順にワールド行列を求める ---
            foreach (var bone in ordered)
            {
                var pose = poseProvider(bone).Sanitized();

                // 差分画像・口パク・目パチによるスロット選択とスケール補正
                var slotIndex = SlotSelector.Select(bone, context, ref pose);

                var transform = new BoneTransform(bone)
                {
                    LocalPose = pose,
                    ActiveSlotIndex = slotIndex,
                };

                ApplyHierarchy(transform, bone, pose, transforms);

                transforms[bone.Id] = transform;
                results.Add(transform);
            }

            // --- 2. IK: 有効なチェーンを解いて姿勢を上書きし、影響下を再計算する ---
            ApplyInverseKinematics(skeleton, transforms, ordered);

            // --- 3. 物理: 揺れものを減衰バネで追従させる ---
            physics.Apply(skeleton, transforms, ordered, context);

            // --- 4. Zオーダー確定（階層順で安定ソート） ---
            for (var i = 0; i < results.Count; i++)
            {
                var t = results[i];
                t.ZOrder = t.Bone.BaseZOrder + t.LocalPose.ZOrder;
            }

            LastResult = transforms;

            return results
                .Select((t, index) => (t, index))
                .OrderBy(x => x.t.ZOrder)
                .ThenBy(x => x.index)
                .Select(x => x.t)
                .ToList();
        }

        /// <summary>親のワールド行列と累積不透明度を合成する。</summary>
        static void ApplyHierarchy(
            BoneTransform transform,
            BoneDefinition bone,
            BonePose pose,
            Dictionary<string, BoneTransform> transforms)
        {
            var local = pose.ToMatrix();

            if (!bone.IsRoot && transforms.TryGetValue(bone.ParentId!, out var parent))
            {
                // 子は既定で親ボーンの先端に接続する。
                var connection = Matrix3x2.CreateTranslation(parent.Bone.Length, 0f);
                transform.World = local * connection * parent.World;
                transform.Opacity = parent.Opacity * pose.Opacity;
            }
            else
            {
                transform.World = local;
                transform.Opacity = pose.Opacity;
            }
        }

        /// <summary>IK設定を持つボーンのチェーンを解く。</summary>
        void ApplyInverseKinematics(
            Skeleton skeleton,
            Dictionary<string, BoneTransform> transforms,
            IReadOnlyList<BoneDefinition> ordered)
        {
            var ikBones = ordered.Where(b => b.Ik?.IsEnabled == true).ToList();
            if (ikBones.Count == 0)
                return;

            foreach (var endBone in ikBones)
            {
                var settings = endBone.Ik!;
                var chainLength = Math.Max(2, settings.ChainLength);
                var chain = skeleton.GetChain(endBone.Id, chainLength);
                if (chain.Count < 2)
                    continue;

                IkSolver.Solve(chain, transforms, settings);

                // IKで変化した姿勢を階層へ反映し直す。
                RecalculateSubtree(skeleton, transforms, ordered, chain[chain.Count - 1]);
            }
        }

        /// <summary>指定ボーン以下のワールド行列を再計算する。</summary>
        static void RecalculateSubtree(
            Skeleton skeleton,
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

                ApplyHierarchy(transform, bone, transform.LocalPose, transforms);
            }
        }
    }

    /// <summary>フレーム評価に必要な外部情報。</summary>
    public sealed class EvaluationContext
    {
        /// <summary>アイテム先頭からの経過時間(秒)。</summary>
        public double Time { get; set; }

        /// <summary>前フレームからの経過時間(秒)。物理演算に使用する。</summary>
        public double DeltaTime { get; set; } = 1.0 / 60.0;

        /// <summary>YMM4から受け取る口の開き具合(0〜1)。</summary>
        public double LipSyncValue { get; set; }

        /// <summary>物理演算を有効にするか。</summary>
        public bool EnablePhysics { get; set; } = true;

        /// <summary>目パチを有効にするか。</summary>
        public bool EnableBlink { get; set; } = true;

        /// <summary>目パチのランダム性を決める乱数シード。</summary>
        public int BlinkSeed { get; set; } = 1234;

        /// <summary>手動で選択した画像スロット（ボーンID → スロット添字）。</summary>
        public IReadOnlyDictionary<string, int>? ManualSlotSelection { get; set; }
    }
}
