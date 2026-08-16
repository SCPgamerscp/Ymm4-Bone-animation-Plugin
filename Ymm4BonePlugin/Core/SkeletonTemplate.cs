using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ymm4BonePlugin.Core
{
    /// <summary>
    /// ボーン構造をJSONで保存・読み込みするためのDTO。
    /// <see cref="Vector2"/> をそのままシリアライズせず、X/Yへ展開して互換性を確保する。
    /// </summary>
    public sealed class SkeletonTemplate
    {
        /// <summary>テンプレート形式のバージョン。将来の互換処理に使用する。</summary>
        public int Version { get; set; } = 1;

        /// <summary>テンプレート名。</summary>
        public string Name { get; set; } = "Skeleton";

        /// <summary>ボーン一覧。</summary>
        public List<BoneTemplate> Bones { get; set; } = new List<BoneTemplate>();

        static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true,
        };

        /// <summary><see cref="Skeleton"/> からテンプレートを生成する。</summary>
        public static SkeletonTemplate FromSkeleton(Skeleton skeleton, string name = "Skeleton")
        {
            if (skeleton is null)
                throw new ArgumentNullException(nameof(skeleton));

            var template = new SkeletonTemplate { Name = name };
            foreach (var bone in skeleton.Bones)
                template.Bones.Add(BoneTemplate.FromBone(bone));
            return template;
        }

        /// <summary>テンプレートから <see cref="Skeleton"/> を復元する。</summary>
        public Skeleton ToSkeleton()
        {
            var skeleton = new Skeleton();

            // 親が未追加でもIDが失われないよう、まず全ボーンを親なしで追加する。
            var parentMap = new Dictionary<string, string?>();
            foreach (var boneTemplate in Bones)
            {
                var bone = boneTemplate.ToBone();
                parentMap[bone.Id] = bone.ParentId;
                bone.ParentId = null;
                skeleton.Add(bone);
            }

            // 全追加後に親子関係を張り直す（循環は SetParent 側で拒否される）。
            foreach (var pair in parentMap)
            {
                if (!string.IsNullOrEmpty(pair.Value))
                    skeleton.SetParent(pair.Key, pair.Value);
            }

            return skeleton;
        }

        public string ToJson() => JsonSerializer.Serialize(this, Options);

        /// <summary>JSON文字列からテンプレートを読み込む。失敗時はnull。</summary>
        public static SkeletonTemplate? FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;
            try
            {
                return JsonSerializer.Deserialize<SkeletonTemplate>(json, Options);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    /// <summary>ボーン1本のJSON表現。</summary>
    public sealed class BoneTemplate
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = "Bone";
        public string? ParentId { get; set; }
        public float Length { get; set; } = 100f;
        public float AnchorX { get; set; } = 0.5f;
        public float AnchorY { get; set; } = 0.5f;
        public int BaseZOrder { get; set; }
        public List<ImageSlotTemplate> ImageSlots { get; set; } = new List<ImageSlotTemplate>();
        public PhysicsSettings? Physics { get; set; }
        public LipSyncSettings? LipSync { get; set; }
        public BlinkSettings? Blink { get; set; }
        public IkTemplate? Ik { get; set; }

        public static BoneTemplate FromBone(BoneDefinition bone)
        {
            var template = new BoneTemplate
            {
                Id = bone.Id,
                Name = bone.Name,
                ParentId = bone.ParentId,
                Length = bone.Length,
                AnchorX = bone.AnchorPoint.X,
                AnchorY = bone.AnchorPoint.Y,
                BaseZOrder = bone.BaseZOrder,
                Physics = bone.Physics?.Clone(),
                LipSync = bone.LipSync?.Clone(),
                Blink = bone.Blink?.Clone(),
                Ik = bone.Ik is null ? null : IkTemplate.FromSettings(bone.Ik),
            };
            foreach (var slot in bone.ImageSlots)
                template.ImageSlots.Add(new ImageSlotTemplate { Name = slot.Name, FilePath = slot.FilePath });
            return template;
        }

        public BoneDefinition ToBone()
        {
            var bone = new BoneDefinition
            {
                Id = string.IsNullOrEmpty(Id) ? Guid.NewGuid().ToString("N") : Id,
                Name = Name,
                ParentId = ParentId,
                Length = Length,
                AnchorPoint = new Vector2(AnchorX, AnchorY),
                BaseZOrder = BaseZOrder,
                Physics = Physics?.Clone(),
                LipSync = LipSync?.Clone(),
                Blink = Blink?.Clone(),
                Ik = Ik?.ToSettings(),
            };
            foreach (var slot in ImageSlots)
                bone.ImageSlots.Add(new ImageSlot { Name = slot.Name, FilePath = slot.FilePath });
            return bone;
        }
    }

    public sealed class ImageSlotTemplate
    {
        public string Name { get; set; } = "Default";
        public string FilePath { get; set; } = string.Empty;
    }

    /// <summary>IK設定のJSON表現（Vector2を展開）。</summary>
    public sealed class IkTemplate
    {
        public bool IsEnabled { get; set; }
        public int ChainLength { get; set; } = 2;
        public float TargetX { get; set; }
        public float TargetY { get; set; }
        public bool FlipBend { get; set; }
        public int Iterations { get; set; } = 12;
        public float Weight { get; set; } = 1f;

        public static IkTemplate FromSettings(IkSettings settings) => new IkTemplate
        {
            IsEnabled = settings.IsEnabled,
            ChainLength = settings.ChainLength,
            TargetX = settings.Target.X,
            TargetY = settings.Target.Y,
            FlipBend = settings.FlipBend,
            Iterations = settings.Iterations,
            Weight = settings.Weight,
        };

        public IkSettings ToSettings() => new IkSettings
        {
            IsEnabled = IsEnabled,
            ChainLength = ChainLength,
            Target = new Vector2(TargetX, TargetY),
            FlipBend = FlipBend,
            Iterations = Iterations,
            Weight = Weight,
        };
    }
}
