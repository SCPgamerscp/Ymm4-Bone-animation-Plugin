using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using YukkuriMovieMaker.Commons;

namespace Ymm4BoneAnimationPlugin.Views
{
    /// <summary>
    /// ボーン階層エディタ。
    /// TreeViewのドラッグ＆ドロップで親子関係を変更できる。
    /// </summary>
    public partial class BoneTreeEditor : UserControl, IPropertyEditorControl
    {
        public event EventHandler? BeginEdit;
        public event EventHandler? EndEdit;

        Point dragStartPoint;
        BoneTreeNodeViewModel? draggedNode;

        public BoneTreeEditor()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is BoneTreeEditorViewModel oldVm)
            {
                oldVm.BeginEdit -= PropertiesEditor_BeginEdit;
                oldVm.EndEdit -= PropertiesEditor_EndEdit;
            }
            if (e.NewValue is BoneTreeEditorViewModel newVm)
            {
                newVm.BeginEdit += PropertiesEditor_BeginEdit;
                newVm.EndEdit += PropertiesEditor_EndEdit;
            }
        }

        void PropertiesEditor_BeginEdit(object? sender, EventArgs e)
            => BeginEdit?.Invoke(this, e);

        void PropertiesEditor_EndEdit(object? sender, EventArgs e)
        {
            // ボーン個別の設定を変更した際に、複数選択中の他アイテムへも反映する
            if (DataContext is BoneTreeEditorViewModel vm)
            {
                vm.CopyToOtherItems();
                vm.RefreshSelectedNode();
            }
            EndEdit?.Invoke(this, e);
        }

        void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (DataContext is BoneTreeEditorViewModel vm && e.NewValue is BoneTreeNodeViewModel node)
                vm.SelectedBone = node.Item;
        }

        #region ドラッグ＆ドロップによる親子関係の変更

        void Tree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            dragStartPoint = e.GetPosition(null);
            draggedNode = FindNode(e.OriginalSource as DependencyObject);
        }

        void Tree_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || draggedNode is null)
                return;

            var position = e.GetPosition(null);
            var diff = dragStartPoint - position;

            // 誤操作を防ぐため、一定距離動いてからドラッグを開始する
            if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            try
            {
                DragDrop.DoDragDrop(tree, draggedNode, DragDropEffects.Move);
            }
            catch (Exception)
            {
                // ドラッグ中の例外でYMM4を巻き込まない
            }
            finally
            {
                draggedNode = null;
            }
        }

        void Tree_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
                return;
            }

            e.Effects = CanDrop(e) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        bool CanDrop(DragEventArgs e)
        {
            if (DataContext is not BoneTreeEditorViewModel vm)
                return false;
            if (e.Data.GetData(typeof(BoneTreeNodeViewModel)) is not BoneTreeNodeViewModel source)
                return false;

            var target = FindNode(e.OriginalSource as DependencyObject);

            // ルート化（何もない場所へのドロップ）は常に許可
            if (target is null)
                return !string.IsNullOrEmpty(source.Item.ParentId);

            if (ReferenceEquals(target, source))
                return false;

            // 既に同じ親であれば変更不要
            return target.Id != source.Item.ParentId;
        }

        void Tree_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;

            if (DataContext is not BoneTreeEditorViewModel vm)
                return;

            // ファイルのドラッグ＆ドロップ（パーツ画像一括追加）
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                {
                    vm.AddBonesFromFiles(files);
                }
                return;
            }

            // ボーンノードのドラッグ＆ドロップ（親子関係変更）
            if (e.Data.GetData(typeof(BoneTreeNodeViewModel)) is not BoneTreeNodeViewModel source)
                return;

            var target = FindNode(e.OriginalSource as DependencyObject);

            // 自分自身へのドロップは無視する
            if (target != null && ReferenceEquals(target, source))
                return;

            // 循環参照になる場合、ViewModel側で false が返り変更されない
            var newParentId = target?.Id ?? string.Empty;
            if (!vm.SetParent(source.Id, newParentId) && target != null)
            {
                MessageBox.Show(
                    "そのボーンは自分自身の子孫にはできません。",
                    "ボーン階層の変更",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        /// <summary>クリック位置のビジュアルツリーを遡ってノードを特定する。</summary>
        static BoneTreeNodeViewModel? FindNode(DependencyObject? source)
        {
            while (source != null)
            {
                if (source is TreeViewItem item)
                    return item.DataContext as BoneTreeNodeViewModel;
                if (source is TreeView)
                    return null;

                source = source is Visual or System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(source)
                    : LogicalTreeHelper.GetParent(source);
            }
            return null;
        }

        #endregion
    }
}
