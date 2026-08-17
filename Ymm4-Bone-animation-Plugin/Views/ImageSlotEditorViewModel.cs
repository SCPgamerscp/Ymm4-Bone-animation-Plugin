using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Linq;
using YukkuriMovieMaker.Commons;
using Ymm4BoneAnimationPlugin.Shape;

namespace Ymm4BoneAnimationPlugin.Views
{
    /// <summary>
    /// 差分画像スロット一覧エディタのViewModel。
    /// </summary>
    internal class ImageSlotEditorViewModel : Bindable, IPropertyEditorControl, IDisposable
    {
        readonly INotifyPropertyChanged item;
        readonly ItemProperty[] properties;

        public event EventHandler? BeginEdit;
        public event EventHandler? EndEdit;

        public List<BoneImageSlot> Slots { get => slots; set => Set(ref slots, value); }
        List<BoneImageSlot> slots = new();

        public int SelectedIndex { get => selectedIndex; set => Set(ref selectedIndex, value); }
        int selectedIndex = 0;

        public ActionCommand AddCommand { get; }
        public ActionCommand RemoveCommand { get; }
        public ActionCommand MoveUpCommand { get; }
        public ActionCommand MoveDownCommand { get; }

        public ImageSlotEditorViewModel(ItemProperty[] properties)
        {
            this.properties = properties;
            item = (INotifyPropertyChanged)properties[0].PropertyOwner;
            item.PropertyChanged += OnItemPropertyChanged;

            AddCommand = new ActionCommand(
                _ => true,
                _ =>
                {
                    var index = SelectedIndex;
                    BeginEdit?.Invoke(this, EventArgs.Empty);
                    var updated = Slots.Select(s => new BoneImageSlot(s)).ToList();
                    updated.Insert(
                        Math.Clamp(index + 1, 0, updated.Count),
                        new BoneImageSlot($"差分{updated.Count + 1}", string.Empty));
                    Commit(updated);
                    EndEdit?.Invoke(this, EventArgs.Empty);
                    SelectedIndex = Math.Clamp(index + 1, 0, Slots.Count - 1);
                });

            RemoveCommand = new ActionCommand(
                _ => Slots.Count > 1 && SelectedIndex >= 0,
                _ =>
                {
                    var index = SelectedIndex;
                    BeginEdit?.Invoke(this, EventArgs.Empty);
                    var updated = Slots.Select(s => new BoneImageSlot(s)).ToList();
                    updated.RemoveAt(index);
                    Commit(updated);
                    EndEdit?.Invoke(this, EventArgs.Empty);
                    SelectedIndex = Math.Clamp(index, 0, Slots.Count - 1);
                });

            MoveUpCommand = new ActionCommand(
                _ => SelectedIndex > 0,
                _ => Move(-1));

            MoveDownCommand = new ActionCommand(
                _ => SelectedIndex >= 0 && SelectedIndex < Slots.Count - 1,
                _ => Move(1));

            Reload();
        }

        void Move(int offset)
        {
            var index = SelectedIndex;
            var newIndex = index + offset;
            if (index < 0 || newIndex < 0 || newIndex >= Slots.Count)
                return;

            BeginEdit?.Invoke(this, EventArgs.Empty);
            var updated = Slots.Select(s => new BoneImageSlot(s)).ToList();
            var moved = updated[index];
            updated.RemoveAt(index);
            updated.Insert(newIndex, moved);
            Commit(updated);
            EndEdit?.Invoke(this, EventArgs.Empty);

            SelectedIndex = newIndex;
        }

        void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == properties[0].PropertyInfo.Name)
                Reload();
        }

        void Reload()
        {
            var values = properties[0].GetValue<ImmutableList<BoneImageSlot>>() ?? [];
            if (!Slots.SequenceEqual(values))
                Slots = [.. values];

            if (SelectedIndex >= Slots.Count)
                SelectedIndex = Math.Max(0, Slots.Count - 1);

            foreach (var command in new[] { AddCommand, RemoveCommand, MoveUpCommand, MoveDownCommand })
                command.RaiseCanExecuteChanged();
        }

        void Commit(List<BoneImageSlot> updated)
        {
            foreach (var property in properties)
                property.SetValue(updated.Select(s => new BoneImageSlot(s)).ToImmutableList());
        }

        /// <summary>現在の内容を他の選択アイテムへコピーする。</summary>
        public void CopyToOtherItems()
        {
            foreach (var property in properties.Skip(1))
                property.SetValue(Slots.Select(s => new BoneImageSlot(s)).ToImmutableList());
        }

        public void Dispose()
        {
            item.PropertyChanged -= OnItemPropertyChanged;
        }
    }
}
