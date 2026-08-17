using System;
using System.Collections.Generic;
using System.Linq;

namespace Ymm4BoneAnimationPlugin.Core
{
    /// <summary>
    /// ボーン階層構造の保持と、循環参照を防ぎながらの親子繋ぎ替えを担当する。
    /// </summary>
    public sealed class Skeleton
    {
        readonly List<BoneDefinition> bones = new List<BoneDefinition>();

        public IReadOnlyList<BoneDefinition> Bones => bones;

        public int Count => bones.Count;

        public Skeleton()
        {
        }

        public Skeleton(IEnumerable<BoneDefinition> bones)
        {
            if (bones is null)
                throw new ArgumentNullException(nameof(bones));

            foreach (var bone in bones)
                Add(bone);
        }

        /// <summary>ボーンを追加する。IDが重複する場合は新しいIDを振り直す。</summary>
        public BoneDefinition Add(BoneDefinition bone)
        {
            if (bone is null)
                throw new ArgumentNullException(nameof(bone));

            if (string.IsNullOrEmpty(bone.Id) || bones.Any(b => b.Id == bone.Id))
                bone.Id = Guid.NewGuid().ToString("N");

            // 存在しない親を指している場合はルート扱いにする。
            if (!string.IsNullOrEmpty(bone.ParentId) && !bones.Any(b => b.Id == bone.ParentId))
                bone.ParentId = null;

            bones.Add(bone);
            return bone;
        }

        /// <summary>IDでボーンを取得する。存在しない場合はnull。</summary>
        public BoneDefinition? Find(string? id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            for (var i = 0; i < bones.Count; i++)
            {
                if (bones[i].Id == id)
                    return bones[i];
            }
            return null;
        }

        /// <summary>
        /// ボーンを削除する。子ボーンは削除されたボーンの親へ引き継がれる。
        /// </summary>
        public bool Remove(string id)
        {
            var bone = Find(id);
            if (bone is null)
                return false;

            foreach (var child in bones.Where(b => b.ParentId == id))
                child.ParentId = bone.ParentId;

            return bones.Remove(bone);
        }

        /// <summary>指定ボーンの直接の子を取得する。</summary>
        public IEnumerable<BoneDefinition> GetChildren(string id)
            => bones.Where(b => b.ParentId == id);

        /// <summary>ルートボーン一覧を取得する。</summary>
        public IEnumerable<BoneDefinition> GetRoots()
            => bones.Where(b => b.IsRoot);

        /// <summary>
        /// 親子関係を変更する。循環参照になる場合は false を返して変更しない。
        /// TreeView のドラッグ＆ドロップから呼び出される。
        /// </summary>
        public bool SetParent(string boneId, string? newParentId)
        {
            var bone = Find(boneId);
            if (bone is null)
                return false;

            if (string.IsNullOrEmpty(newParentId))
            {
                bone.ParentId = null;
                return true;
            }

            if (boneId == newParentId)
                return false;

            if (Find(newParentId) is null)
                return false;

            // newParent が bone の子孫であれば循環するため拒否する。
            if (IsDescendantOf(newParentId, boneId))
                return false;

            bone.ParentId = newParentId;
            return true;
        }

        /// <summary>candidateId が ancestorId の子孫であるかどうか。</summary>
        public bool IsDescendantOf(string candidateId, string ancestorId)
        {
            var current = Find(candidateId);
            var guard = 0;
            while (current != null && guard++ <= bones.Count)
            {
                if (current.ParentId == ancestorId)
                    return true;
                current = Find(current.ParentId);
            }
            return false;
        }

        /// <summary>
        /// 親が必ず子より前に来る順序（トポロジカル順）でボーンを列挙する。
        /// 万一循環が存在した場合でも、残りを末尾に付与して無限ループを避ける。
        /// </summary>
        public IReadOnlyList<BoneDefinition> GetTopologicalOrder()
        {
            var result = new List<BoneDefinition>(bones.Count);
            var visited = new HashSet<string>();
            var childLookup = bones
                .Where(b => !b.IsRoot)
                .GroupBy(b => b.ParentId!)
                .ToDictionary(g => g.Key, g => g.ToList());

            void Visit(BoneDefinition bone)
            {
                if (!visited.Add(bone.Id))
                    return;
                result.Add(bone);
                if (childLookup.TryGetValue(bone.Id, out var children))
                {
                    foreach (var child in children)
                        Visit(child);
                }
            }

            foreach (var root in bones.Where(b => b.IsRoot))
                Visit(root);

            // 循環等で未訪問のボーンが残っていれば末尾に追加する。
            foreach (var bone in bones)
            {
                if (visited.Add(bone.Id))
                    result.Add(bone);
            }

            return result;
        }

        /// <summary>
        /// 末端ボーンから親方向へ最大 length 本のチェーンを取得する。
        /// 戻り値は [末端, 親, 祖父...] の順。
        /// </summary>
        public IReadOnlyList<BoneDefinition> GetChain(string endBoneId, int length)
        {
            var chain = new List<BoneDefinition>();
            var current = Find(endBoneId);
            while (current != null && chain.Count < length)
            {
                chain.Add(current);
                current = Find(current.ParentId);
            }
            return chain;
        }

        public Skeleton Clone() => new Skeleton(bones.Select(b => b.Clone()));
    }
}
