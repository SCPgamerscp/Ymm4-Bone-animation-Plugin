using System.Windows;
using YukkuriMovieMaker.Commons;

namespace Ymm4BonePlugin.Views
{
    /// <summary>
    /// JSONテンプレートの保存・読み込みUIを表示するための属性。
    /// </summary>
    internal class TemplateIoEditorAttribute : PropertyEditorAttribute2
    {
        public override FrameworkElement Create() => new TemplateIoEditor();

        public override void SetBindings(FrameworkElement control, ItemProperty[] itemProperties)
        {
            if (control is not TemplateIoEditor editor)
                return;
            editor.SetProperties(itemProperties);
        }

        public override void ClearBindings(FrameworkElement control)
        {
            if (control is not TemplateIoEditor editor)
                return;
            editor.SetProperties(null);
        }
    }
}
