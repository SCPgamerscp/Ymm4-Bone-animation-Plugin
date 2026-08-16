using System.Numerics;
using Xunit;
using Ymm4BonePlugin.Core;

namespace Ymm4BonePlugin.Core.Tests
{
    public class EvaluatorTests
    {
        static BoneDefinition Bone(string id, string? parentId = null, float length = 100f)
            => new BoneDefinition { Id = id, Name = id, ParentId = parentId, Length = length };

        static Func<BoneDefinition, BonePose> Poses(Dictionary<string, BonePose> map)
            => bone => map.TryGetValue(bone.Id, out var pose) ? pose : BonePose.Identity;

        [Fact]
        public void Fk_ChildInheritsParentRotationAndConnectsAtTip()
        {
            var skeleton = new Skeleton();
            skeleton.Add(Bone("root", length: 100f));
            skeleton.Add(Bone("child", "root", 100f));

            var evaluator = new SkeletonEvaluator();
            var poses = new Dictionary<string, BonePose>
            {
                ["root"] = BonePose.Identity,
                ["child"] = BonePose.Identity,
            };

            var result = evaluator.Evaluate(skeleton, Poses(poses), new EvaluationContext { EnablePhysics = false });
            var child = result.First(t => t.Bone.Id == "child");

            // 親の長さ100の先端に接続される
            Assert.Equal(100f, child.Origin.X, 3);
            Assert.Equal(0f, child.Origin.Y, 3);
            Assert.Equal(200f, child.Tip.X, 3);
        }

        [Fact]
        public void Fk_ParentRotationPropagatesToChild()
        {
            var skeleton = new Skeleton();
            skeleton.Add(Bone("root", length: 100f));
            skeleton.Add(Bone("child", "root", 100f));

            var evaluator = new SkeletonEvaluator();
            var rootPose = BonePose.Identity;
            rootPose.Rotation = 90f;

            var poses = new Dictionary<string, BonePose>
            {
                ["root"] = rootPose,
                ["child"] = BonePose.Identity,
            };

            var result = evaluator.Evaluate(skeleton, Poses(poses), new EvaluationContext { EnablePhysics = false });
            var child = result.First(t => t.Bone.Id == "child");

            // 親が90度回転 → 子の付け根は (0, 100) 付近
            Assert.Equal(0f, child.Origin.X, 2);
            Assert.Equal(100f, child.Origin.Y, 2);
            Assert.Equal(90f, child.WorldRotation, 2);
        }

        [Fact]
        public void Opacity_IsMultipliedThroughHierarchy()
        {
            var skeleton = new Skeleton();
            skeleton.Add(Bone("root"));
            skeleton.Add(Bone("child", "root"));

            var rootPose = BonePose.Identity;
            rootPose.Opacity = 0.5f;
            var childPose = BonePose.Identity;
            childPose.Opacity = 0.5f;

            var evaluator = new SkeletonEvaluator();
            var result = evaluator.Evaluate(
                skeleton,
                Poses(new Dictionary<string, BonePose> { ["root"] = rootPose, ["child"] = childPose }),
                new EvaluationContext { EnablePhysics = false });

            Assert.Equal(0.25f, result.First(t => t.Bone.Id == "child").Opacity, 4);
        }

        [Fact]
        public void ZOrder_SortsResultsBackToFront()
        {
            var skeleton = new Skeleton();
            skeleton.Add(new BoneDefinition { Id = "back", Name = "back", BaseZOrder = 0 });
            skeleton.Add(new BoneDefinition { Id = "front", Name = "front", BaseZOrder = 10 });
            skeleton.Add(new BoneDefinition { Id = "middle", Name = "middle", BaseZOrder = 5 });

            var evaluator = new SkeletonEvaluator();
            var result = evaluator.Evaluate(skeleton, _ => BonePose.Identity, new EvaluationContext { EnablePhysics = false });

            Assert.Equal(new[] { "back", "middle", "front" }, result.Select(t => t.Bone.Id).ToArray());
        }

        [Fact]
        public void ZOrder_AnimatedValueCanReorderParts()
        {
            var skeleton = new Skeleton();
            skeleton.Add(new BoneDefinition { Id = "armL", Name = "armL", BaseZOrder = 0 });
            skeleton.Add(new BoneDefinition { Id = "armR", Name = "armR", BaseZOrder = 1 });

            // armL に +5 のZオーダーアニメーションを与えると前後が入れ替わる
            var armLPose = BonePose.Identity;
            armLPose.ZOrder = 5f;

            var evaluator = new SkeletonEvaluator();
            var result = evaluator.Evaluate(
                skeleton,
                Poses(new Dictionary<string, BonePose> { ["armL"] = armLPose }),
                new EvaluationContext { EnablePhysics = false });

            Assert.Equal(new[] { "armR", "armL" }, result.Select(t => t.Bone.Id).ToArray());
        }

        [Fact]
        public void Evaluate_HandlesNaNPoseSafely()
        {
            var skeleton = new Skeleton();
            skeleton.Add(Bone("root"));

            var broken = new BonePose
            {
                Position = new Vector2(float.NaN, float.NaN),
                Rotation = float.NaN,
                ScaleX = float.NaN,
                ScaleY = float.PositiveInfinity,
                Stretch = float.NaN,
                Opacity = float.NaN,
                ZOrder = float.NaN,
            };

            var evaluator = new SkeletonEvaluator();
            var result = evaluator.Evaluate(
                skeleton,
                Poses(new Dictionary<string, BonePose> { ["root"] = broken }),
                new EvaluationContext { EnablePhysics = false });

            var root = result.Single();
            Assert.True(MathHelper.IsFinite(root.Origin));
            Assert.Equal(1f, root.Opacity, 4);
        }

        [Fact]
        public void SquashAndStretch_PreservesVolumeApproximately()
        {
            var pose = BonePose.Identity;
            pose.Stretch = 4f;

            var scale = MathHelper.GetScale(pose.ToMatrix());

            // 軸方向は4倍、垂直方向は 1/sqrt(4)=0.5 倍
            Assert.Equal(4f, scale.X, 3);
            Assert.Equal(0.5f, scale.Y, 3);
        }

        [Fact]
        public void Evaluate_DoesNotThrow_OnEmptySkeleton()
        {
            var evaluator = new SkeletonEvaluator();
            var result = evaluator.Evaluate(new Skeleton(), _ => BonePose.Identity, new EvaluationContext());
            Assert.Empty(result);
        }
    }
}
