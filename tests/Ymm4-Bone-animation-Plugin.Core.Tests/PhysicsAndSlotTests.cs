using System.Numerics;
using Xunit;
using Ymm4BoneAnimationPlugin.Core;
using Ymm4BoneAnimationPlugin.Core.Kinematics;

namespace Ymm4BoneAnimationPlugin.Core.Tests
{
    public class PhysicsTests
    {
        static Skeleton CreateHair()
        {
            var skeleton = new Skeleton();
            skeleton.Add(new BoneDefinition { Id = "head", Name = "head", Length = 50f });
            skeleton.Add(new BoneDefinition
            {
                Id = "hair",
                Name = "hair",
                ParentId = "head",
                Length = 80f,
                Physics = new PhysicsSettings
                {
                    Stiffness = 20f,
                    Damping = 2f,
                    Inertia = 1f,
                    AngleLimit = 40f,
                },
            });
            return skeleton;
        }

        [Fact]
        public void Physics_SwaysWhenParentMoves_ThenSettles()
        {
            var skeleton = CreateHair();
            var evaluator = new SkeletonEvaluator();

            // 髪は下方向(90度)に垂れ下がっている想定。
            // 頭が水平に動くと、髪の軸に対して垂直方向の力が働いて揺れる。
            const float restAngle = 90f;
            var hairRest = BonePose.Identity;
            hairRest.Rotation = restAngle;

            var headPose = BonePose.Identity;
            var poses = new Dictionary<string, BonePose> { ["hair"] = hairRest };

            BonePose Provider(BoneDefinition bone)
                => poses.TryGetValue(bone.Id, out var p) ? p : BonePose.Identity;

            float RunFrames(int frames, int startFrame, Func<int, float> headX)
            {
                var maxDeviation = 0f;
                for (var i = 0; i < frames; i++)
                {
                    var frame = startFrame + i;
                    headPose.Position = new Vector2(headX(i), 0f);
                    poses["head"] = headPose;

                    var context = new EvaluationContext { Time = frame / 60.0, DeltaTime = 1.0 / 60.0 };
                    var result = evaluator.Evaluate(skeleton, Provider, context);
                    var hair = result.First(t => t.Bone.Id == "hair");

                    // 静止角度からのズレが「揺れ」
                    maxDeviation = Math.Max(maxDeviation, Math.Abs(hair.LocalPose.Rotation - restAngle));
                }
                return maxDeviation;
            }

            // 頭を素早く左右に振る → 髪が揺れる
            var swayMax = RunFrames(30, 0, i => (float)Math.Sin(i * 0.5) * 200f);
            Assert.True(swayMax > 0.5f, $"髪が揺れていない: {swayMax}");

            // 頭を止めると減衰して静止角度へ戻る
            RunFrames(600, 30, _ => 0f);

            var finalContext = new EvaluationContext { Time = 11.0, DeltaTime = 1.0 / 60.0 };
            headPose.Position = Vector2.Zero;
            poses["head"] = headPose;
            var final = evaluator.Evaluate(skeleton, Provider, finalContext);
            var finalDeviation = Math.Abs(final.First(t => t.Bone.Id == "hair").LocalPose.Rotation - restAngle);

            Assert.True(finalDeviation < 1.0f, $"揺れが収束していない: {finalDeviation}");
        }

        [Fact]
        public void Physics_DoesNotSway_WhenParentMovesAlongBoneAxis()
        {
            // ボーン軸と平行な移動では垂直方向の力が生じないため揺れない（物理的に正しい挙動）
            var skeleton = CreateHair();
            var evaluator = new SkeletonEvaluator();
            var poses = new Dictionary<string, BonePose>();
            var headPose = BonePose.Identity;

            for (var i = 0; i < 30; i++)
            {
                headPose.Position = new Vector2(i * 20f, 0f);
                poses["head"] = headPose;
                var context = new EvaluationContext { Time = i / 60.0, DeltaTime = 1.0 / 60.0 };
                var result = evaluator.Evaluate(skeleton, b => poses.TryGetValue(b.Id, out var p) ? p : BonePose.Identity, context);
                Assert.Equal(0f, result.First(t => t.Bone.Id == "hair").LocalPose.Rotation, 3);
            }
        }

        [Fact]
        public void Physics_RespectsAngleLimit()
        {
            var skeleton = CreateHair();
            skeleton.Find("hair")!.Physics!.AngleLimit = 10f;
            skeleton.Find("hair")!.Physics!.Inertia = 50f; // 過剰な力を与える

            var evaluator = new SkeletonEvaluator();
            var poses = new Dictionary<string, BonePose>();
            var headPose = BonePose.Identity;

            for (var i = 0; i < 120; i++)
            {
                headPose.Position = new Vector2((float)Math.Sin(i * 1.2) * 2000f, 0f);
                poses["head"] = headPose;
                var context = new EvaluationContext { Time = i / 60.0, DeltaTime = 1.0 / 60.0 };
                var result = evaluator.Evaluate(skeleton, b => poses.TryGetValue(b.Id, out var p) ? p : BonePose.Identity, context);
                var angle = result.First(t => t.Bone.Id == "hair").LocalPose.Rotation;

                Assert.True(Math.Abs(angle) <= 10.5f, $"角度制限を超えた: {angle}");
                Assert.False(float.IsNaN(angle));
            }
        }

        [Fact]
        public void Physics_DisabledFlagKeepsPoseUntouched()
        {
            var skeleton = CreateHair();
            var evaluator = new SkeletonEvaluator();
            var poses = new Dictionary<string, BonePose>();
            var headPose = BonePose.Identity;

            for (var i = 0; i < 30; i++)
            {
                headPose.Position = new Vector2(i * 50f, 0f);
                poses["head"] = headPose;
                var context = new EvaluationContext { Time = i / 60.0, DeltaTime = 1.0 / 60.0, EnablePhysics = false };
                var result = evaluator.Evaluate(skeleton, b => poses.TryGetValue(b.Id, out var p) ? p : BonePose.Identity, context);
                Assert.Equal(0f, result.First(t => t.Bone.Id == "hair").LocalPose.Rotation, 4);
            }
        }

        [Fact]
        public void Physics_SurvivesExtremeDeltaTime()
        {
            var skeleton = CreateHair();
            var evaluator = new SkeletonEvaluator();
            var poses = new Dictionary<string, BonePose> { ["head"] = BonePose.Identity };

            // dt=0 や巨大な dt、NaN でも発散しない
            foreach (var dt in new[] { 0.0, -1.0, 100.0, double.NaN, double.PositiveInfinity })
            {
                var context = new EvaluationContext { Time = 1.0, DeltaTime = dt };
                var result = evaluator.Evaluate(
                    skeleton,
                    b => poses.TryGetValue(b.Id, out var p) ? p : BonePose.Identity,
                    context);
                var angle = result.First(t => t.Bone.Id == "hair").LocalPose.Rotation;
                Assert.False(float.IsNaN(angle), $"dt={dt} でNaNが発生");
            }
        }
    }

    public class SlotSelectorTests
    {
        static BoneDefinition CreateMouth()
            => new BoneDefinition
            {
                Id = "mouth",
                Name = "mouth",
                ImageSlots =
                {
                    new ImageSlot { Name = "open", FilePath = "open.png" },
                    new ImageSlot { Name = "half", FilePath = "half.png" },
                    new ImageSlot { Name = "close", FilePath = "close.png" },
                },
                LipSync = new LipSyncSettings { SlotNames = { "open", "half", "close" } },
            };

        [Theory]
        [InlineData(0.0, 2)]  // 閉じている
        [InlineData(1.0, 0)]  // 全開
        [InlineData(0.5, 1)]  // 半開
        public void LipSync_SelectsSlotByOpenness(double lipSync, int expectedIndex)
        {
            var bone = CreateMouth();
            var pose = BonePose.Identity;
            var index = SlotSelector.Select(bone, new EvaluationContext { LipSyncValue = lipSync }, ref pose);

            Assert.Equal(expectedIndex, index);
        }

        [Fact]
        public void LipSync_ScaleInfluenceStretchesMouth()
        {
            var bone = CreateMouth();
            bone.LipSync!.ScaleInfluence = 0.5f;

            var pose = BonePose.Identity;
            SlotSelector.Select(bone, new EvaluationContext { LipSyncValue = 1.0 }, ref pose);

            Assert.Equal(1.5f, pose.ScaleY, 4);
        }

        [Fact]
        public void ManualSelection_TakesPriorityOverLipSync()
        {
            var bone = CreateMouth();
            var pose = BonePose.Identity;
            var context = new EvaluationContext
            {
                LipSyncValue = 1.0,
                ManualSlotSelection = new Dictionary<string, int> { ["mouth"] = 2 },
            };

            Assert.Equal(2, SlotSelector.Select(bone, context, ref pose));
        }

        [Fact]
        public void ManualSelection_ClampsOutOfRangeIndex()
        {
            var bone = CreateMouth();
            var pose = BonePose.Identity;
            var context = new EvaluationContext
            {
                ManualSlotSelection = new Dictionary<string, int> { ["mouth"] = 99 },
            };

            Assert.Equal(2, SlotSelector.Select(bone, context, ref pose));
        }

        [Fact]
        public void Blink_IsDeterministicForSameTime()
        {
            var blink = new BlinkSettings { IntervalSeconds = 3f, DurationSeconds = 0.2f };
            var context = new EvaluationContext { Time = 7.5, BlinkSeed = 42 };

            var first = SlotSelector.GetBlinkAmount(blink, context);
            var second = SlotSelector.GetBlinkAmount(blink, context);

            Assert.Equal(first, second);
        }

        [Fact]
        public void Blink_ClosesEyesAtSomePointInEachCycle()
        {
            var blink = new BlinkSettings { IntervalSeconds = 2f, DurationSeconds = 0.2f };
            var maxClose = 0f;

            // 1周期分を細かくサンプリングすれば必ず閉じる瞬間がある
            for (var t = 0.0; t < 2.0; t += 1.0 / 240.0)
            {
                var amount = SlotSelector.GetBlinkAmount(blink, new EvaluationContext { Time = t, BlinkSeed = 7 });
                maxClose = Math.Max(maxClose, amount);
                Assert.InRange(amount, 0f, 1f);
            }

            Assert.True(maxClose > 0.8f, $"まばたきが発生しなかった: {maxClose}");
        }

        [Fact]
        public void Blink_ReturnsZeroForNegativeTime()
        {
            var blink = new BlinkSettings();
            Assert.Equal(0f, SlotSelector.GetBlinkAmount(blink, new EvaluationContext { Time = -1.0 }));
        }

        [Fact]
        public void Select_ReturnsZero_WhenNoSettings()
        {
            var bone = new BoneDefinition { Id = "x" };
            var pose = BonePose.Identity;
            Assert.Equal(0, SlotSelector.Select(bone, new EvaluationContext(), ref pose));
        }
    }
}
