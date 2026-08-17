using System.Numerics;
using Xunit;
using Ymm4BoneAnimationPlugin.Core;

namespace Ymm4BoneAnimationPlugin.Core.Tests
{
    public class TemplateTests
    {
        static Skeleton CreateSample()
        {
            var skeleton = new Skeleton();
            skeleton.Add(new BoneDefinition
            {
                Id = "body",
                Name = "体",
                Length = 120f,
                AnchorPoint = new Vector2(0.5f, 0.9f),
                BaseZOrder = 0,
                ImageSlots = { new ImageSlot { Name = "normal", FilePath = @"C:\img\body.png" } },
            });
            skeleton.Add(new BoneDefinition
            {
                Id = "hair",
                Name = "髪",
                ParentId = "body",
                Length = 90f,
                Physics = new PhysicsSettings { Stiffness = 15f, Damping = 2.5f, AngleLimit = 30f },
            });
            skeleton.Add(new BoneDefinition
            {
                Id = "hand",
                Name = "手",
                ParentId = "body",
                Length = 60f,
                Ik = new IkSettings { IsEnabled = true, ChainLength = 2, Target = new Vector2(150f, -40f), FlipBend = true },
                Blink = new BlinkSettings { IntervalSeconds = 3f, SlotNames = { "open", "close" } },
                LipSync = new LipSyncSettings { ScaleInfluence = 0.3f, SlotNames = { "a", "i" } },
            });
            return skeleton;
        }

        [Fact]
        public void RoundTrip_PreservesStructureAndSettings()
        {
            var original = CreateSample();
            var json = SkeletonTemplate.FromSkeleton(original, "テスト").ToJson();

            var restored = SkeletonTemplate.FromJson(json)!.ToSkeleton();

            Assert.Equal(3, restored.Count);

            var body = restored.Find("body")!;
            Assert.Equal("体", body.Name);
            Assert.Equal(120f, body.Length);
            Assert.Equal(new Vector2(0.5f, 0.9f), body.AnchorPoint);
            Assert.Equal(@"C:\img\body.png", body.ImageSlots[0].FilePath);

            var hair = restored.Find("hair")!;
            Assert.Equal("body", hair.ParentId);
            Assert.Equal(15f, hair.Physics!.Stiffness);
            Assert.Equal(30f, hair.Physics!.AngleLimit);

            var hand = restored.Find("hand")!;
            Assert.True(hand.Ik!.IsEnabled);
            Assert.True(hand.Ik!.FlipBend);
            Assert.Equal(new Vector2(150f, -40f), hand.Ik!.Target);
            Assert.Equal(3f, hand.Blink!.IntervalSeconds);
            Assert.Equal(0.3f, hand.LipSync!.ScaleInfluence);
        }

        [Fact]
        public void RoundTrip_ProducesIdenticalEvaluation()
        {
            var original = CreateSample();
            var restored = SkeletonTemplate.FromJson(SkeletonTemplate.FromSkeleton(original).ToJson())!.ToSkeleton();

            var context = new EvaluationContext { EnablePhysics = false, Time = 1.0 };
            var a = new SkeletonEvaluator().Evaluate(original, _ => BonePose.Identity, context);
            var b = new SkeletonEvaluator().Evaluate(restored, _ => BonePose.Identity, context);

            Assert.Equal(a.Count, b.Count);
            for (var i = 0; i < a.Count; i++)
            {
                Assert.Equal(a[i].Bone.Id, b[i].Bone.Id);
                Assert.Equal(a[i].Origin.X, b[i].Origin.X, 3);
                Assert.Equal(a[i].Origin.Y, b[i].Origin.Y, 3);
                Assert.Equal(a[i].WorldRotation, b[i].WorldRotation, 3);
            }
        }

        [Fact]
        public void ToSkeleton_RestoresParentsRegardlessOfOrder()
        {
            // 子が先に並んだJSONでも親子関係が復元される
            var json = """
            {
              "Version": 1,
              "Name": "reordered",
              "Bones": [
                { "Id": "child", "Name": "child", "ParentId": "parent", "Length": 50 },
                { "Id": "parent", "Name": "parent", "Length": 100 }
              ]
            }
            """;

            var skeleton = SkeletonTemplate.FromJson(json)!.ToSkeleton();

            Assert.Equal(2, skeleton.Count);
            Assert.Equal("parent", skeleton.Find("child")!.ParentId);
        }

        [Fact]
        public void FromJson_ReturnsNull_ForInvalidJson()
        {
            Assert.Null(SkeletonTemplate.FromJson("{ this is not json"));
            Assert.Null(SkeletonTemplate.FromJson(""));
            Assert.Null(SkeletonTemplate.FromJson("   "));
        }

        [Fact]
        public void ToSkeleton_DropsCircularParentReference()
        {
            // 相互に親を指す不正データ
            var json = """
            {
              "Bones": [
                { "Id": "a", "ParentId": "b" },
                { "Id": "b", "ParentId": "a" }
              ]
            }
            """;

            var skeleton = SkeletonTemplate.FromJson(json)!.ToSkeleton();

            // 循環は SetParent に拒否され、少なくとも1つはルートになる
            Assert.Equal(2, skeleton.Count);
            Assert.True(skeleton.GetRoots().Any());

            // 評価しても無限ループにならない
            var result = new SkeletonEvaluator().Evaluate(skeleton, _ => BonePose.Identity, new EvaluationContext());
            Assert.Equal(2, result.Count);
        }

        [Fact]
        public void ToSkeleton_GeneratesIdForMissingId()
        {
            var json = """{ "Bones": [ { "Name": "noid" } ] }""";
            var skeleton = SkeletonTemplate.FromJson(json)!.ToSkeleton();

            Assert.Single(skeleton.Bones);
            Assert.False(string.IsNullOrEmpty(skeleton.Bones[0].Id));
        }
    }

    public class MathHelperTests
    {
        [Theory]
        [InlineData(0f, 0f)]
        [InlineData(180f, 180f)]
        [InlineData(190f, -170f)]
        [InlineData(-190f, 170f)]
        [InlineData(720f, 0f)]
        [InlineData(float.NaN, 0f)]
        public void NormalizeDegrees_WrapsToPlusMinus180(float input, float expected)
            => Assert.Equal(expected, MathHelper.NormalizeDegrees(input), 3);

        [Fact]
        public void DeltaDegrees_TakesShortestPath()
        {
            Assert.Equal(20f, MathHelper.DeltaDegrees(350f, 10f), 3);
            Assert.Equal(-20f, MathHelper.DeltaDegrees(10f, 350f), 3);
        }

        [Fact]
        public void LerpDegrees_CrossesZeroCorrectly()
            => Assert.Equal(0f, MathHelper.NormalizeDegrees(MathHelper.LerpDegrees(350f, 10f, 0.5f)), 3);

        [Fact]
        public void SafeDivide_ReturnsFallbackOnZero()
            => Assert.Equal(99f, MathHelper.SafeDivide(1f, 0f, 99f));

        [Fact]
        public void ToDegrees_And_FromDegrees_RoundTrip()
        {
            var vector = MathHelper.FromDegrees(37f, 5f);
            Assert.Equal(37f, MathHelper.ToDegrees(vector), 2);
            Assert.Equal(5f, vector.Length(), 3);
        }

        [Fact]
        public void ToDegrees_ReturnsZeroForZeroVector()
            => Assert.Equal(0f, MathHelper.ToDegrees(Vector2.Zero));

        [Fact]
        public void InvertOrIdentity_ReturnsIdentityForSingularMatrix()
        {
            var singular = new Matrix3x2(0, 0, 0, 0, 0, 0);
            Assert.Equal(Matrix3x2.Identity, MathHelper.InvertOrIdentity(singular));
        }
    }
}
