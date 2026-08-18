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
        PuppetPinViewModel? draggingPin;
        Point pinDragStartOffset;
        PuppetPinViewModel? connectingSourcePin;

        PuppetImageLayerViewModel? draggingLayer;
        Point layerDragStartOffset;
        bool layerMoved;

        public PuppetEditorWindow(PuppetEditorViewModel vm)
        {
            InitializeComponent();
            DataContext = viewModel = vm;

            vm.Pins.CollectionChanged += OnPinsCollectionChanged;
            vm.Bones.CollectionChanged += OnBonesCollectionChanged;
            vm.ImageLayers.CollectionChanged += OnImageLayersCollectionChanged;

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
                    if (viewModel.SelectedPin != null)
                    {
                        viewModel.DeleteSelectedPin();
                        e.Handled = true;
                    }
                    else if (viewModel.SelectedLayer != null)
                    {
                        viewModel.DeleteSelectedLayer();
                        e.Handled = true;
                    }
                }
            };
        }

        void OnPinsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RenderPins();
        void OnBonesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RenderBones();
        void OnImageLayersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RenderImages();

        void RenderAll()
        {
            RenderImages();
            RenderBones();
            RenderPins();
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

                    var selectionBorder = new Rectangle
                    {
                        Stroke = new SolidColorBrush(Color.FromArgb(240, 0, 150, 255)),
                        StrokeThickness = 2.5,
                        StrokeDashArray = new DoubleCollection { 4, 2 },
                        Visibility = layer.IsSelected ? Visibility.Visible : Visibility.Collapsed,
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
                        if (e.PropertyName == nameof(PuppetImageLayerViewModel.IsSelected))
                            selectionBorder.Visibility = layer.IsSelected ? Visibility.Visible : Visibility.Collapsed;
                        else if (e.PropertyName is nameof(PuppetImageLayerViewModel.X) or nameof(PuppetImageLayerViewModel.Y) or nameof(PuppetImageLayerViewModel.ZOrder))
                            UpdateLayerPos();
                    };

                    // 右クリックメニュー（重なり順・削除）
                    var contextMenu = new ContextMenu();
                    var mFront = new MenuItem { Header = "⏫ 最前面へ" };
                    mFront.Click += (_, _) => { viewModel.SelectedLayer = layer; viewModel.BringSelectedLayerToFront(); };
                    var mForward = new MenuItem { Header = "▲ 前面へ" };
                    mForward.Click += (_, _) => { viewModel.SelectedLayer = layer; viewModel.ChangeSelectedLayerZOrder(1); };
                    var mBackward = new MenuItem { Header = "▼ 背面へ" };
                    mBackward.Click += (_, _) => { viewModel.SelectedLayer = layer; viewModel.ChangeSelectedLayerZOrder(-1); };
                    var mBack = new MenuItem { Header = "⏬ 最背面へ" };
                    mBack.Click += (_, _) => { viewModel.SelectedLayer = layer; viewModel.SendSelectedLayerToBack(); };
                    var mDelete = new MenuItem { Header = "🗑 パーツ削除" };
                    mDelete.Click += (_, _) => { viewModel.SelectedLayer = layer; viewModel.DeleteSelectedLayer(); };

                    contextMenu.Items.Add(mFront);
                    contextMenu.Items.Add(mForward);
                    contextMenu.Items.Add(mBackward);
                    contextMenu.Items.Add(mBack);
                    contextMenu.Items.Add(new Separator());
                    contextMenu.Items.Add(mDelete);
                    container.ContextMenu = contextMenu;

                    container.Children.Add(image);
                    container.Children.Add(selectionBorder);
                    UpdateLayerPos();

                    // レイヤーのマウスイベント（選択＆ドラッグ移動）
                    container.MouseDown += (s, e) =>
                    {
                        if (e.ChangedButton == MouseButton.Right)
                        {
                            viewModel.SelectedLayer = layer;
                        }
                        else if (e.ChangedButton == MouseButton.Left)
                        {
                            if (viewModel.IsAddPinMode)
                            {
                                // ピン追加モード時はクリック位置にピンを追加
                                var canvasPos = e.GetPosition(MainCanvas);
                                viewModel.AddPinAt(canvasPos.X, canvasPos.Y);
                                e.Handled = true;
                            }
                            else
                            {
                                viewModel.SelectedLayer = layer;

                                if (viewModel.IsSelectMoveMode)
                                {
                                    draggingLayer = layer;
                                    layerMoved = false;
                                    var canvasPos = e.GetPosition(MainCanvas);
                                    layerDragStartOffset = new Point(layer.X - canvasPos.X, layer.Y - canvasPos.Y);
                                    container.CaptureMouse();
                                }
                                e.Handled = true;
                            }
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
                    Stroke = new SolidColorBrush(Color.FromArgb(220, 33, 150, 243)),
                    StrokeThickness = 4,
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

        void RenderPins()
        {
            PinsCanvas.Children.Clear();
            foreach (var pin in viewModel.Pins)
            {
                var pinElement = CreatePinVisual(pin);
                PinsCanvas.Children.Add(pinElement);
            }
        }

        FrameworkElement CreatePinVisual(PuppetPinViewModel pin)
        {
            var container = new Grid
            {
                Width = 24,
                Height = 24,
                Cursor = Cursors.Hand,
                DataContext = pin,
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
                Background = new SolidColorBrush(Color.FromArgb(180, 20, 20, 20)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 1, 4, 1),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, -20),
                IsHitTestVisible = false,
            };

            var label = new TextBlock
            {
                Text = pin.Name,
                Foreground = Brushes.White,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
            };
            labelBorder.Child = label;

            void UpdateVisualState()
            {
                if (pin.IsSelected)
                {
                    outerCircle.Fill = new SolidColorBrush(Color.FromArgb(230, 255, 102, 0));
                    outerCircle.Stroke = Brushes.White;
                }
                else
                {
                    outerCircle.Fill = new SolidColorBrush(Color.FromArgb(210, 0, 122, 204));
                    outerCircle.Stroke = Brushes.White;
                }
            }

            pin.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(PuppetPinViewModel.IsSelected))
                    UpdateVisualState();
                else if (e.PropertyName == nameof(PuppetPinViewModel.Name))
                    label.Text = pin.Name;
                else if (e.PropertyName is nameof(PuppetPinViewModel.X) or nameof(PuppetPinViewModel.Y))
                {
                    Canvas.SetLeft(container, pin.X - 12);
                    Canvas.SetTop(container, pin.Y - 12);
                }
            };

            UpdateVisualState();
            container.Children.Add(outerCircle);
            container.Children.Add(innerCircle);
            container.Children.Add(labelBorder);

            Canvas.SetLeft(container, pin.X - 12);
            Canvas.SetTop(container, pin.Y - 12);

            bool pinMoved = false;

            // ピンのマウスイベント
            container.MouseDown += (s, e) =>
            {
                if (e.ChangedButton == MouseButton.Left)
                {
                    viewModel.SelectedPin = pin;

                    if (viewModel.IsConnectBoneMode)
                    {
                        // ボーン接続モード：ドラッグ開始
                        connectingSourcePin = pin;
                        ConnectingLine.X1 = pin.X;
                        ConnectingLine.Y1 = pin.Y;
                        ConnectingLine.X2 = pin.X;
                        ConnectingLine.Y2 = pin.Y;
                        ConnectingLine.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        // 移動・選択モードまたはピン追加モード：ピンをドラッグ移動
                        draggingPin = pin;
                        pinMoved = false;
                        var canvasPos = e.GetPosition(MainCanvas);
                        pinDragStartOffset = new Point(pin.X - canvasPos.X, pin.Y - canvasPos.Y);
                    }
                    container.CaptureMouse();
                    e.Handled = true;
                }
            };

            container.MouseMove += (s, e) =>
            {
                if (draggingPin == pin)
                {
                    if (!pinMoved)
                    {
                        viewModel.PushSnapshot();
                        pinMoved = true;
                    }
                    var canvasPos = e.GetPosition(MainCanvas);
                    pin.X = Math.Round(canvasPos.X + pinDragStartOffset.X, 1);
                    pin.Y = Math.Round(canvasPos.Y + pinDragStartOffset.Y, 1);
                    e.Handled = true;
                }
                else if (connectingSourcePin == pin)
                {
                    var canvasPos = e.GetPosition(MainCanvas);
                    ConnectingLine.X2 = canvasPos.X;
                    ConnectingLine.Y2 = canvasPos.Y;
                    e.Handled = true;
                }
            };

            container.MouseUp += (s, e) =>
            {
                if (draggingPin == pin)
                {
                    draggingPin = null;
                    container.ReleaseMouseCapture();
                    e.Handled = true;
                }
                else if (connectingSourcePin != null)
                {
                    ConnectingLine.Visibility = Visibility.Collapsed;
                    var hitPin = FindPinAt(e.GetPosition(MainCanvas));
                    if (hitPin != null && hitPin != connectingSourcePin)
                    {
                        viewModel.ConnectPins(connectingSourcePin, hitPin);
                    }
                    connectingSourcePin = null;
                    container.ReleaseMouseCapture();
                    e.Handled = true;
                }
            };

            return container;
        }

        PuppetPinViewModel? FindPinAt(Point canvasPos)
        {
            foreach (var pin in viewModel.Pins)
            {
                var dx = pin.X - canvasPos.X;
                var dy = pin.Y - canvasPos.Y;
                if (dx * dx + dy * dy <= 22 * 22)
                    return pin;
            }
            return null;
        }

        #region キャンバスマウス操作 (ズーム・パン・ピン打ち)

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
                // クリックされた要素がピンや画像パーツであれば親の処理はスキップ
                var src = e.OriginalSource as DependencyObject;
                if (IsDescendantOf(src, PinsCanvas) || IsDescendantOf(src, ImageLayerCanvas))
                {
                    return;
                }

                var canvasPos = e.GetPosition(MainCanvas);

                if (viewModel.IsAddPinMode)
                {
                    // ピン追加モード：背景クリックでもピンを追加
                    viewModel.AddPinAt(canvasPos.X, canvasPos.Y);
                    e.Handled = true;
                }
                else
                {
                    // 移動・選択モードで背景をクリックした場合は選択解除
                    viewModel.SelectedPin = null;
                    viewModel.SelectedLayer = null;
                }
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
            else if (connectingSourcePin != null)
            {
                var canvasPos = e.GetPosition(MainCanvas);
                ConnectingLine.X2 = canvasPos.X;
                ConnectingLine.Y2 = canvasPos.Y;
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
            if (connectingSourcePin != null && e.ChangedButton == MouseButton.Left)
            {
                ConnectingLine.Visibility = Visibility.Collapsed;
                var canvasPos = e.GetPosition(MainCanvas);
                var hitPin = FindPinAt(canvasPos);
                if (hitPin != null && hitPin != connectingSourcePin)
                {
                    viewModel.ConnectPins(connectingSourcePin, hitPin);
                }
                connectingSourcePin = null;
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

        void BrowsePinImage_Click(object sender, RoutedEventArgs e)
        {
            if (viewModel.SelectedPin == null)
                return;

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "ピンに割り当てる画像の選択",
                Filter = "画像ファイル (*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.svg)|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.svg|すべてのファイル (*.*)|*.*",
            };

            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FileName))
            {
                viewModel.SelectedPin.ImagePath = dialog.FileName;
                if (!viewModel.ImageLayers.Any(l => l.FilePath.Equals(dialog.FileName, StringComparison.OrdinalIgnoreCase)))
                {
                    viewModel.ImageLayers.Add(new PuppetImageLayerViewModel(dialog.FileName)
                    {
                        X = viewModel.SelectedPin.X,
                        Y = viewModel.SelectedPin.Y,
                        ZOrder = viewModel.SelectedPin.ZOrder,
                    });
                }
            }
        }

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
