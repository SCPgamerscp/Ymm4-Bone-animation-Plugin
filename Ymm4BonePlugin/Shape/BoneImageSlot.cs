using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;

namespace Ymm4BonePlugin.Shape
{
    /// <summary>
    /// ボーンに紐づく画像スロット（表情・手などの差分1枚分）。
    /// </summary>
    public class BoneImageSlot : Animatable
    {
        [Display(GroupName = "差分画像", Name = "名前", Description = "口パク・目パチ設定から参照する名前")]
        [TextEditor]
        public string Name { get => name; set => Set(ref name, value); }
        string name = "通常";

        [Display(GroupName = "差分画像", Name = "画像ファイル")]
        [FileSelector(YukkuriMovieMaker.Settings.FileGroupType.ImageItem)]
        public string FilePath { get => filePath; set => Set(ref filePath, value); }
        string filePath = string.Empty;

        public BoneImageSlot()
        {
        }

        public BoneImageSlot(string name, string filePath)
        {
            this.name = name;
            this.filePath = filePath;
        }

        public BoneImageSlot(BoneImageSlot source)
        {
            name = source.Name;
            filePath = source.FilePath;
        }

        protected override IEnumerable<IAnimatable> GetAnimatables() => [];
    }
}
