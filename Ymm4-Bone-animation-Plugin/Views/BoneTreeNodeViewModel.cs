using System.Collections.Generic;
using System.Collections.ObjectModel;
using YukkuriMovieMaker.Commons;
using Ymm4BoneAnimationPlugin.Shape;

namespace Ymm4BoneAnimationPlugin.Views
{
    /// <summary>
    /// TreeViewの1ノードに対応するViewModel。
    /// </summary>
    internal class BoneTreeNodeViewModel : Bindable
    {
        public BoneTreeNodeViewModel(BoneItem item)
        {
            Item = item;
        }

        /// <summary>対応するボーンの設定項目。</summary>
        public BoneItem Item { get; }

        public string Id => Item.Id;

        /// <summary>TreeViewに表示する名前。</summary>
        public string DisplayName
        {
            get
            {
                var name = string.IsNullOrWhiteSpace(Item.Name) ? "(名前なし)" : Item.Name;
                var marks = new List<string>();
                if (Item.IsIkEnabled)
                    marks.Add("IK");
                if (Item.IsPhysicsEnabled)
                    marks.Add("揺れ");
                if (Item.IsLipSyncEnabled)
                    marks.Add("口");
                if (Item.IsBlinkEnabled)
                    marks.Add("目");
                return marks.Count > 0 ? $"{name}  [{string.Join("/", marks)}]" : name;
            }
        }

        /// <summary>子ノード。</summary>
        public ObservableCollection<BoneTreeNodeViewModel> Children { get; } = new();

        public bool IsExpanded { get => isExpanded; set => Set(ref isExpanded, value); }
        bool isExpanded = true;

        public bool IsSelected { get => isSelected; set => Set(ref isSelected, value); }
        bool isSelected;

        /// <summary>表示名の再評価を促す。</summary>
        public void RefreshDisplayName() => OnPropertyChanged(nameof(DisplayName));
    }
}
