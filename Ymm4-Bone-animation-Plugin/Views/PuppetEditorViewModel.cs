using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using YukkuriMovieMaker.Commons;
using Ymm4BoneAnimationPlugin.Shape;

namespace Ymm4BoneAnimationPlugin.Views
{
    /// <summary>
    /// 画像パーツ＝ボーン（骨）。
    /// 各画像パーツが独立したボーンであり、親子関係（骨組み）を結ぶことができる。
    /// </summary>
    public class PuppetImageLayerViewModel : Bindable
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public string FilePath { get; init; }
        public string FileName => Path.GetFileName(FilePath);
        public string PartName => Path.GetFileNameWithoutExtension(FilePath);

        public BitmapSource? Thumbnail { get => thumbnail; private set => Set(ref thumbnail, value); }
        BitmapSource? thumbnail;

        public double Width { get => width; set { if (Set(ref width, value)) UpdateJointPos(); } }
        double width = 200;

        public double Height { get => height; set { if (Set(ref height, value)) UpdateJointPos(); } }
        double height = 200;

        public double X { get => x; set { if (Set(ref x, value)) UpdateJointPos(); } }
        double x = 0;

        public double Y { get => y; set { if (Set(ref y, value)) UpdateJointPos(); } }
        double y = 0;

        public int ZOrder { get => zOrder; set => Set(ref zOrder, value); }
        int zOrder;

        // --- 親パーツ（ボーン結合先） ---
        public string? ParentId { get => parentId; set { if (Set(ref parentId, value)) OnPropertyChanged(nameof(HasParent)); } }
        string? parentId;

        public bool HasParent => !string.IsNullOrEmpty(ParentId);

        public string ParentName { get => parentName; set => Set(ref parentName, value); }
        string parentName = "（なし：ルート）";

        // --- 回転の中心関節（アンカーポイント 0~1） ---
        public double AnchorX { get => anchorX; set { if (Set(ref anchorX, value)) UpdateJointPos(); } }
        double anchorX = 0.5;

        public double AnchorY { get => anchorY; set { if (Set(ref anchorY, value)) UpdateJointPos(); } }
        double anchorY = 0.5;

        // --- キャンバス上の関節座標（ピン位置） ---
        public double JointX { get => jointX; private set => Set(ref jointX, value); }
        double jointX;

        public double JointY { get => jointY; private set => Set(ref jointY, value); }
        double jointY;

        void UpdateJointPos()
        {
            JointX = Math.Round(X + (AnchorX - 0.5) * Width, 1);
            JointY = Math.Round(Y + (AnchorY - 0.5) * Height, 1);
        }

        public void SetJointPos(double canvasX, double canvasY)
        {
            if (Width > 0 && Height > 0)
            {
                var left = X - Width / 2.0;
                var top = Y - Height / 2.0;
                AnchorX = Math.Clamp(Math.Round((canvasX - left) / Width, 3), 0.0, 1.0);
                AnchorY = Math.Clamp(Math.Round((canvasY - top) / Height, 3), 0.0, 1.0);
            }
        }

        // --- 口パク・目パチ設定 ---
        public bool IsLipSyncEnabled { get => isLipSyncEnabled; set => Set(ref isLipSyncEnabled, value); }
        bool isLipSyncEnabled;

        public string LipSyncSlotNames { get => lipSyncSlotNames; set => Set(ref lipSyncSlotNames, value); }
        string lipSyncSlotNames = string.Empty;

        public double LipSyncScaleInfluence { get => lipSyncScaleInfluence; set => Set(ref lipSyncScaleInfluence, value); }
        double lipSyncScaleInfluence;

        public bool IsBlinkEnabled { get => isBlinkEnabled; set => Set(ref isBlinkEnabled, value); }
        bool isBlinkEnabled;

        public string BlinkSlotNames { get => blinkSlotNames; set => Set(ref blinkSlotNames, value); }
        string blinkSlotNames = string.Empty;

        // --- IK・物理設定 ---
        public bool IsIkEnabled { get => isIkEnabled; set => Set(ref isIkEnabled, value); }
        bool isIkEnabled;

        public bool IsPhysicsEnabled { get => isPhysicsEnabled; set => Set(ref isPhysicsEnabled, value); }
        bool isPhysicsEnabled;

        public bool IsSelected { get => isSelected; set { if (Set(ref isSelected, value)) OnPropertyChanged(nameof(IsHighlighted)); } }
        bool isSelected;

        public bool IsHighlighted { get => isHighlighted || isSelected; set => Set(ref isHighlighted, value); }
        bool isHighlighted;

        /// <summary>元のBoneItem（既存ボーンのプロパティを保持するため）</summary>
        public BoneItem? OriginalBone { get; set; }

        public PuppetImageLayerViewModel(string filePath)
        {
            FilePath = filePath;
            LoadImage();
            UpdateJointPos();
        }

        void LoadImage()
        {
            if (!File.Exists(FilePath))
                return;
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(FilePath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();

                Thumbnail = bitmap;
                if (bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0)
                {
                    Width = bitmap.PixelWidth;
                    Height = bitmap.PixelHeight;
                }
            }
            catch
            {
                // ロード失敗時はスキップ
            }
        }
    }

    /// <summary>
    /// 親画像パーツ→子画像パーツを結ぶボーン接続線。
    /// </summary>
    public class PuppetBoneViewModel : Bindable
    {
        public PuppetImageLayerViewModel ParentLayer { get; }
        public PuppetImageLayerViewModel ChildLayer { get; }

        public double X1 => ParentLayer.JointX;
        public double Y1 => ParentLayer.JointY;
        public double X2 => ChildLayer.JointX;
        public double Y2 => ChildLayer.JointY;

        public PuppetBoneViewModel(PuppetImageLayerViewModel parent, PuppetImageLayerViewModel child)
        {
            ParentLayer = parent;
            ChildLayer = child;
            parent.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName is nameof(PuppetImageLayerViewModel.JointX) or nameof(PuppetImageLayerViewModel.JointY))
                {
                    OnPropertyChanged(nameof(X1));
                    OnPropertyChanged(nameof(Y1));
                }
            };
            child.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName is nameof(PuppetImageLayerViewModel.JointX) or nameof(PuppetImageLayerViewModel.JointY))
                {
                    OnPropertyChanged(nameof(X2));
                    OnPropertyChanged(nameof(Y2));
                }
            };
        }
    }

    /// <summary>
    /// 親パーツ選択ドロップダウン用アイテム
    /// </summary>
    public record ParentOption(string? Id, string Name);

    /// <summary>
    /// Undo / Redo 用のスナップショットデータ
    /// </summary>
    internal record struct LayerSnapshot(
        string Id,
        string FilePath,
        double X,
        double Y,
        double Width,
        double Height,
        int ZOrder,
        string? ParentId,
        double AnchorX,
        double AnchorY,
        bool IsLipSyncEnabled,
        string LipSyncSlotNames,
        double LipSyncScaleInfluence,
        bool IsBlinkEnabled,
        string BlinkSlotNames,
        bool IsIkEnabled,
        bool IsPhysicsEnabled,
        BoneItem? OriginalBone);

    internal record struct EditorSnapshot(ImmutableList<LayerSnapshot> Layers, string? SelectedLayerId);

    /// <summary>
    /// モードレス万能操作のパペットボーンエディタViewModel。
    /// </summary>
    public class PuppetEditorViewModel : Bindable
    {
        public ObservableCollection<PuppetImageLayerViewModel> ImageLayers { get; } = new();
        public ObservableCollection<PuppetBoneViewModel> Bones { get; } = new();

        readonly Stack<EditorSnapshot> undoStack = new();
        readonly Stack<EditorSnapshot> redoStack = new();

        public PuppetImageLayerViewModel? SelectedLayer
        {
            get => selectedLayer;
            set
            {
                if (selectedLayer != null)
                    selectedLayer.IsSelected = false;
                if (Set(ref selectedLayer, value) && selectedLayer != null)
                {
                    selectedLayer.IsSelected = true;
                }
                UpdateAvailableParents();
                RaiseCommandStates();
            }
        }
        PuppetImageLayerViewModel? selectedLayer;

        /// <summary>現在選択中のパーツが親として選択可能なパーツ一覧（自身や循環参照を除く）</summary>
        public ObservableCollection<ParentOption> AvailableParents { get; } = new();

        public double Zoom { get => zoom; set => Set(ref zoom, Math.Clamp(value, 0.1, 10.0)); }
        double zoom = 1.0;

        public double PanX { get => panX; set => Set(ref panX, value); }
        double panX = 0;

        public double PanY { get => panY; set => Set(ref panY, value); }
        double panY = 0;

        public ActionCommand ClearAllCommand { get; }
        public ActionCommand ResetViewCommand { get; }
        public ActionCommand AddImagesCommand { get; }
        public ActionCommand UndoCommand { get; }
        public ActionCommand RedoCommand { get; }
        public ActionCommand BringLayerToFrontCommand { get; }
        public ActionCommand BringLayerForwardCommand { get; }
        public ActionCommand SendLayerBackwardCommand { get; }
        public ActionCommand SendLayerToBackCommand { get; }
        public ActionCommand DeleteLayerCommand { get; }
        public ActionCommand UnlinkParentCommand { get; }
        public ActionCommand ConnectToPreviousLayerCommand { get; }

        public PuppetEditorViewModel(ImmutableList<BoneItem> existingBones)
        {
            ClearAllCommand = new ActionCommand(_ => ImageLayers.Count > 0, _ => ClearAll());
            ResetViewCommand = new ActionCommand(_ => true, _ => ResetView());
            AddImagesCommand = new ActionCommand(_ => true, _ => SelectImages());
            UndoCommand = new ActionCommand(_ => undoStack.Count > 0, _ => Undo());
            RedoCommand = new ActionCommand(_ => redoStack.Count > 0, _ => Redo());
            BringLayerToFrontCommand = new ActionCommand(_ => SelectedLayer != null, _ => BringSelectedLayerToFront());
            BringLayerForwardCommand = new ActionCommand(_ => SelectedLayer != null, _ => ChangeSelectedLayerZOrder(1));
            SendLayerBackwardCommand = new ActionCommand(_ => SelectedLayer != null, _ => ChangeSelectedLayerZOrder(-1));
            SendLayerToBackCommand = new ActionCommand(_ => SelectedLayer != null, _ => SendSelectedLayerToBack());
            DeleteLayerCommand = new ActionCommand(_ => SelectedLayer != null, _ => DeleteSelectedLayer());
            UnlinkParentCommand = new ActionCommand(_ => SelectedLayer?.HasParent == true, _ => SetLayerParent(SelectedLayer, null));
            ConnectToPreviousLayerCommand = new ActionCommand(_ => CanConnectToPrevious(), _ => ConnectToPrevious());

            ImageLayers.CollectionChanged += (s, e) =>
            {
                RebuildBoneConnections();
                UpdateAvailableParents();
            };

            ImportExistingBones(existingBones);
        }

        bool CanConnectToPrevious()
        {
            if (SelectedLayer == null || ImageLayers.Count <= 1)
                return false;
            var index = ImageLayers.IndexOf(SelectedLayer);
            return index > 0;
        }

        void ConnectToPrevious()
        {
            if (!CanConnectToPrevious() || SelectedLayer == null)
                return;
            var index = ImageLayers.IndexOf(SelectedLayer);
            var prev = ImageLayers[index - 1];
            ConnectLayers(prev, SelectedLayer);
        }

        void RaiseCommandStates()
        {
            ClearAllCommand.RaiseCanExecuteChanged();
            UndoCommand.RaiseCanExecuteChanged();
            RedoCommand.RaiseCanExecuteChanged();
            BringLayerToFrontCommand.RaiseCanExecuteChanged();
            BringLayerForwardCommand.RaiseCanExecuteChanged();
            SendLayerBackwardCommand.RaiseCanExecuteChanged();
            SendLayerToBackCommand.RaiseCanExecuteChanged();
            DeleteLayerCommand.RaiseCanExecuteChanged();
            UnlinkParentCommand.RaiseCanExecuteChanged();
            ConnectToPreviousLayerCommand.RaiseCanExecuteChanged();
        }

        #region Undo / Redo

        public void PushSnapshot()
        {
            var layerList = ImageLayers.Select(l => new LayerSnapshot(
                l.Id,
                l.FilePath,
                l.X,
                l.Y,
                l.Width,
                l.Height,
                l.ZOrder,
                l.ParentId,
                l.AnchorX,
                l.AnchorY,
                l.IsLipSyncEnabled,
                l.LipSyncSlotNames,
                l.LipSyncScaleInfluence,
                l.IsBlinkEnabled,
                l.BlinkSlotNames,
                l.IsIkEnabled,
                l.IsPhysicsEnabled,
                l.OriginalBone)).ToImmutableList();

            undoStack.Push(new EditorSnapshot(layerList, SelectedLayer?.Id));
            redoStack.Clear();
            RaiseCommandStates();
        }

        public void Undo()
        {
            if (undoStack.Count == 0)
                return;

            var layerList = ImageLayers.Select(l => new LayerSnapshot(
                l.Id,
                l.FilePath,
                l.X,
                l.Y,
                l.Width,
                l.Height,
                l.ZOrder,
                l.ParentId,
                l.AnchorX,
                l.AnchorY,
                l.IsLipSyncEnabled,
                l.LipSyncSlotNames,
                l.LipSyncScaleInfluence,
                l.IsBlinkEnabled,
                l.BlinkSlotNames,
                l.IsIkEnabled,
                l.IsPhysicsEnabled,
                l.OriginalBone)).ToImmutableList();

            redoStack.Push(new EditorSnapshot(layerList, SelectedLayer?.Id));

            var snapshot = undoStack.Pop();
            RestoreSnapshot(snapshot);
            RaiseCommandStates();
        }

        public void Redo()
        {
            if (redoStack.Count == 0)
                return;

            var layerList = ImageLayers.Select(l => new LayerSnapshot(
                l.Id,
                l.FilePath,
                l.X,
                l.Y,
                l.Width,
                l.Height,
                l.ZOrder,
                l.ParentId,
                l.AnchorX,
                l.AnchorY,
                l.IsLipSyncEnabled,
                l.LipSyncSlotNames,
                l.LipSyncScaleInfluence,
                l.IsBlinkEnabled,
                l.BlinkSlotNames,
                l.IsIkEnabled,
                l.IsPhysicsEnabled,
                l.OriginalBone)).ToImmutableList();

            undoStack.Push(new EditorSnapshot(layerList, SelectedLayer?.Id));

            var snapshot = redoStack.Pop();
            RestoreSnapshot(snapshot);
            RaiseCommandStates();
        }

        void RestoreSnapshot(EditorSnapshot snapshot)
        {
            ImageLayers.Clear();
            foreach (var l in snapshot.Layers)
            {
                var layer = new PuppetImageLayerViewModel(l.FilePath)
                {
                    Id = l.Id,
                    X = l.X,
                    Y = l.Y,
                    Width = l.Width,
                    Height = l.Height,
                    ZOrder = l.ZOrder,
                    ParentId = l.ParentId,
                    AnchorX = l.AnchorX,
                    AnchorY = l.AnchorY,
                    IsLipSyncEnabled = l.IsLipSyncEnabled,
                    LipSyncSlotNames = l.LipSyncSlotNames,
                    LipSyncScaleInfluence = l.LipSyncScaleInfluence,
                    IsBlinkEnabled = l.IsBlinkEnabled,
                    BlinkSlotNames = l.BlinkSlotNames,
                    IsIkEnabled = l.IsIkEnabled,
                    IsPhysicsEnabled = l.IsPhysicsEnabled,
                    OriginalBone = l.OriginalBone,
                };
                ImageLayers.Add(layer);
            }

            RebuildBoneConnections();
            SelectedLayer = ImageLayers.FirstOrDefault(l => l.Id == snapshot.SelectedLayerId);
        }

        #endregion

        void ImportExistingBones(ImmutableList<BoneItem> existingBones)
        {
            if (existingBones == null || existingBones.Count == 0)
                return;

            var layerMap = new Dictionary<string, PuppetImageLayerViewModel>();

            foreach (var bone in existingBones)
            {
                var imagePath = bone.ImageSlots.FirstOrDefault(s => !string.IsNullOrEmpty(s.FilePath))?.FilePath;
                if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                    continue;

                var pinX = bone.X.Values.Count > 0 ? bone.X.Values[0].Value : 0;
                var pinY = bone.Y.Values.Count > 0 ? bone.Y.Values[0].Value : 0;

                var layer = new PuppetImageLayerViewModel(imagePath)
                {
                    Id = bone.Id,
                    X = pinX,
                    Y = pinY,
                    ZOrder = bone.BaseZOrder,
                    ParentId = bone.ParentId,
                    AnchorX = bone.AnchorX,
                    AnchorY = bone.AnchorY,
                    IsLipSyncEnabled = bone.IsLipSyncEnabled,
                    LipSyncSlotNames = bone.LipSyncSlotNames,
                    LipSyncScaleInfluence = bone.LipSyncScaleInfluence,
                    IsBlinkEnabled = bone.IsBlinkEnabled,
                    BlinkSlotNames = bone.BlinkSlotNames,
                    IsIkEnabled = bone.IsIkEnabled,
                    IsPhysicsEnabled = bone.IsPhysicsEnabled,
                    OriginalBone = bone,
                };
                layerMap[bone.Id] = layer;
                ImageLayers.Add(layer);
            }

            RebuildBoneConnections();
            if (ImageLayers.Count > 0)
                SelectedLayer = ImageLayers[0];
        }

        /// <summary>
        /// 画像パーツAと画像パーツBをボーン（親子関係）で結ぶ
        /// </summary>
        public void ConnectLayers(PuppetImageLayerViewModel parentLayer, PuppetImageLayerViewModel childLayer)
        {
            if (parentLayer == null || childLayer == null || ReferenceEquals(parentLayer, childLayer))
                return;

            // 循環参照チェック
            var current = parentLayer;
            while (current != null)
            {
                if (current.Id == childLayer.Id)
                    return;
                current = ImageLayers.FirstOrDefault(l => l.Id == current.ParentId);
            }

            PushSnapshot();
            childLayer.ParentId = parentLayer.Id;
            RebuildBoneConnections();
            SelectedLayer = childLayer;
        }

        public void SetLayerParent(PuppetImageLayerViewModel? layer, string? newParentId)
        {
            if (layer == null || layer.ParentId == newParentId)
                return;

            if (!string.IsNullOrEmpty(newParentId))
            {
                var parent = ImageLayers.FirstOrDefault(l => l.Id == newParentId);
                if (parent != null)
                {
                    ConnectLayers(parent, layer);
                    return;
                }
            }

            PushSnapshot();
            layer.ParentId = null;
            RebuildBoneConnections();
            RaiseCommandStates();
        }

        public void RebuildBoneConnections()
        {
            Bones.Clear();
            foreach (var layer in ImageLayers)
            {
                if (!string.IsNullOrEmpty(layer.ParentId))
                {
                    var parent = ImageLayers.FirstOrDefault(l => l.Id == layer.ParentId);
                    if (parent != null)
                    {
                        layer.ParentName = parent.PartName;
                        Bones.Add(new PuppetBoneViewModel(parent, layer));
                    }
                    else
                    {
                        layer.ParentName = "（なし：ルート）";
                    }
                }
                else
                {
                    layer.ParentName = "（なし：ルート）";
                }
            }
            UpdateAvailableParents();
        }

        public void UpdateAvailableParents()
        {
            AvailableParents.Clear();
            AvailableParents.Add(new ParentOption(null, "（なし：ルートパーツ）"));

            if (SelectedLayer == null)
                return;

            // 自身の子孫を除いた選択可能な親パーツリスト
            var descendants = GetDescendantIds(SelectedLayer.Id);

            foreach (var layer in ImageLayers)
            {
                if (layer.Id != SelectedLayer.Id && !descendants.Contains(layer.Id))
                {
                    AvailableParents.Add(new ParentOption(layer.Id, $"🦴 {layer.PartName}"));
                }
            }
        }

        HashSet<string> GetDescendantIds(string rootId)
        {
            var result = new HashSet<string>();
            var queue = new Queue<string>();
            queue.Enqueue(rootId);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var child in ImageLayers.Where(l => l.ParentId == current))
                {
                    if (result.Add(child.Id))
                        queue.Enqueue(child.Id);
                }
            }
            return result;
        }

        public void DeleteSelectedLayer()
        {
            if (SelectedLayer == null)
                return;

            PushSnapshot();
            var removeLayer = SelectedLayer;
            var parentId = removeLayer.ParentId;

            foreach (var child in ImageLayers.Where(l => l.ParentId == removeLayer.Id))
                child.ParentId = parentId;

            ImageLayers.Remove(removeLayer);
            SelectedLayer = ImageLayers.LastOrDefault();
        }

        public void BringSelectedLayerToFront()
        {
            if (SelectedLayer == null || ImageLayers.Count <= 1)
                return;

            PushSnapshot();
            var maxZ = ImageLayers.Max(l => l.ZOrder);
            SelectedLayer.ZOrder = maxZ + 1;
            NormalizeZOrders();
            RaiseCommandStates();
        }

        public void SendSelectedLayerToBack()
        {
            if (SelectedLayer == null || ImageLayers.Count <= 1)
                return;

            PushSnapshot();
            var minZ = ImageLayers.Min(l => l.ZOrder);
            SelectedLayer.ZOrder = minZ - 1;
            NormalizeZOrders();
            RaiseCommandStates();
        }

        public void ChangeSelectedLayerZOrder(int delta)
        {
            if (SelectedLayer == null || ImageLayers.Count <= 1)
                return;

            var sorted = ImageLayers.OrderBy(l => l.ZOrder).ToList();
            var index = sorted.IndexOf(SelectedLayer);
            var targetIndex = Math.Clamp(index + delta, 0, sorted.Count - 1);
            if (index == targetIndex)
                return;

            PushSnapshot();
            var other = sorted[targetIndex];
            var temp = SelectedLayer.ZOrder;
            SelectedLayer.ZOrder = other.ZOrder;
            other.ZOrder = temp;
            NormalizeZOrders();
            RaiseCommandStates();
        }

        void NormalizeZOrders()
        {
            var sorted = ImageLayers.OrderBy(l => l.ZOrder).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                sorted[i].ZOrder = i;
            }
        }

        public void ClearAll()
        {
            PushSnapshot();
            ImageLayers.Clear();
            Bones.Clear();
            SelectedLayer = null;
            RaiseCommandStates();
        }

        public void ResetView()
        {
            Zoom = 1.0;
            PanX = 0;
            PanY = 0;
        }

        public void SelectImages()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "パーツ画像の追加",
                Filter = "画像ファイル (*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.svg)|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.svg|すべてのファイル (*.*)|*.*",
                Multiselect = true,
            };

            if (dialog.ShowDialog() == true && dialog.FileNames.Length > 0)
            {
                AddImageFiles(dialog.FileNames);
            }
        }

        public void AddImageFiles(IEnumerable<string> filePaths)
        {
            var valid = filePaths.Where(f => !string.IsNullOrWhiteSpace(f) && File.Exists(f)).ToList();
            if (valid.Count == 0)
                return;

            PushSnapshot();
            int curZ = ImageLayers.Count == 0 ? 0 : ImageLayers.Max(l => l.ZOrder) + 1;

            foreach (var file in valid)
            {
                if (!ImageLayers.Any(l => l.FilePath.Equals(file, StringComparison.OrdinalIgnoreCase)))
                {
                    // パーツ分け立ち絵が本来の位置で綺麗に重なるよう、オフセットなし (0, 0) で配置
                    var layer = new PuppetImageLayerViewModel(file)
                    {
                        X = 0,
                        Y = 0,
                        ZOrder = curZ++,
                    };
                    ImageLayers.Add(layer);
                    SelectedLayer = layer;
                }
            }
        }

        /// <summary>
        /// 画像パーツ＝ボーンの階層構造を YMM4 の BoneItem リストへ変換して出力する。
        /// </summary>
        public ImmutableList<BoneItem> ExportToBoneItems()
        {
            var result = new List<BoneItem>();
            var layerToBoneMap = new Dictionary<string, BoneItem>();

            foreach (var layer in ImageLayers)
            {
                var bone = layer.OriginalBone != null
                    ? new BoneItem(layer.OriginalBone)
                    : new BoneItem(layer.PartName);

                bone.Name = layer.PartName;
                bone.BaseZOrder = layer.ZOrder;
                bone.AnchorX = (float)layer.AnchorX;
                bone.AnchorY = (float)layer.AnchorY;
                bone.IsLipSyncEnabled = layer.IsLipSyncEnabled;
                bone.LipSyncSlotNames = layer.LipSyncSlotNames;
                bone.LipSyncScaleInfluence = layer.LipSyncScaleInfluence;
                bone.IsBlinkEnabled = layer.IsBlinkEnabled;
                bone.BlinkSlotNames = layer.BlinkSlotNames;
                bone.IsIkEnabled = layer.IsIkEnabled;
                bone.IsPhysicsEnabled = layer.IsPhysicsEnabled;
                bone.ImageSlots = [new BoneImageSlot(layer.PartName, layer.FilePath)];

                layerToBoneMap[layer.Id] = bone;
                result.Add(bone);
            }

            // 親子関係と長さ・オフセットの計算
            foreach (var layer in ImageLayers)
            {
                var bone = layerToBoneMap[layer.Id];

                if (!string.IsNullOrEmpty(layer.ParentId) && layerToBoneMap.TryGetValue(layer.ParentId, out var parentBone))
                {
                    bone.ParentId = parentBone.Id;

                    var parentLayer = ImageLayers.FirstOrDefault(l => l.Id == layer.ParentId);
                    if (parentLayer != null)
                    {
                        var dx = layer.JointX - parentLayer.JointX;
                        var dy = layer.JointY - parentLayer.JointY;
                        var dist = Math.Sqrt(dx * dx + dy * dy);

                        if (dist > 5)
                            parentBone.Length = Math.Round(dist, 1);
                    }
                }
                else
                {
                    bone.ParentId = string.Empty;
                    if (bone.X.Values.Count > 0)
                        bone.X.Values[0].Value = Math.Round(layer.JointX, 1);
                    if (bone.Y.Values.Count > 0)
                        bone.Y.Values[0].Value = Math.Round(layer.JointY, 1);
                }
            }

            return result.ToImmutableList();
        }
    }
}
