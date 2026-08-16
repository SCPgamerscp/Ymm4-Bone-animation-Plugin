using System.Numerics;
using Xunit;
using Ymm4BonePlugin.Core;

namespace Ymm4BonePlugin.Core.Tests
{
    public class SkeletonTests
    {
        static BoneDefinition Bone(string id, string? parentId = null, float length = 100f)
            => new BoneDefinition { Id = id, Name = id, ParentId = parentId, Length = length };

        [Fact]
        public void Add_AssignsNewId_WhenIdDuplicates()
        {
            var skeleton = new Skeleton();
            skeleton.Add(Bone("a"));
            var second = skeleton.Add(new BoneDefinition { Id = "a", Name = "dup" });

            Assert.NotEqual("a", second.Id);
            Assert.Equal(2, skeleton.Count);
        }

        [Fact]
        public void Add_TreatsMissingParentAsRoot()
        {
            var skeleton = new Skeleton();
            var bone = skeleton.Add(Bone("child", "nonexistent"));

            Assert.True(bone.IsRoot);
            Assert.Null(bone.ParentId);
        }

        [Fact]
        public void SetParent_RejectsCircularReference()
        {
            var skeleton = new Skeleton();
            skeleton.Add(Bone("root"));
            skeleton.Add(Bone("child", "root"));
            skeleton.Add(Bone("grandchild", "child"));

            // root を自身の子孫の下に付けようとする → 拒否される
            Assert.False(skeleton.SetParent("root", "grandchild"));
            Assert.Null(skeleton.Find("root")!.ParentId);

            // 自分自身を親にする → 拒否される
            Assert.False(skeleton.SetParent("child", "child"));
            Assert.Equal("root", skeleton.Find("child")!.ParentId);
        }

        [Fact]
        public void SetParent_AllowsValidReparent()
        {
            var skeleton = new Skeleton();
            skeleton.Add(Bone("root"));
            skeleton.Add(Bone("a", "root"));
            skeleton.Add(Bone("b", "root"));

            Assert.True(skeleton.SetParent("b", "a"));
            Assert.Equal("a", skeleton.Find("b")!.ParentId);

            // ルート化
            Assert.True(skeleton.SetParent("b", null));
            Assert.True(skeleton.Find("b")!.IsRoot);
        }

        [Fact]
        public void Remove_PromotesChildrenToGrandparent()
        {
            var skeleton = new Skeleton();
            skeleton.Add(Bone("root"));
            skeleton.Add(Bone("middle", "root"));
            skeleton.Add(Bone("leaf", "middle"));

            Assert.True(skeleton.Remove("middle"));
            Assert.Equal(2, skeleton.Count);
            Assert.Equal("root", skeleton.Find("leaf")!.ParentId);
        }

        [Fact]
        public void GetTopologicalOrder_PlacesParentsBeforeChildren()
        {
            var skeleton = new Skeleton();
            // あえて子を先に追加する
            skeleton.Add(Bone("root"));
            skeleton.Add(Bone("c", "b"));
            skeleton.Add(Bone("b", "root"));
            skeleton.SetParent("c", "b");

            var order = skeleton.GetTopologicalOrder();
            var ids = order.Select(b => b.Id).ToList();

            Assert.Equal(3, ids.Count);
            Assert.True(ids.IndexOf("root") < ids.IndexOf("b"));
            Assert.True(ids.IndexOf("b") < ids.IndexOf("c"));
        }

        [Fact]
        public void GetChain_ReturnsEndToRootOrder()
        {
            var skeleton = new Skeleton();
            skeleton.Add(Bone("shoulder"));
            skeleton.Add(Bone("upper", "shoulder"));
            skeleton.Add(Bone("lower", "upper"));

            var chain = skeleton.GetChain("lower", 2);

            Assert.Equal(2, chain.Count);
            Assert.Equal("lower", chain[0].Id);
            Assert.Equal("upper", chain[1].Id);
        }

        [Fact]
        public void Clone_ProducesIndependentCopy()
        {
            var skeleton = new Skeleton();
            var bone = skeleton.Add(Bone("root"));
            bone.ImageSlots.Add(new ImageSlot { Name = "normal", FilePath = "a.png" });

            var clone = skeleton.Clone();
            clone.Find("root")!.Name = "changed";
            clone.Find("root")!.ImageSlots[0].FilePath = "b.png";

            Assert.Equal("root", skeleton.Find("root")!.Name);
            Assert.Equal("a.png", skeleton.Find("root")!.ImageSlots[0].FilePath);
        }
    }
}
