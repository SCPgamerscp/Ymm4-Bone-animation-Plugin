using System.Windows;
using YukkuriMovieMaker.Commons;

namespace Ymm4BoneAnimationPlugin.Views
{
    /// <summary>
    /// ボーン階層エディタをアイテム編集エリアに表示するための属性。
    /// </summary>
    internal class BoneTreeEditorAttribute : PropertyEditorAttribute2
    {
        public override FrameworkElement Create() => new BoneTreeEditor();

        public override void SetBindings(FrameworkElement control, ItemProperty[] itemProperties)
        {
            if (control is not BoneTreeEditor editor)
                return;
            editor.DataContext = new BoneTreeEditorViewModel(itemProperties);
        }

        public override void ClearBindings(FrameworkElement control)
        {
            if (control is not BoneTreeEditor editor)
                return;
            (editor.DataContext as BoneTreeEditorViewModel)?.Dispose();
            editor.DataContext = null;
        }
    }
}
