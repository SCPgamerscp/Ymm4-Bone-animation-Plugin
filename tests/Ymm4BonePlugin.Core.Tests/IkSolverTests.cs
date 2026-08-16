using System.Numerics;
using Xunit;
using Ymm4BonePlugin.Core;

namespace Ymm4BonePlugin.Core.Tests
{
    public class IkSolverTests
    {
        /// <summary>肩(root) → 上腕 → 下腕 の腕チェーンを作る。</summary>
        static Skeleton CreateArm(float upper = 100f, float lower = 100f)
        {
            var skeleton = new Skeleton();
            skeleton.Add(new BoneDefinition { Id = "upper", Name = "upper", Length = upper });
            skeleton.Add(new BoneDefinition { Id = "lower", Name = "lower", ParentId = "upper", Length = lower });
            return skeleton;
        }

        static IReadOnlyList<BoneTransform> Solve(Skeleton skeleton, Vector2 target, bool flip = false, int chainLength = 2, int iterations = 30)
        {
            var end = skeleton.Bones.Last();
            end.Ik = new IkSettings
            {
                IsEnabled = true,
                ChainLength = chainLength,
                Target = target,
                FlipBend = flip,
                Weight = 1f,
                Iterations = iterations,
            };

            var evaluator = new SkeletonEvaluator();
            return evaluator.Evaluate(skeleton, _ => BonePose.Identity, new EvaluationContext { EnablePhysics = false });
        }

        [Fact]
        public void TwoBoneIk_ReachesTargetWithinRange()
        {
            var skeleton = CreateArm();
            var target = new Vector2(120f, 80f);

            var result = Solve(skeleton, target);
            var tip = result.First(t => t.Bone.Id == "lower").Tip;

            Assert.True((tip - target).Length() < 1.0f, $"tip={tip}, target={target}");
        }

        [Fact]
        public void TwoBoneIk_StretchesTowardUnreachableTarget()
        {
            var skeleton = CreateArm();
            // 合計長200を超える距離
            var target = new Vector2(500f, 0f);

            var result = Solve(skeleton, target);
            var tip = result.First(t => t.Bone.Id == "lower").Tip;

            // 到達不能なので伸ばしきり（X≈200）になり、方向は合っている
            Assert.Equal(200f, tip.X, 0);
            Assert.Equal(0f, tip.Y, 1);
        }

        [Fact]
        public void TwoBoneIk_HandlesTooCloseTarget()
        {
            var skeleton = CreateArm(100f, 40f);
            // |100-40|=60 より近い距離
            var target = new Vector2(10f, 0f);

            var result = Solve(skeleton, target);
            var tip = result.First(t => t.Bone.Id == "lower").Tip;

            // 折りたたみ限界（60）でクランプされ、発散しない
            Assert.True(MathHelper.IsFinite(tip));
            Assert.True(tip.Length() >= 55f, $"tip={tip}");
        }

        [Fact]
        public void TwoBoneIk_FlipBendMirrorsElbowDirection()
        {
            var target = new Vector2(120f, 80f);

            var normal = Solve(CreateArm(), target, flip: false);
            var flipped = Solve(CreateArm(), target, flip: true);

            var normalElbow = normal.First(t => t.Bone.Id == "lower").Origin;
            var flippedElbow = flipped.First(t => t.Bone.Id == "lower").Origin;

            // 肘の位置が root→target 軸を挟んで反対側に来ることを外積の符号で確認する
            var axis = Vector2.Normalize(target);
            float Side(Vector2 elbow) => axis.X * elbow.Y - axis.Y * elbow.X;

            Assert.True(Side(normalElbow) * Side(flippedElbow) < 0f,
                $"肘が反転していない: normal={normalElbow}, flipped={flippedElbow}");

            // どちらも末端はターゲットに到達する
            Assert.True((normal.First(t => t.Bone.Id == "lower").Tip - target).Length() < 1f);
            Assert.True((flipped.First(t => t.Bone.Id == "lower").Tip - target).Length() < 1f);
        }

        [Fact]
        public void CcdIk_ThreeBoneChainApproachesTarget()
        {
            var skeleton = new Skeleton();
            skeleton.Add(new BoneDefinition { Id = "b1", Name = "b1", Length = 80f });
            skeleton.Add(new BoneDefinition { Id = "b2", Name = "b2", ParentId = "b1", Length = 80f });
            skeleton.Add(new BoneDefinition { Id = "b3", Name = "b3", ParentId = "b2", Length = 80f });

            var target = new Vector2(100f, 120f);
            var result = Solve(skeleton, target, chainLength: 3, iterations: 60);
            var tip = result.First(t => t.Bone.Id == "b3").Tip;

            Assert.True((tip - target).Length() < 3.0f, $"tip={tip}, target={target}");
        }

        [Fact]
        public void Ik_WeightZeroKeepsFkPose()
        {
            var skeleton = CreateArm();
            var end = skeleton.Find("lower")!;
            end.Ik = new IkSettings
            {
                IsEnabled = true,
                ChainLength = 2,
                Target = new Vector2(0f, 200f),
                Weight = 0f,
            };

            var evaluator = new SkeletonEvaluator();
            var result = evaluator.Evaluate(skeleton, _ => BonePose.Identity, new EvaluationContext { EnablePhysics = false });

            // Weight=0 なので FK のまま（X軸方向に真っ直ぐ）
            Assert.Equal(200f, result.First(t => t.Bone.Id == "lower").Tip.X, 2);
        }

        [Fact]
        public void Ik_IgnoresNaNTarget()
        {
            var skeleton = CreateArm();
            var result = Solve(skeleton, new Vector2(float.NaN, float.NaN));
            var tip = result.First(t => t.Bone.Id == "lower").Tip;

            Assert.True(MathHelper.IsFinite(tip));
        }
    }
}
