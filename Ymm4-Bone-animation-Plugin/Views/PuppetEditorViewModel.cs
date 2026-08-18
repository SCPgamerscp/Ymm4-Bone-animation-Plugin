using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using YukkuriMovieMaker.Commons;
using Ymm4BoneAnimationPlugin.Shape;

namespace Ymm4BoneAnimationPlugin.Views
{
    public enum PuppetToolMode
    {
        [Description("移動・選択")]
        SelectMove,

        [Description("ピン追加")]
        AddPin,

        [Description("ボーン接続")]
        ConnectBone,
    }

    /// <summary>
    /// パペットエディタ上のピン（関節）。
    /// </summary>
    public class PuppetPinViewModel : Bindable
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N");

        public string Name { get => name; set => Set(ref name, value); }
        string name = "ピン";

        public double X { get => x; set => Set(ref x, value); }
        double x;

        public double Y { get => y; set => Set(ref y, value); }
        double y;

        public string? ParentPinId { get => parentPinId; set => Set(ref parentPinId, value); }
        string? parentPinId;

        public string? ImagePath { get => imagePath; set => Set(ref imagePath, value); }
        string? imagePath;

        public double AnchorX { get => anchorX; set => Set(ref anchorX, value); }
        double anchorX = 0.5;

        public double AnchorY { get => anchorY; set => Set(ref anchorY, value); }
        double anchorY = 0.5;

        public int ZOrder { get => zOrder; set => Set(ref zOrder, value); }
        int zOrder;

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

        public bool IsSelected { get => isSelected; set => Set(ref isSelected, value); }
        bool isSelected;

        /// <summary>元のBoneItem（既存ボーンのプロパティを保持するため）</summary>
        public BoneItem? OriginalBone { get; set; }
    }

    /// <summary>
    /// パペットエディタ上のボーン接続線（親ピン→子ピン）。
    /// </summary>
    public class PuppetBoneViewModel : Bindable
    {
        public PuppetPinViewModel ParentPin { get; }
        public PuppetPinViewModel ChildPin { get; }

        public double X1 => ParentPin.X;
        public double Y1 => ParentPin.Y;
        public double X2 => ChildPin.X;
        public double Y2 => ChildPin.Y;

        public PuppetBoneViewModel(PuppetPinViewModel parent, PuppetPinViewModel child)
        {
            ParentPin = parent;
            ChildPin = child;
            parent.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName is nameof(PuppetPinViewModel.X) or nameof(PuppetPinViewModel.Y))
                {
                    OnPropertyChanged(nameof(X1));
                    OnPropertyChanged(nameof(Y1));
                }
            };
            child.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName is nameof(PuppetPinViewModel.X) or nameof(PuppetPinViewModel.Y))
                {
                    OnPropertyChanged(nameof(X2));
                    OnPropertyChanged(nameof(Y2));
                }
            };
        }
    }

    /// <summary>
    /// キャンバス上に配置された画像パーツ（レイヤー）。
    /// </summary>
    public class PuppetImageLayerViewModel : Bindable
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public string FilePath { get; init; }
        public string FileName => Path.GetFileName(FilePath);

        public double Width { get => width; set => Set(ref width, value); }
        double width = 200;

        public double Height { get => height; set => Set(ref height, value); }
        double height = 200;

        public double X { get => x; set => Set(ref x, value); }
        double x = 0;

        public double Y { get => y; set => Set(ref y, value); }
        double y = 0;

        public int ZOrder { get => zOrder; set => Set(ref zOrder, value); }
        int zOrder;

        public bool IsSelected { get => isSelected; set => Set(ref isSelected, value); }
        bool isSelected;

        public PuppetImageLayerViewModel(string filePath)
        {
            FilePath = filePath;
        }
    }

    /// <summary>
    /// Undo / Redo 用のスナップショットデータ
    /// </summary>
    internal record struct PinSnapshot(
        string Id,
        string Name,
        double X,
        double Y,
        string? ParentPinId,
        string? ImagePath,
        double AnchorX,
        double AnchorY,
        int ZOrder,
        bool IsLipSyncEnabled,
        string LipSyncSlotNames,
        double LipSyncScaleInfluence,
        bool IsBlinkEnabled,
        string BlinkSlotNames,
        bool IsIkEnabled,
        bool IsPhysicsEnabled,
        BoneItem? OriginalBone);

    internal record struct LayerSnapshot(string Id, string FilePath, double X, double Y, double Width, double Height, int ZOrder);
    internal record struct EditorSnapshot(ImmutableList<PinSnapshot> Pins, ImmutableList<LayerSnapshot> Layers, string? SelectedPinId, string? SelectedLayerId);

    /// <summary>
    /// パペット変形方式のボーンエディタViewModel。
    /// Undo/Redo、パーツ画像直接選択・配置、ピン・ボーン接続、口パク・目パチ設定を完全サポート。
    /// </summary>
    public class PuppetEditorViewModel : Bindable
    {
        public ObservableCollection<PuppetPinViewModel> Pins { get; } = new();
        public ObservableCollection<PuppetBoneViewModel> Bones { get; } = new();
        public ObservableCollection<PuppetImageLayerViewModel> ImageLayers { get; } = new();

        readonly Stack<EditorSnapshot> undoStack = new();
        readonly Stack<EditorSnapshot> redoStack = new();

        public PuppetToolMode CurrentTool
        {
            get => currentTool;
            set
            {
                if (Set(ref currentTool, value))
                {
                    OnPropertyChanged(nameof(IsSelectMoveMode));
                    OnPropertyChanged(nameof(IsAddPinMode));
                    OnPropertyChanged(nameof(IsConnectBoneMode));
                }
            }
        }
        PuppetToolMode currentTool = PuppetToolMode.AddPin;

        public bool IsSelectMoveMode { get => CurrentTool == PuppetToolMode.SelectMove; set { if (value) CurrentTool = PuppetToolMode.SelectMove; } }
        public bool IsAddPinMode { get => CurrentTool == PuppetToolMode.AddPin; set { if (value) CurrentTool = PuppetToolMode.AddPin; } }
        public bool IsConnectBoneMode { get => CurrentTool == PuppetToolMode.ConnectBone; set { if (value) CurrentTool = PuppetToolMode.ConnectBone; } }

        public PuppetPinViewModel? SelectedPin
        {
            get => selectedPin;
            set
            {
                if (selectedPin != null)
                    selectedPin.IsSelected = false;
                if (Set(ref selectedPin, value) && selectedPin != null)
                {
                    selectedPin.IsSelected = true;
                    // ピン選択時は画像選択を排他解除
                    SelectedLayer = null;
                }
                RaiseCommandStates();
            }
        }
        PuppetPinViewModel? selectedPin;

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
                    // レイヤー選択時はピン選択を排他解除
                    if (SelectedPin != null)
                    {
                        SelectedPin.IsSelected = false;
                        selectedPin = null;
                        OnPropertyChanged(nameof(SelectedPin));
                    }
                }
                RaiseCommandStates();
            }
        }
        PuppetImageLayerViewModel? selectedLayer;

        public double Zoom { get => zoom; set => Set(ref zoom, Math.Clamp(value, 0.1, 10.0)); }
        double zoom = 1.0;

        public double PanX { get => panX; set => Set(ref panX, value); }
        double panX = 0;

        public double PanY { get => panY; set => Set(ref panY, value); }
        double panY = 0;

        public ActionCommand AddPinCommand { get; }
        public ActionCommand ConnectBoneCommand { get; }
        public ActionCommand SelectMoveCommand { get; }
        public ActionCommand DeleteSelectedCommand { get; }
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

        public PuppetEditorViewModel(ImmutableList<BoneItem> existingBones)
        {
            AddPinCommand = new ActionCommand(_ => true, _ => CurrentTool = PuppetToolMode.AddPin);
            ConnectBoneCommand = new ActionCommand(_ => true, _ => CurrentTool = PuppetToolMode.ConnectBone);
            SelectMoveCommand = new ActionCommand(_ => true, _ => CurrentTool = PuppetToolMode.SelectMove);
            DeleteSelectedCommand = new ActionCommand(_ => SelectedPin != null, _ => DeleteSelectedPin());
            ClearAllCommand = new ActionCommand(_ => Pins.Count > 0 || ImageLayers.Count > 0, _ => ClearAll());
            ResetViewCommand = new ActionCommand(_ => true, _ => ResetView());
            AddImagesCommand = new ActionCommand(_ => true, _ => SelectImages());
            UndoCommand = new ActionCommand(_ => undoStack.Count > 0, _ => Undo());
            RedoCommand = new ActionCommand(_ => redoStack.Count > 0, _ => Redo());
            BringLayerToFrontCommand = new ActionCommand(_ => SelectedLayer != null, _ => BringSelectedLayerToFront());
            BringLayerForwardCommand = new ActionCommand(_ => SelectedLayer != null, _ => ChangeSelectedLayerZOrder(1));
            SendLayerBackwardCommand = new ActionCommand(_ => SelectedLayer != null, _ => ChangeSelectedLayerZOrder(-1));
            SendLayerToBackCommand = new ActionCommand(_ => SelectedLayer != null, _ => SendSelectedLayerToBack());
            DeleteLayerCommand = new ActionCommand(_ => SelectedLayer != null, _ => DeleteSelectedLayer());

            ImportExistingBones(existingBones);
        }

        void RaiseCommandStates()
        {
            DeleteSelectedCommand.RaiseCanExecuteChanged();
            ClearAllCommand.RaiseCanExecuteChanged();
            UndoCommand.RaiseCanExecuteChanged();
            RedoCommand.RaiseCanExecuteChanged();
            BringLayerToFrontCommand.RaiseCanExecuteChanged();
            BringLayerForwardCommand.RaiseCanExecuteChanged();
            SendLayerBackwardCommand.RaiseCanExecuteChanged();
            SendLayerToBackCommand.RaiseCanExecuteChanged();
            DeleteLayerCommand.RaiseCanExecuteChanged();
        }

        #region Undo / Redo

        public void PushSnapshot()
        {
            var pinList = Pins.Select(p => new PinSnapshot(
                p.Id,
                p.Name,
                p.X,
                p.Y,
                p.ParentPinId,
                p.ImagePath,
                p.AnchorX,
                p.AnchorY,
                p.ZOrder,
                p.IsLipSyncEnabled,
                p.LipSyncSlotNames,
                p.LipSyncScaleInfluence,
                p.IsBlinkEnabled,
                p.BlinkSlotNames,
                p.IsIkEnabled,
                p.IsPhysicsEnabled,
                p.OriginalBone)).ToImmutableList();

            var layerList = ImageLayers.Select(l => new LayerSnapshot(l.Id, l.FilePath, l.X, l.Y, l.Width, l.Height, l.ZOrder)).ToImmutableList();
            undoStack.Push(new EditorSnapshot(pinList, layerList, SelectedPin?.Id, SelectedLayer?.Id));
            redoStack.Clear();
            RaiseCommandStates();
        }

        public void Undo()
        {
            if (undoStack.Count == 0)
                return;

            var pinList = Pins.Select(p => new PinSnapshot(
                p.Id,
                p.Name,
                p.X,
                p.Y,
                p.ParentPinId,
                p.ImagePath,
                p.AnchorX,
                p.AnchorY,
                p.ZOrder,
                p.IsLipSyncEnabled,
                p.LipSyncSlotNames,
                p.LipSyncScaleInfluence,
                p.IsBlinkEnabled,
                p.BlinkSlotNames,
                p.IsIkEnabled,
                p.IsPhysicsEnabled,
                p.OriginalBone)).ToImmutableList();

            var layerList = ImageLayers.Select(l => new LayerSnapshot(l.Id, l.FilePath, l.X, l.Y, l.Width, l.Height, l.ZOrder)).ToImmutableList();
            redoStack.Push(new EditorSnapshot(pinList, layerList, SelectedPin?.Id, SelectedLayer?.Id));

            var snapshot = undoStack.Pop();
            RestoreSnapshot(snapshot);
            RaiseCommandStates();
        }

        public void Redo()
        {
            if (redoStack.Count == 0)
                return;

            var pinList = Pins.Select(p => new PinSnapshot(
                p.Id,
                p.Name,
                p.X,
                p.Y,
                p.ParentPinId,
                p.ImagePath,
                p.AnchorX,
                p.AnchorY,
                p.ZOrder,
                p.IsLipSyncEnabled,
                p.LipSyncSlotNames,
                p.LipSyncScaleInfluence,
                p.IsBlinkEnabled,
                p.BlinkSlotNames,
                p.IsIkEnabled,
                p.IsPhysicsEnabled,
                p.OriginalBone)).ToImmutableList();

            var layerList = ImageLayers.Select(l => new LayerSnapshot(l.Id, l.FilePath, l.X, l.Y, l.Width, l.Height, l.ZOrder)).ToImmutableList();
            undoStack.Push(new EditorSnapshot(pinList, layerList, SelectedPin?.Id, SelectedLayer?.Id));

            var snapshot = redoStack.Pop();
            RestoreSnapshot(snapshot);
            RaiseCommandStates();
        }

        void RestoreSnapshot(EditorSnapshot snapshot)
        {
            Pins.Clear();
            foreach (var p in snapshot.Pins)
            {
                Pins.Add(new PuppetPinViewModel
                {
                    Id = p.Id,
                    Name = p.Name,
                    X = p.X,
                    Y = p.Y,
                    ParentPinId = p.ParentPinId,
                    ImagePath = p.ImagePath,
                    AnchorX = p.AnchorX,
                    AnchorY = p.AnchorY,
                    ZOrder = p.ZOrder,
                    IsLipSyncEnabled = p.IsLipSyncEnabled,
                    LipSyncSlotNames = p.LipSyncSlotNames,
                    LipSyncScaleInfluence = p.LipSyncScaleInfluence,
                    IsBlinkEnabled = p.IsBlinkEnabled,
                    BlinkSlotNames = p.BlinkSlotNames,
                    IsIkEnabled = p.IsIkEnabled,
                    IsPhysicsEnabled = p.IsPhysicsEnabled,
                    OriginalBone = p.OriginalBone,
                });
            }

            ImageLayers.Clear();
            foreach (var l in snapshot.Layers)
            {
                ImageLayers.Add(new PuppetImageLayerViewModel(l.FilePath)
                {
                    Id = l.Id,
                    X = l.X,
                    Y = l.Y,
                    Width = l.Width,
                    Height = l.Height,
                    ZOrder = l.ZOrder,
                });
            }

            RebuildBoneConnections();
            SelectedPin = Pins.FirstOrDefault(p => p.Id == snapshot.SelectedPinId);
            SelectedLayer = ImageLayers.FirstOrDefault(l => l.Id == snapshot.SelectedLayerId);
        }

        #endregion

        void ImportExistingBones(ImmutableList<BoneItem> existingBones)
        {
            if (existingBones == null || existingBones.Count == 0)
                return;

            var pinMap = new Dictionary<string, PuppetPinViewModel>();
            double curX = 0;
            double curY = 0;

            foreach (var bone in existingBones)
            {
                var pin = new PuppetPinViewModel
                {
                    Id = bone.Id,
                    Name = bone.Name,
                    ParentPinId = bone.ParentId,
                    ImagePath = bone.ImageSlots.FirstOrDefault(s => !string.IsNullOrEmpty(s.FilePath))?.FilePath,
                    AnchorX = bone.AnchorX,
                    AnchorY = bone.AnchorY,
                    ZOrder = bone.BaseZOrder,
                    IsLipSyncEnabled = bone.IsLipSyncEnabled,
                    LipSyncSlotNames = bone.LipSyncSlotNames,
                    LipSyncScaleInfluence = bone.LipSyncScaleInfluence,
                    IsBlinkEnabled = bone.IsBlinkEnabled,
                    BlinkSlotNames = bone.BlinkSlotNames,
                    IsIkEnabled = bone.IsIkEnabled,
                    IsPhysicsEnabled = bone.IsPhysicsEnabled,
                    X = curX,
                    Y = curY,
                    OriginalBone = bone,
                };
                pinMap[bone.Id] = pin;
                Pins.Add(pin);

                if (!string.IsNullOrEmpty(pin.ImagePath) && File.Exists(pin.ImagePath))
                {
                    if (!ImageLayers.Any(l => l.FilePath.Equals(pin.ImagePath, StringComparison.OrdinalIgnoreCase)))
                        ImageLayers.Add(new PuppetImageLayerViewModel(pin.ImagePath) { ZOrder = bone.BaseZOrder });
                }

                curY += Math.Max(40, bone.Length);
            }

            RebuildBoneConnections();
            if (Pins.Count > 0)
                SelectedPin = Pins[0];
        }

        public void AddPinAt(double x, double y)
        {
            PushSnapshot();

            var name = $"ピン{Pins.Count + 1}";
            var newPin = new PuppetPinViewModel
            {
                Name = name,
                X = Math.Round(x, 1),
                Y = Math.Round(y, 1),
                ZOrder = Pins.Count,
            };

            // クリック位置にある画像パーツを探して割り当て＆アンカー自動計算
            var hitLayer = FindLayerAt(x, y) ?? ImageLayers.LastOrDefault();
            if (hitLayer != null)
            {
                newPin.ImagePath = hitLayer.FilePath;
                newPin.ZOrder = hitLayer.ZOrder;

                var layerLeft = hitLayer.X - hitLayer.Width / 2.0;
                var layerTop = hitLayer.Y - hitLayer.Height / 2.0;
                if (hitLayer.Width > 0 && hitLayer.Height > 0)
                {
                    newPin.AnchorX = Math.Clamp(Math.Round((x - layerLeft) / hitLayer.Width, 3), 0.0, 1.0);
                    newPin.AnchorY = Math.Clamp(Math.Round((y - layerTop) / hitLayer.Height, 3), 0.0, 1.0);
                }
            }

            // 直前に選択されていたピンがあれば、自動的にその子としてボーンを繋ぐ
            if (SelectedPin != null)
            {
                newPin.ParentPinId = SelectedPin.Id;
            }

            Pins.Add(newPin);
            RebuildBoneConnections();
            SelectedPin = newPin;
        }

        public PuppetImageLayerViewModel? FindLayerAt(double canvasX, double canvasY)
        {
            return ImageLayers
                .OrderByDescending(l => l.ZOrder)
                .FirstOrDefault(l =>
                {
                    var left = l.X - l.Width / 2.0;
                    var top = l.Y - l.Height / 2.0;
                    return canvasX >= left && canvasX <= left + l.Width &&
                           canvasY >= top && canvasY <= top + l.Height;
                });
        }

        public void ConnectPins(PuppetPinViewModel parentPin, PuppetPinViewModel childPin)
        {
            if (parentPin == null || childPin == null || ReferenceEquals(parentPin, childPin))
                return;

            var current = parentPin;
            while (current != null)
            {
                if (current.Id == childPin.Id)
                    return;
                current = Pins.FirstOrDefault(p => p.Id == current.ParentPinId);
            }

            PushSnapshot();
            childPin.ParentPinId = parentPin.Id;
            RebuildBoneConnections();
            SelectedPin = childPin;
        }

        public void RebuildBoneConnections()
        {
            Bones.Clear();
            foreach (var pin in Pins)
            {
                if (!string.IsNullOrEmpty(pin.ParentPinId))
                {
                    var parent = Pins.FirstOrDefault(p => p.Id == pin.ParentPinId);
                    if (parent != null)
                        Bones.Add(new PuppetBoneViewModel(parent, pin));
                }
            }
        }

        public void DeleteSelectedPin()
        {
            if (SelectedPin == null)
                return;

            PushSnapshot();
            var removePin = SelectedPin;
            var parentId = removePin.ParentPinId;

            foreach (var child in Pins.Where(p => p.ParentPinId == removePin.Id))
                child.ParentPinId = parentId;

            Pins.Remove(removePin);
            RebuildBoneConnections();
            SelectedPin = Pins.LastOrDefault();
        }

        public void DeleteSelectedLayer()
        {
            if (SelectedLayer == null)
                return;

            PushSnapshot();
            var removeLayer = SelectedLayer;
            ImageLayers.Remove(removeLayer);
            SelectedLayer = null;
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
            Pins.Clear();
            Bones.Clear();
            ImageLayers.Clear();
            SelectedPin = null;
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
                    var layer = new PuppetImageLayerViewModel(file) { ZOrder = curZ++ };
                    ImageLayers.Add(layer);
                    SelectedLayer = layer;
                }
            }
        }

        /// <summary>
        /// パペットエディタのピン・ボーン構成（およびピンのない固定パーツ画像）を
        /// YMM4 の BoneItem リストへ変換して出力する。
        /// </summary>
        public ImmutableList<BoneItem> ExportToBoneItems()
        {
            var result = new List<BoneItem>();
            var pinToBoneMap = new Dictionary<string, BoneItem>();
            var usedImagePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. ピンが打たれたボーンを生成
            foreach (var pin in Pins)
            {
                var bone = pin.OriginalBone != null
                    ? new BoneItem(pin.OriginalBone)
                    : new BoneItem(pin.Name);

                bone.Name = pin.Name;
                bone.BaseZOrder = pin.ZOrder;
                bone.AnchorX = (float)pin.AnchorX;
                bone.AnchorY = (float)pin.AnchorY;
                bone.IsLipSyncEnabled = pin.IsLipSyncEnabled;
                bone.LipSyncSlotNames = pin.LipSyncSlotNames;
                bone.LipSyncScaleInfluence = pin.LipSyncScaleInfluence;
                bone.IsBlinkEnabled = pin.IsBlinkEnabled;
                bone.BlinkSlotNames = pin.BlinkSlotNames;
                bone.IsIkEnabled = pin.IsIkEnabled;
                bone.IsPhysicsEnabled = pin.IsPhysicsEnabled;

                // 画像が割り当てられている場合
                if (!string.IsNullOrEmpty(pin.ImagePath))
                {
                    var fileName = Path.GetFileNameWithoutExtension(pin.ImagePath);
                    bone.ImageSlots = [new BoneImageSlot(fileName, pin.ImagePath)];
                    usedImagePaths.Add(pin.ImagePath);
                }

                pinToBoneMap[pin.Id] = bone;
                result.Add(bone);
            }

            // 親子関係および長さ・相対オフセットの計算
            foreach (var pin in Pins)
            {
                var bone = pinToBoneMap[pin.Id];

                if (!string.IsNullOrEmpty(pin.ParentPinId) && pinToBoneMap.TryGetValue(pin.ParentPinId, out var parentBone))
                {
                    bone.ParentId = parentBone.Id;

                    var parentPin = Pins.FirstOrDefault(p => p.Id == pin.ParentPinId);
                    if (parentPin != null)
                    {
                        var dx = pin.X - parentPin.X;
                        var dy = pin.Y - parentPin.Y;
                        var dist = Math.Sqrt(dx * dx + dy * dy);

                        if (dist > 5)
                            parentBone.Length = Math.Round(dist, 1);
                    }
                }
                else
                {
                    bone.ParentId = string.Empty;
                    // ルートボーンの初期配置座標
                    if (bone.X.Values.Count > 0)
                        bone.X.Values[0].Value = Math.Round(pin.X, 1);
                    if (bone.Y.Values.Count > 0)
                        bone.Y.Values[0].Value = Math.Round(pin.Y, 1);
                }
            }

            // 2. ピンが打たれていない画像パーツも「固定パーツ」としてボーン一覧に出力
            foreach (var layer in ImageLayers)
            {
                if (usedImagePaths.Contains(layer.FilePath))
                    continue;

                var fileName = Path.GetFileNameWithoutExtension(layer.FilePath);
                var fixedBone = new BoneItem(fileName)
                {
                    BaseZOrder = layer.ZOrder,
                    AnchorX = 0.5f,
                    AnchorY = 0.5f,
                    ImageSlots = [new BoneImageSlot(fileName, layer.FilePath)],
                };

                if (fixedBone.X.Values.Count > 0)
                    fixedBone.X.Values[0].Value = Math.Round(layer.X, 1);
                if (fixedBone.Y.Values.Count > 0)
                    fixedBone.Y.Values[0].Value = Math.Round(layer.Y, 1);

                result.Add(fixedBone);
            }

            return result.ToImmutableList();
        }
    }
}
