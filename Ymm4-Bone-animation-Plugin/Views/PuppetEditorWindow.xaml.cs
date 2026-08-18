using System;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Ymm4BoneAnimationPlugin.Views
{
    public partial class PuppetEditorWindow : Window
    {
        public static readonly IValueConverter NotNullConverter = new NotNullToBoolConverter();

        readonly PuppetEditorViewModel viewModel;
        public bool IsApplied { get; private set; }

        Point lastMousePos;
        bool isPanning;

        PuppetImageLayerViewModel? draggingJointLayer;
        Point jointDragStartOffset;

        PuppetImageLayerViewModel? connectingSourceLayer;

        PuppetImageLayerViewModel? draggingLayer;
        Point layerDragStartOffset;
        bool layerMoved;

        public PuppetEditorWindow(PuppetEditorViewModel vm)
        {
            InitializeComponent();
            DataContext = viewModel = vm;

            vm.ImageLayers.CollectionChanged += OnLayersCollectionChanged;
            vm.Bones.CollectionChanged += OnBonesCollectionChanged;

            Loaded += (s, e) =>
            {
                RenderAll();
                // 画面中央に原点を配置
                if (vm.PanX == 0 && vm.PanY == 0)
                {
                    vm.PanX = CanvasContainer.ActualWidth / 2;
                    vm.PanY = CanvasContainer.ActualHeight / 2;
                }
            };

            KeyDown += (s, e) =>
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    if (e.Key == Key.Z)
                    {
                        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                        {
                            if (viewModel.RedoCommand.CanExecute(null))
                                viewModel.RedoCommand.Execute(null);
                        }
                        else
                        {
                            if (viewModel.UndoCommand.CanExecute(null))
                                viewModel.UndoCommand.Execute(null);
                        }
                        e.Handled = true;
                    }
                    else if (e.Key == Key.Y)
                    {
                        if (viewModel.RedoCommand.CanExecute(null))
                            viewModel.RedoCommand.Execute(null);
                        e.Handled = true;
                    }
                }
                else if (e.Key == Key.Delete)
                {
                    if (viewModel.SelectedLayer != null)
                    {
                        viewModel.DeleteSelectedLayer();
                        e.Handled = true;
                    }
                }
            };
        }

        void OnLayersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            RenderImages();
            RenderJointPins();
        }

        void OnBonesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RenderBones();

        void RenderAll()
        {
            RenderImages();
            RenderBones();
            RenderJointPins();
        }

        void RenderImages()
        {
            ImageLayerCanvas.Children.Clear();
            var orderedLayers = viewModel.ImageLayers.OrderBy(l => l.ZOrder).ToList();

            foreach (var layer in orderedLayers)
            {
                if (!File.Exists(layer.FilePath))
                    continue;

                try
                {
                    var bitmap = layer.Thumbnail;
                    if (bitmap == null)
                    {
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.UriSource = new Uri(layer.FilePath, UriKind.Absolute);
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.EndInit();
                        bi.Freeze();
                        bitmap = bi;
                    }

                    layer.Width = bitmap.PixelWidth;
                    layer.Height = bitmap.PixelHeight;

                    var container = new Grid
                    {
                        Width = layer.Width,
                        Height = layer.Height,
                        Cursor = Cursors.Hand,
                        DataContext = layer,
                    };

                    var image = new Image
                    {
                        Source = bitmap,
                        Width = layer.Width,
                        Height = layer.Height,
                        Opacity = 0.95,
                        IsHitTestVisible = false,
                    };

                    void UpdateLayerPos()
                    {
                        Canvas.SetLeft(container, layer.X - layer.Width / 2.0);
                        Canvas.SetTop(container, layer.Y - layer.Height / 2.0);
                        Canvas.SetZIndex(container, layer.ZOrder);
                    }

                    layer.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName is nameof(PuppetImageLayerViewModel.X) or nameof(PuppetImageLayerViewModel.Y) or nameof(PuppetImageLayerViewModel.ZOrder))
                            UpdateLayerPos();
                    };

                    // 右クリックメニュー（重なり順・削除・親子解除）
                    var contextMenu = new ContextMenu();
                    var mFront = new MenuItem { Header = "⏫ 最前面へ" };
                    mFront.Click += (_, _) => { viewModel.SelectedLayer = layer; viewModel.BringSelectedLayerToFront(); };
                    var mForward = new MenuItem { Header = "▲ 前面へ" };
                    mForward.Click += (_, _) => { viewModel.SelectedLayer = layer; viewModel.ChangeSelectedLayerZOrder(1); };
                    var mBackward = new MenuItem { Header = "▼ 背面へ" };
                    mBackward.Click += (_, _) => { viewModel.SelectedLayer = layer; viewModel.ChangeSelectedLayerZOrder(-1); };
                    var mBack = new MenuItem { Header = "⏬ 最背面へ" };
                    mBack.Click += (_, _) => { viewModel.SelectedLayer = layer; viewModel.SendSelectedLayerToBack(); };
                    var mUnlink = new MenuItem { Header = "❌ 親子結合を解除" };
                    mUnlink.Click += (_, _) => { viewModel.SetLayerParent(layer, null); };
                    var mDelete = new MenuItem { Header = "🗑 パーツ削除" };
                    mDelete.Click += (_, _) => { viewModel.SelectedLayer = layer; viewModel.DeleteSelectedLayer(); };

                    contextMenu.Items.Add(mFront);
                    contextMenu.Items.Add(mForward);
                    contextMenu.Items.Add(mBackward);
                    contextMenu.Items.Add(mBack);
                    contextMenu.Items.Add(new Separator());
                    contextMenu.Items.Add(mUnlink);
                    contextMenu.Items.Add(mDelete);
                    container.ContextMenu = contextMenu;

                    container.Children.Add(image);
                    UpdateLayerPos();

                    // 画像パーツのマウスイベント（選択＆ドラッグ移動）
                    container.MouseDown += (s, e) =>
                    {
                        if (e.ChangedButton == MouseButton.Right)
                        {
                            viewModel.SelectedLayer = layer;
                        }
                        else if (e.ChangedButton == MouseButton.Left)
                        {
                            viewModel.SelectedLayer = layer;
                            draggingLayer = layer;
                            layerMoved = false;
                            var canvasPos = e.GetPosition(MainCanvas);
                            layerDragStartOffset = new Point(layer.X - canvasPos.X, layer.Y - canvasPos.Y);
                            container.CaptureMouse();
                            e.Handled = true;
                        }
                    };

                    container.MouseMove += (s, e) =>
                    {
                        if (draggingLayer == layer)
                        {
                            if (!layerMoved)
                            {
                                viewModel.PushSnapshot();
                                layerMoved = true;
                            }
                            var canvasPos = e.GetPosition(MainCanvas);
                            layer.X = Math.Round(canvasPos.X + layerDragStartOffset.X, 1);
                            layer.Y = Math.Round(canvasPos.Y + layerDragStartOffset.Y, 1);
                            e.Handled = true;
                        }
                    };

                    container.MouseUp += (s, e) =>
                    {
                        if (draggingLayer == layer)
                        {
                            draggingLayer = null;
                            container.ReleaseMouseCapture();
                            e.Handled = true;
                        }
                    };

                    ImageLayerCanvas.Children.Add(container);
                }
                catch
                {
                    // 画像読み込み失敗時はスキップ
                }
            }
        }

        void RenderBones()
        {
            BoneLinesCanvas.Children.Clear();
            foreach (var bone in viewModel.Bones)
            {
                var line = new Line
                {
                    Stroke = new SolidColorBrush(Color.FromArgb(230, 33, 150, 243)),
                    StrokeThickness = 4.5,
                    IsHitTestVisible = false,
                };

                var bindingX1 = new Binding(nameof(PuppetBoneViewModel.X1)) { Source = bone };
                var bindingY1 = new Binding(nameof(PuppetBoneViewModel.Y1)) { Source = bone };
                var bindingX2 = new Binding(nameof(PuppetBoneViewModel.X2)) { Source = bone };
                var bindingY2 = new Binding(nameof(PuppetBoneViewModel.Y2)) { Source = bone };

                line.SetBinding(Line.X1Property, bindingX1);
                line.SetBinding(Line.Y1Property, bindingY1);
                line.SetBinding(Line.X2Property, bindingX2);
                line.SetBinding(Line.Y2Property, bindingY2);

                BoneLinesCanvas.Children.Add(line);
            }
        }

        void RenderJointPins()
        {
            JointPinsCanvas.Children.Clear();
            foreach (var layer in viewModel.ImageLayers)
            {
                var pinElement = CreateJointPinVisual(layer);
                JointPinsCanvas.Children.Add(pinElement);
            }
        }

        FrameworkElement CreateJointPinVisual(PuppetImageLayerViewModel layer)
        {
            var container = new Grid
            {
                Width = 26,
                Height = 26,
                Cursor = Cursors.Hand,
                DataContext = layer,
            };

            var outerCircle = new Ellipse
            {
                Width = 22,
                Height = 22,
                StrokeThickness = 2.5,
            };

            var innerCircle = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = Brushes.White,
            };

            var labelBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(190, 20, 20, 20)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 1, 4, 1),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, -20),
                IsHitTestVisible = false,
            };

            var label = new TextBlock
            {
                Text = layer.PartName,
                Foreground = Brushes.White,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
            };
            labelBorder.Child = label;

            void UpdateVisualState()
            {
                if (layer.IsSelected)
                {
                    outerCircle.Fill = new SolidColorBrush(Color.FromArgb(240, 255, 102, 0));
                    outerCircle.Stroke = Brushes.White;
                }
                else
                {
                    outerCircle.Fill = new SolidColorBrush(Color.FromArgb(220, 0, 122, 204));
                    outerCircle.Stroke = Brushes.White;
                }
            }

            void UpdatePosition()
            {
                Canvas.SetLeft(container, layer.JointX - 13);
                Canvas.SetTop(container, layer.JointY - 13);
                Canvas.SetZIndex(container, 1000 + layer.ZOrder);
            }

            layer.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName is nameof(PuppetImageLayerViewModel.IsSelected))
                    UpdateVisualState();
                else if (e.PropertyName is nameof(PuppetImageLayerViewModel.PartName))
                    label.Text = layer.PartName;
                else if (e.PropertyName is nameof(PuppetImageLayerViewModel.JointX) or nameof(PuppetImageLayerViewModel.JointY) or nameof(PuppetImageLayerViewModel.ZOrder))
                    UpdatePosition();
            };

            UpdateVisualState();
            container.Children.Add(outerCircle);
            container.Children.Add(innerCircle);
            container.Children.Add(labelBorder);
            UpdatePosition();

            bool jointMoved = false;

            // 関節ピンのマウスイベント（選択＆関節ドラッグ移動＆Shiftボーン結線）
            container.MouseDown += (s, e) =>
            {
                if (e.ChangedButton == MouseButton.Left)
                {
                    viewModel.SelectedLayer = layer;

                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                    {
                        // Shiftドラッグでボーン結線開始
                        connectingSourceLayer = layer;
                        ConnectingLine.X1 = layer.JointX;
                        ConnectingLine.Y1 = layer.JointY;
                        ConnectingLine.X2 = layer.JointX;
                        ConnectingLine.Y2 = layer.JointY;
                        ConnectingLine.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        // 通常ドラッグで関節位置（アンカー）を移動
                        draggingJointLayer = layer;
                        jointMoved = false;
                        var canvasPos = e.GetPosition(MainCanvas);
                        jointDragStartOffset = new Point(layer.JointX - canvasPos.X, layer.JointY - canvasPos.Y);
                    }
                    container.CaptureMouse();
                    e.Handled = true;
                }
            };

            container.MouseMove += (s, e) =>
            {
                if (draggingJointLayer == layer)
                {
                    if (!jointMoved)
                    {
                        viewModel.PushSnapshot();
                        jointMoved = true;
                    }
                    var canvasPos = e.GetPosition(MainCanvas);
                    var targetX = canvasPos.X + jointDragStartOffset.X;
                    var targetY = canvasPos.Y + jointDragStartOffset.Y;
                    layer.SetJointPos(targetX, targetY);
                    e.Handled = true;
                }
                else if (connectingSourceLayer == layer)
                {
                    var canvasPos = e.GetPosition(MainCanvas);
                    ConnectingLine.X2 = canvasPos.X;
                    ConnectingLine.Y2 = canvasPos.Y;
                    e.Handled = true;
                }
            };

            container.MouseUp += (s, e) =>
            {
                if (draggingJointLayer == layer)
                {
                    draggingJointLayer = null;
                    container.ReleaseMouseCapture();
                    e.Handled = true;
                }
                else if (connectingSourceLayer != null)
                {
                    ConnectingLine.Visibility = Visibility.Collapsed;
                    var canvasPos = e.GetPosition(MainCanvas);
                    var hitLayer = FindLayerByJointAt(canvasPos);
                    if (hitLayer != null && hitLayer != connectingSourceLayer)
                    {
                        viewModel.ConnectLayers(connectingSourceLayer, hitLayer);
                    }
                    connectingSourceLayer = null;
                    container.ReleaseMouseCapture();
                    e.Handled = true;
                }
            };

            return container;
        }

        PuppetImageLayerViewModel? FindLayerByJointAt(Point canvasPos)
        {
            foreach (var layer in viewModel.ImageLayers)
            {
                var dx = layer.JointX - canvasPos.X;
                var dy = layer.JointY - canvasPos.Y;
                if (dx * dx + dy * dy <= 26 * 26)
                    return layer;
            }
            return null;
        }

        #region キャンバスマウス操作 (ズーム・パン・選択解除)

        void Canvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            var oldZoom = viewModel.Zoom;
            var newZoom = e.Delta > 0 ? oldZoom * 1.15 : oldZoom / 1.15;
            newZoom = Math.Clamp(newZoom, 0.1, 10.0);

            var mousePos = e.GetPosition(CanvasContainer);
            var canvasMouseX = (mousePos.X - viewModel.PanX) / oldZoom;
            var canvasMouseY = (mousePos.Y - viewModel.PanY) / oldZoom;

            viewModel.Zoom = newZoom;
            viewModel.PanX = mousePos.X - canvasMouseX * newZoom;
            viewModel.PanY = mousePos.Y - canvasMouseY * newZoom;
            e.Handled = true;
        }

        void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle || e.ChangedButton == MouseButton.Right)
            {
                isPanning = true;
                lastMousePos = e.GetPosition(CanvasContainer);
                CanvasContainer.CaptureMouse();
                e.Handled = true;
            }
            else if (e.ChangedButton == MouseButton.Left)
            {
                // クリック要素がピンや画像ならスキップ
                var src = e.OriginalSource as DependencyObject;
                if (IsDescendantOf(src, JointPinsCanvas) || IsDescendantOf(src, ImageLayerCanvas))
                {
                    return;
                }

                // 背景クリック時は選択解除
                viewModel.SelectedLayer = null;
            }
        }

        static bool IsDescendantOf(DependencyObject? node, DependencyObject? parent)
        {
            while (node != null)
            {
                if (ReferenceEquals(node, parent))
                    return true;
                node = node is Visual or System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(node)
                    : LogicalTreeHelper.GetParent(node);
            }
            return false;
        }

        void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (isPanning)
            {
                var currentPos = e.GetPosition(CanvasContainer);
                var delta = currentPos - lastMousePos;
                viewModel.PanX += delta.X;
                viewModel.PanY += delta.Y;
                lastMousePos = currentPos;
                e.Handled = true;
            }
        }

        void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (isPanning && (e.ChangedButton == MouseButton.Middle || e.ChangedButton == MouseButton.Right))
            {
                isPanning = false;
                CanvasContainer.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        #endregion

        #region ドラッグ＆ドロップ

        void Canvas_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        void Canvas_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                {
                    viewModel.AddImageFiles(files);
                }
                e.Handled = true;
            }
        }

        #endregion

        void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            IsApplied = true;
            DialogResult = true;
            Close();
        }

        void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            IsApplied = false;
            DialogResult = false;
            Close();
        }
    }

    public class NotNullToBoolConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value != null;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
