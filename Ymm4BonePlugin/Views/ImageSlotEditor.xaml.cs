using System;
using System.Windows;
using System.Windows.Controls;
using YukkuriMovieMaker.Commons;

namespace Ymm4BonePlugin.Views
{
    /// <summary>
    /// 差分画像スロット一覧エディタ。
    /// </summary>
    public partial class ImageSlotEditor : UserControl, IPropertyEditorControl
    {
        public event EventHandler? BeginEdit;
        public event EventHandler? EndEdit;

        public ImageSlotEditor()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is ImageSlotEditorViewModel oldVm)
            {
                oldVm.BeginEdit -= PropertiesEditor_BeginEdit;
                oldVm.EndEdit -= PropertiesEditor_EndEdit;
            }
            if (e.NewValue is ImageSlotEditorViewModel newVm)
            {
                newVm.BeginEdit += PropertiesEditor_BeginEdit;
                newVm.EndEdit += PropertiesEditor_EndEdit;
            }
        }

        void PropertiesEditor_BeginEdit(object? sender, EventArgs e)
            => BeginEdit?.Invoke(this, e);

        void PropertiesEditor_EndEdit(object? sender, EventArgs e)
        {
            // 複数アイテム選択時に、変更内容を他のアイテムへも反映する
            (DataContext as ImageSlotEditorViewModel)?.CopyToOtherItems();
            EndEdit?.Invoke(this, e);
        }
    }
}
