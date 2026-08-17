using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Shape;
using YukkuriMovieMaker.Project;
using Ymm4BonePlugin.Core;
using Ymm4BonePlugin.Views;

namespace Ymm4BonePlugin.Shape
{
    /// <summary>
    /// 2Dボーン図形の設定項目。
    /// ボーン一覧を保持し、アイテム編集エリアへボーン階層エディタを表示する。
    /// </summary>
    internal class BoneShapeParameter(SharedDataStore? sharedData) : ShapeParameterBase(sharedData)
    {
        /// <summary>
        /// ボーン一覧。TreeViewエディタから階層編集される。
        /// </summary>
        [Display(GroupName = "ボーン階層", Name = "", Description = "ドラッグ＆ドロップで親子関係を変更できます")]
        [BoneTreeEditor(PropertyEditorSize = PropertyEditorSize.FullWidth)]
        public ImmutableList<BoneItem> Bones { get => bones; set => Set(ref bones, value); }
        ImmutableList<BoneItem> bones = CreateDefaultSkeleton();

        [Display(GroupName = "全体設定", Name = "物理演算を有効化", Description = "揺れもの設定を持つボーンの物理演算を実行します")]
        [ToggleSlider]
        public bool IsPhysicsEnabled { get => isPhysicsEnabled; set => Set(ref isPhysicsEnabled, value); }
        bool isPhysicsEnabled = true;

        [Display(GroupName = "全体設定", Name = "目パチを有効化")]
        [ToggleSlider]
        public bool IsBlinkEnabled { get => isBlinkEnabled; set => Set(ref isBlinkEnabled, value); }
        bool isBlinkEnabled = true;

        [Display(GroupName = "全体設定", Name = "目パチシード", Description = "まばたきのタイミングを変えるための値")]
        [TextBoxSlider("F0", "", 0, 9999)]
        [DefaultValue(1234)]
        [Range(0, int.MaxValue)]
        public int BlinkSeed { get => blinkSeed; set => Set(ref blinkSeed, value); }
        int blinkSeed = 1234;

        // 図形の描画内容はプレビューと出力で共通のため、ガイドは出力にも含まれる。
        // 出力前にオフにする必要がある旨をDescriptionで明示する。
        [Display(GroupName = "全体設定", Name = "ボーンを表示", Description = "ボーンのガイド線を描画します。動画出力にも含まれるため、書き出す前にオフにしてください")]
        [ToggleSlider]
        public bool ShowBoneGuide { get => showBoneGuide; set => Set(ref showBoneGuide, value); }
        bool showBoneGuide = true;

        [Display(GroupName = "テンプレート", Name = "", Description = "ボーン構造をJSONファイルとして保存・読み込みします")]
        [TemplateIoEditor(PropertyEditorSize = PropertyEditorSize.FullWidth)]
        public string TemplateIo { get => templateIo; set => Set(ref templateIo, value); }
        string templateIo = string.Empty;

        //必ず引数なしのコンストラクタを定義する。これがないとプロジェクトファイルの読み込みに失敗する。
        public BoneShapeParameter() : this(null)
        {
        }

        /// <summary>既定のボーン構成（体 → 頭 → 髪）を作る。</summary>
        static ImmutableList<BoneItem> CreateDefaultSkeleton()
        {
            var body = new BoneItem("体") { Length = 120, BaseZOrder = 0 };
            var head = new BoneItem("頭", body.Id) { Length = 80, BaseZOrder = 10 };
            head.Rotation.Values[0].Value = -90;
            return [body, head];
        }

        /// <summary>
        /// Core層の <see cref="Skeleton"/> を構築する。
        /// </summary>
        public Skeleton BuildSkeleton()
        {
            var skeleton = new Skeleton();

            // 先に全ボーンを親なしで登録し、その後に親子関係を張る。
            // 順序に依存せず、循環参照は Skeleton 側で拒否される。
            var parentMap = new Dictionary<string, string>();
            foreach (var item in Bones)
            {
                var definition = item.ToBoneDefinition();
                if (!string.IsNullOrEmpty(definition.ParentId))
                    parentMap[definition.Id] = definition.ParentId!;
                definition.ParentId = null;
                skeleton.Add(definition);
            }

            foreach (var pair in parentMap)
                skeleton.SetParent(pair.Key, pair.Value);

            return skeleton;
        }

        /// <summary>exo出力は非対応。</summary>
        public override IEnumerable<string> CreateShapeItemExoFilter(int keyFrameIndex, ExoOutputDescription desc) => [];

        /// <summary>exo出力は非対応。</summary>
        public override IEnumerable<string> CreateMaskExoFilter(int keyFrameIndex, ExoOutputDescription desc, ShapeMaskExoOutputDescription shapeMaskDesc) => [];

        /// <summary>描画処理を行う図形ソースを生成する。</summary>
        public override IShapeSource CreateShapeSource(IGraphicsDevicesAndContext devices)
            => new BoneShapeSource(devices, this);

        /// <summary>キーフレーム補間の対象となるプロパティを返す。</summary>
        protected override IEnumerable<IAnimatable> GetAnimatables() => Bones;

        /// <summary>図形の種類を切り替えたときに設定を復元する。</summary>
        protected override void LoadSharedData(SharedDataStore store)
        {
            var data = store.Load<SharedData>();
            if (data is null)
                return;
            data.CopyTo(this);
        }

        /// <summary>図形の種類を切り替えたときのために設定を一時保存する。</summary>
        protected override void SaveSharedData(SharedDataStore store)
            => store.Save(new SharedData(this));

        /// <summary>設定の一時保存用クラス。</summary>
        public class SharedData(BoneShapeParameter parameter)
        {
            // Animationは参照をそのまま持たず、必ずコピーを保持する。
            public ImmutableList<BoneItem> Bones { get; } = [.. parameter.Bones.Select(b => new BoneItem(b))];
            public bool IsPhysicsEnabled { get; } = parameter.IsPhysicsEnabled;
            public bool IsBlinkEnabled { get; } = parameter.IsBlinkEnabled;
            public int BlinkSeed { get; } = parameter.BlinkSeed;
            public bool ShowBoneGuide { get; } = parameter.ShowBoneGuide;

            public void CopyTo(BoneShapeParameter parameter)
            {
                parameter.Bones = [.. Bones.Select(b => new BoneItem(b))];
                parameter.IsPhysicsEnabled = IsPhysicsEnabled;
                parameter.IsBlinkEnabled = IsBlinkEnabled;
                parameter.BlinkSeed = BlinkSeed;
                parameter.ShowBoneGuide = ShowBoneGuide;
            }
        }
    }
}
