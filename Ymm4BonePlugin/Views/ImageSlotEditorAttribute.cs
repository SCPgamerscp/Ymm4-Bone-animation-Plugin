using System.Windows;
using YukkuriMovieMaker.Commons;

namespace Ymm4BonePlugin.Views
{
    /// <summary>
    /// 差分画像スロット一覧エディタを表示するための属性。
    /// </summary>
    internal class ImageSlotEditorAttribute : PropertyEditorAttribute2
    {
        public override FrameworkElement Create() => new ImageSlotEditor();

        public override void SetBindings(FrameworkElement control, ItemProperty[] itemProperties)
        {
            if (control is not ImageSlotEditor editor)
                return;
            editor.DataContext = new ImageSlotEditorViewModel(itemProperties);
        }

        public override void ClearBindings(FrameworkElement control)
        {
            if (control is not ImageSlotEditor editor)
                return;
            (editor.DataContext as ImageSlotEditorViewModel)?.Dispose();
            editor.DataContext = null;
        }
    }
}
