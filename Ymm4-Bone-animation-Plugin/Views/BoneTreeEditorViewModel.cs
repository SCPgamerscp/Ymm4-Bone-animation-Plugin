using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using YukkuriMovieMaker.Commons;
using Ymm4BoneAnimationPlugin.Core;
using Ymm4BoneAnimationPlugin.Shape;

namespace Ymm4BoneAnimationPlugin.Views
{
    /// <summary>
    /// ボーン階層エディタのViewModel。
    /// ボーンの追加・削除・並べ替え・親子の繋ぎ替えを担当する。
    /// </summary>
    internal class BoneTreeEditorViewModel : Bindable, IPropertyEditorControl, IDisposable
    {
        readonly INotifyPropertyChanged item;
        readonly ItemProperty[] properties;

        public event EventHandler? BeginEdit;
        public event EventHandler? EndEdit;

        /// <summary>ツリーのルートノード一覧。</summary>
        public ObservableCollection<BoneTreeNodeViewModel> RootNodes { get; } = new();

        /// <summary>現在のボーン一覧（フラット）。</summary>
        public List<BoneItem> Bones { get => bones; private set => Set(ref bones, value); }
        List<BoneItem> bones = new();

        /// <summary>選択中のボーン。プロパティグリッドの編集対象になる。</summary>
        public BoneItem? SelectedBone
        {
            get => selectedBone;
            set
            {
                if (Set(ref selectedBone, value))
                {
                    if (item is BoneShapeParameter param && param.SelectedBoneId != value?.Id)
                        param.SelectedBoneId = value?.Id;
                    RaiseCommandStates();
                }
            }
        }
        BoneItem? selectedBone;

        public ActionCommand AddCommand { get; }
        public ActionCommand AddChildCommand { get; }
        public ActionCommand RemoveCommand { get; }
        public ActionCommand MoveUpCommand { get; }
        public ActionCommand MoveDownCommand { get; }
        public ActionCommand UnparentCommand { get; }
        public ActionCommand AddImagesCommand { get; }
        public ActionCommand OpenPuppetEditorCommand { get; }

        public BoneTreeEditorViewModel(ItemProperty[] properties)
        {
            this.properties = properties;
            item = (INotifyPropertyChanged)properties[0].PropertyOwner;
            item.PropertyChanged += OnItemPropertyChanged;

            AddCommand = new ActionCommand(
                _ => true,
                _ => AddBone(asChild: false));

            AddChildCommand = new ActionCommand(
                _ => SelectedBone != null,
                _ => AddBone(asChild: true));

            RemoveCommand = new ActionCommand(
                _ => SelectedBone != null && Bones.Count > 1,
                _ => RemoveSelected());

            MoveUpCommand = new ActionCommand(
                _ => IndexOfSelected() > 0,
                _ => MoveSelected(-1));

            MoveDownCommand = new ActionCommand(
                _ => IndexOfSelected() >= 0 && IndexOfSelected() < Bones.Count - 1,
                _ => MoveSelected(1));

            UnparentCommand = new ActionCommand(
                _ => SelectedBone != null && !string.IsNullOrEmpty(SelectedBone.ParentId),
                _ => SetParent(SelectedBone!.Id, string.Empty));

            AddImagesCommand = new ActionCommand(
                _ => true,
                _ => SelectAndAddImages());

            OpenPuppetEditorCommand = new ActionCommand(
                _ => true,
                _ => OpenPuppetEditor());

            Reload();
        }

        void OpenPuppetEditor()
        {
            var editorVm = new PuppetEditorViewModel(Bones.ToImmutableList());
            var window = new PuppetEditorWindow(editorVm);
            if (Application.Current?.MainWindow != null)
                window.Owner = Application.Current.MainWindow;

            window.ShowDialog();

            if (window.IsApplied)
            {
                BeginEdit?.Invoke(this, EventArgs.Empty);
                var exported = editorVm.ExportToBoneItems();
                Commit(exported.ToList());
                EndEdit?.Invoke(this, EventArgs.Empty);

                SelectedBone = Bones.FirstOrDefault();
            }
        }

        void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == properties[0].PropertyInfo.Name)
            {
                Reload();
            }
            else if (e.PropertyName == nameof(BoneShapeParameter.SelectedBoneId))
            {
                if (item is BoneShapeParameter param && SelectedBone?.Id != param.SelectedBoneId)
                    SyncSelectedBone(param.SelectedBoneId);
            }
        }

        void SyncSelectedBone(string? boneId)
        {
            if (string.IsNullOrEmpty(boneId))
            {
                SelectedBone = null;
                return;
            }

            var target = Bones.FirstOrDefault(b => b.Id == boneId);
            if (target != null && selectedBone?.Id != boneId)
            {
                selectedBone = target;
                OnPropertyChanged(nameof(SelectedBone));

                void Walk(IEnumerable<BoneTreeNodeViewModel> nodes)
                {
                    foreach (var node in nodes)
                    {
                        node.IsSelected = (node.Id == boneId);
                        Walk(node.Children);
                    }
                }
                Walk(RootNodes);
                RaiseCommandStates();
            }
        }

        void SelectAndAddImages()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "パーツ画像の選択（複数可）",
                Filter = "画像ファイル (*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.svg)|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.svg|すべてのファイル (*.*)|*.*",
                Multiselect = true,
            };

            if (dialog.ShowDialog() == true && dialog.FileNames.Length > 0)
            {
                AddBonesFromFiles(dialog.FileNames);
            }
        }

        /// <summary>
        /// 画像ファイル一覧からボーンを一括生成して追加する。
        /// </summary>
        public void AddBonesFromFiles(IEnumerable<string> filePaths)
        {
            var validFiles = filePaths
                .Where(f => !string.IsNullOrWhiteSpace(f) && System.IO.File.Exists(f))
                .ToList();

            if (validFiles.Count == 0)
                return;

            BeginEdit?.Invoke(this, EventArgs.Empty);

            var updated = Bones.Select(b => new BoneItem(b)).ToList();
            var baseOrder = updated.Count == 0 ? 0 : updated.Max(b => b.BaseZOrder) + 1;
            var parentId = SelectedBone?.Id ?? string.Empty;

            BoneItem? lastAdded = null;
            foreach (var file in validFiles)
            {
                var fileName = System.IO.Path.GetFileNameWithoutExtension(file);
                var newBone = new BoneItem(fileName, parentId)
                {
                    BaseZOrder = baseOrder++,
                    ImageSlots = [new BoneImageSlot(fileName, file)],
                };
                updated.Add(newBone);
                lastAdded = newBone;
            }

            Commit(updated);
            EndEdit?.Invoke(this, EventArgs.Empty);

            if (lastAdded != null)
                SelectedBone = Bones.FirstOrDefault(b => b.Id == lastAdded.Id);
        }

        /// <summary>プロパティから現在の値を読み直し、ツリーを再構築する。</summary>
        void Reload()
        {
            var values = properties[0].GetValue<ImmutableList<BoneItem>>() ?? [];
            if (!Bones.SequenceEqual(values))
                Bones = [.. values];

            RebuildTree();
            RaiseCommandStates();
        }

        /// <summary>フラットなボーン一覧から親子ツリーを組み立てる。</summary>
        void RebuildTree()
        {
            var previousSelectedId = SelectedBone?.Id;
            var expandedIds = CollectExpandedIds();
            var isFirstBuild = RootNodes.Count == 0;

            RootNodes.Clear();

            // IDが重複していても落ちないよう、最初の1件のみを採用する
            var nodes = new Dictionary<string, BoneTreeNodeViewModel>();
            foreach (var bone in Bones)
            {
                if (!nodes.ContainsKey(bone.Id))
                    nodes[bone.Id] = new BoneTreeNodeViewModel(bone);
            }

            // 循環している親子関係を事前に洗い出す
            var circularIds = FindCircularIds();

            foreach (var bone in Bones)
            {
                if (!nodes.TryGetValue(bone.Id, out var node))
                    continue;

                node.IsExpanded = isFirstBuild || expandedIds.Contains(bone.Id);

                if (!string.IsNullOrEmpty(bone.ParentId)
                    && !circularIds.Contains(bone.Id)
                    && nodes.TryGetValue(bone.ParentId, out var parentNode)
                    && !ReferenceEquals(parentNode, node))
                {
                    parentNode.Children.Add(node);
                }
                else
                {
                    // 親が見つからない・循環している場合はルートとして表示する
                    RootNodes.Add(node);
                }
            }

            // 選択状態を復元する
            if (previousSelectedId != null && nodes.TryGetValue(previousSelectedId, out var selectedNode))
            {
                selectedNode.IsSelected = true;
                selectedBone = selectedNode.Item;
                OnPropertyChanged(nameof(SelectedBone));
            }
        }

        HashSet<string> CollectExpandedIds()
        {
            var result = new HashSet<string>();
            void Walk(IEnumerable<BoneTreeNodeViewModel> nodes)
            {
                foreach (var node in nodes)
                {
                    if (node.IsExpanded)
                        result.Add(node.Id);
                    Walk(node.Children);
                }
            }
            Walk(RootNodes);
            return result;
        }

        /// <summary>親を辿ると循環してしまうボーンのID一覧を返す。</summary>
        HashSet<string> FindCircularIds()
        {
            var parentLookup = new Dictionary<string, string?>();
            foreach (var bone in Bones)
                parentLookup[bone.Id] = bone.ParentId;

            var circular = new HashSet<string>();

            foreach (var bone in Bones)
            {
                var visited = new HashSet<string> { bone.Id };
                var current = bone.ParentId;
                var guard = 0;

                while (!string.IsNullOrEmpty(current) && guard++ <= Bones.Count + 1)
                {
                    if (!visited.Add(current!))
                    {
                        circular.Add(bone.Id);
                        break;
                    }
                    if (!parentLookup.TryGetValue(current!, out current))
                        break;
                }
            }

            return circular;
        }

        /// <summary>
        /// 親子関係を変更する。循環参照になる場合は変更せず false を返す。
        /// TreeViewのドラッグ＆ドロップから呼び出される。
        /// </summary>
        public bool SetParent(string boneId, string? newParentId)
        {
            var bone = Bones.FirstOrDefault(b => b.Id == boneId);
            if (bone is null)
                return false;

            var normalizedParentId = string.IsNullOrEmpty(newParentId) ? null : newParentId;

            // 変更が無い場合は何もしない
            var currentParentId = string.IsNullOrEmpty(bone.ParentId) ? null : bone.ParentId;
            if (currentParentId == normalizedParentId)
                return false;

            // Core層のSkeletonで循環チェックを行い、描画時と同じ判定を使う
            var skeleton = BuildSkeletonForValidation();
            if (!skeleton.SetParent(boneId, normalizedParentId))
                return false;

            BeginEdit?.Invoke(this, EventArgs.Empty);

            var updated = Bones.Select(b => new BoneItem(b)).ToList();
            var targetBone = updated.First(b => b.Id == boneId);
            targetBone.ParentId = normalizedParentId ?? string.Empty;
            Commit(updated);

            EndEdit?.Invoke(this, EventArgs.Empty);

            SelectedBone = Bones.FirstOrDefault(b => b.Id == boneId);
            return true;
        }

        /// <summary>循環チェック用に、現在の状態からCore層のSkeletonを構築する。</summary>
        Skeleton BuildSkeletonForValidation()
        {
            var skeleton = new Skeleton();
            var parentMap = new Dictionary<string, string>();

            foreach (var bone in Bones)
            {
                skeleton.Add(new BoneDefinition { Id = bone.Id, Name = bone.Name });
                if (!string.IsNullOrEmpty(bone.ParentId))
                    parentMap[bone.Id] = bone.ParentId;
            }

            foreach (var pair in parentMap)
                skeleton.SetParent(pair.Key, pair.Value);

            return skeleton;
        }

        void AddBone(bool asChild)
        {
            BeginEdit?.Invoke(this, EventArgs.Empty);

            var updated = Bones.Select(b => new BoneItem(b)).ToList();
            var parentId = asChild && SelectedBone != null ? SelectedBone.Id : string.Empty;
            var newBone = new BoneItem($"ボーン{updated.Count + 1}", parentId)
            {
                BaseZOrder = updated.Count == 0 ? 0 : updated.Max(b => b.BaseZOrder) + 1,
            };

            // 選択中ボーンの直後に挿入する
            var insertIndex = SelectedBone != null
                ? updated.FindIndex(b => b.Id == SelectedBone.Id) + 1
                : updated.Count;
            updated.Insert(Math.Clamp(insertIndex, 0, updated.Count), newBone);

            Commit(updated);
            EndEdit?.Invoke(this, EventArgs.Empty);

            SelectedBone = Bones.FirstOrDefault(b => b.Id == newBone.Id);
        }

        void RemoveSelected()
        {
            if (SelectedBone is null || Bones.Count <= 1)
                return;

            var removeId = SelectedBone.Id;
            var removedParentId = SelectedBone.ParentId;

            BeginEdit?.Invoke(this, EventArgs.Empty);

            var updated = Bones
                .Where(b => b.Id != removeId)
                .Select(b => new BoneItem(b))
                .ToList();

            // 子ボーンは削除されたボーンの親へ引き継ぐ
            foreach (var child in updated.Where(b => b.ParentId == removeId))
                child.ParentId = removedParentId;

            Commit(updated);
            EndEdit?.Invoke(this, EventArgs.Empty);

            SelectedBone = Bones.FirstOrDefault();
        }

        void MoveSelected(int offset)
        {
            var index = IndexOfSelected();
            var newIndex = index + offset;
            if (index < 0 || newIndex < 0 || newIndex >= Bones.Count)
                return;

            var movedId = SelectedBone!.Id;

            BeginEdit?.Invoke(this, EventArgs.Empty);

            var updated = Bones.Select(b => new BoneItem(b)).ToList();
            var moved = updated[index];
            updated.RemoveAt(index);
            updated.Insert(newIndex, moved);

            Commit(updated);
            EndEdit?.Invoke(this, EventArgs.Empty);

            SelectedBone = Bones.FirstOrDefault(b => b.Id == movedId);
        }

        int IndexOfSelected()
            => SelectedBone is null ? -1 : Bones.FindIndex(b => b.Id == SelectedBone.Id);

        /// <summary>変更内容を全ての選択アイテムへ書き込む。</summary>
        void Commit(List<BoneItem> updated)
        {
            foreach (var property in properties)
                property.SetValue(updated.Select(b => new BoneItem(b)).ToImmutableList());
        }

        /// <summary>
        /// 現在のボーン一覧を他の選択アイテムへコピーする。
        /// 個々のボーン設定を変更した後に呼ぶ必要がある。
        /// </summary>
        public void CopyToOtherItems()
        {
            foreach (var property in properties.Skip(1))
                property.SetValue(Bones.Select(b => new BoneItem(b)).ToImmutableList());
        }

        /// <summary>選択中ボーンの表示名を更新する。</summary>
        public void RefreshSelectedNode()
        {
            void Walk(IEnumerable<BoneTreeNodeViewModel> nodes)
            {
                foreach (var node in nodes)
                {
                    node.RefreshDisplayName();
                    Walk(node.Children);
                }
            }
            Walk(RootNodes);
        }

        /// <summary>JSONテンプレートから読み込んだボーン構造を適用する。</summary>
        public void ApplyTemplate(SkeletonTemplate template)
        {
            if (template is null)
                return;

            BeginEdit?.Invoke(this, EventArgs.Empty);

            var updated = new List<BoneItem>();
            foreach (var boneTemplate in template.Bones)
            {
                var bone = new BoneItem(boneTemplate.Name, boneTemplate.ParentId ?? string.Empty)
                {
                    Id = string.IsNullOrEmpty(boneTemplate.Id) ? Guid.NewGuid().ToString("N") : boneTemplate.Id,
                    Length = boneTemplate.Length,
                    AnchorX = boneTemplate.AnchorX,
                    AnchorY = boneTemplate.AnchorY,
                    BaseZOrder = boneTemplate.BaseZOrder,
                };

                if (boneTemplate.ImageSlots.Count > 0)
                    bone.ImageSlots = [.. boneTemplate.ImageSlots.Select(s => new BoneImageSlot(s.Name, s.FilePath))];

                if (boneTemplate.Physics is { } physics)
                {
                    bone.IsPhysicsEnabled = true;
                    bone.Stiffness = physics.Stiffness;
                    bone.Damping = physics.Damping;
                    bone.Inertia = physics.Inertia;
                    bone.Gravity = physics.Gravity;
                    bone.AngleLimit = physics.AngleLimit;
                }

                if (boneTemplate.LipSync is { } lipSync)
                {
                    bone.IsLipSyncEnabled = true;
                    bone.LipSyncSlotNames = string.Join(",", lipSync.SlotNames);
                    bone.LipSyncScaleInfluence = lipSync.ScaleInfluence;
                }

                if (boneTemplate.Blink is { } blink)
                {
                    bone.IsBlinkEnabled = true;
                    bone.BlinkInterval = blink.IntervalSeconds;
                    bone.BlinkDuration = blink.DurationSeconds;
                    bone.BlinkSlotNames = string.Join(",", blink.SlotNames);
                }

                if (boneTemplate.Ik is { } ik)
                {
                    bone.IsIkEnabled = ik.IsEnabled;
                    bone.IkChainLength = ik.ChainLength;
                    bone.IkFlipBend = ik.FlipBend;
                    bone.IkTargetX.Values[0].Value = ik.TargetX;
                    bone.IkTargetY.Values[0].Value = ik.TargetY;
                }

                updated.Add(bone);
            }

            if (updated.Count == 0)
                updated.Add(new BoneItem("ボーン1"));

            Commit(updated);
            EndEdit?.Invoke(this, EventArgs.Empty);

            SelectedBone = Bones.FirstOrDefault();
        }

        /// <summary>現在のボーン構造をJSONテンプレートへ変換する。</summary>
        public SkeletonTemplate CreateTemplate(string name = "Skeleton")
        {
            var skeleton = new Skeleton();
            var parentMap = new Dictionary<string, string>();

            foreach (var bone in Bones)
            {
                var definition = bone.ToBoneDefinition();
                if (!string.IsNullOrEmpty(definition.ParentId))
                    parentMap[definition.Id] = definition.ParentId!;
                definition.ParentId = null;
                skeleton.Add(definition);
            }

            foreach (var pair in parentMap)
                skeleton.SetParent(pair.Key, pair.Value);

            // IKターゲットの初期値をテンプレートへ反映する
            foreach (var definition in skeleton.Bones)
            {
                var source = Bones.FirstOrDefault(b => b.Id == definition.Id);
                if (source?.IsIkEnabled == true && definition.Ik != null)
                {
                    definition.Ik.Target = new System.Numerics.Vector2(
                        (float)source.IkTargetX.Values[0].Value,
                        (float)source.IkTargetY.Values[0].Value);
                }
            }

            return SkeletonTemplate.FromSkeleton(skeleton, name);
        }

        void RaiseCommandStates()
        {
            AddCommand.RaiseCanExecuteChanged();
            AddChildCommand.RaiseCanExecuteChanged();
            RemoveCommand.RaiseCanExecuteChanged();
            MoveUpCommand.RaiseCanExecuteChanged();
            MoveDownCommand.RaiseCanExecuteChanged();
            UnparentCommand.RaiseCanExecuteChanged();
        }

        public void Dispose()
        {
            item.PropertyChanged -= OnItemPropertyChanged;
        }
    }
}
