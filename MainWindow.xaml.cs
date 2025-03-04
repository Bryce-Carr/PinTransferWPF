using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Integration;
using CommunityToolkit.Mvvm.ComponentModel;
using ViewModels;
using PinTransferParameters;
using System.Collections.Specialized;
using System.Linq;
using System.Windows.Media.Media3D;

namespace PinTransferWPF
{
    using StackType = HotelStacker;
    [INotifyPropertyChanged]
    public partial class MainWindow : Window
    {
        public MainViewModel ViewModel { get; }
        MessageBoxResult messageBoxResult;
        private InstrumentController _instrumentController;
        private DispatcherTimer resizeTimer;
        private Dictionary<(int, int), Grid> Shelves = new Dictionary<(int, int), Grid>();
        private SolidColorBrush selectedPlateColor = new SolidColorBrush();
        private SolidColorBrush unselectedPlateColor = new SolidColorBrush();
        private SolidColorBrush primaryColor = new SolidColorBrush();
        private SolidColorBrush secondaryColor = new SolidColorBrush();
        private SolidColorBrush accentColor = new SolidColorBrush();
        private SolidColorBrush backgroundColor = new SolidColorBrush();
        private SolidColorBrush secondaryBackgroundColor = new SolidColorBrush();
        private SolidColorBrush foregroundColor = new SolidColorBrush();
        int Rows = 26; //shelves + 1 for labels
        int Columns = 6;
        int Offset = 3;

        public MainWindow()
        {
            //selectedPlate.Color = (Color)ColorConverter.ConvertFromString("#F37AA5");
            //unselectedPlate.Color = (Color)ColorConverter.ConvertFromString("#C9C1C5");
            InitializeComponent();
            primaryColor.Color = (Color)FindResource("PrimaryColor");
            secondaryColor.Color = (Color)FindResource("SecondaryColor");
            accentColor.Color = (Color)FindResource("AccentColor");
            backgroundColor.Color = (Color)FindResource("BackgroundColor");
            secondaryBackgroundColor.Color = (Color)FindResource("SecondaryBackgroundColor");
            foregroundColor.Color = (Color)FindResource("ForegroundColor");

            selectedPlateColor.Color = (Color)FindResource("PrimaryColor");
            unselectedPlateColor.Color = (Color)FindResource("SecondaryColor");
            _instrumentController = new InstrumentController(this);
            if (Parameters.UsingInstruments)
            {
                _instrumentController.InitializeAllDevices();

                this.Closing += new CancelEventHandler(MainWindow_Closing);

                void MainWindow_Closing(object sender, CancelEventArgs e)
                {
                    _instrumentController.OnShutdown();
                }
            }

            string connectionString = "Data Source=" + Parameters.LoggingDatabase;
            ViewModel = new MainViewModel(_instrumentController,
                                          connectionString);
            DataContext = ViewModel;

            // Subscribe to the CloseWindowRequested event
            ViewModel.CloseWindowRequested += (sender, args) => this.Close();

            // Subscribe to the MaximizeWindowRequested event
            ViewModel.MaximizeWindowRequested += (sender, args) => MaximizeWindow(ViewModel);

            // Subscribe to the MinimizeWindowRequested event
            ViewModel.MinimizeWindowRequested += (sender, args) => this.WindowState = WindowState.Minimized;

            // Subscribe to the OpenLabwareRequested event
            ViewModel.OpenLabwareRequested += (sender, args) => OpenLabware();

            // Subscribe to the WindowDragRequested event
            WindowDragRequested += (sender, args) => this.DragMove();

            // Subscribe to the PlateSelected event
            PlateSelected += SelectPlate;

            // Bind the Window's StateChanged event to update the ViewModel
            StateChanged += (sender, args) => ViewModel.WindowState = WindowState;

            //Subscribe to plates changing
            ViewModel.SourcePlates.CollectionChanged += OnPlatesChanged;
            ViewModel.DestinationPlates.CollectionChanged += OnPlatesChanged;

            // Define grid rows and columns
            for (int i = 0; i < 3; i++)
            {
                StackerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                StackerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            CreateMicroplateStacker();
            SizeChanged += MainWindow_SizeChanged;

            this.Loaded += MainWindow_Loaded;
            SizeChanged += MainWindow_SizeChanged;

            // Initialize the resize timer
            resizeTimer = new DispatcherTimer();
            resizeTimer.Interval = TimeSpan.FromMilliseconds(250);
            resizeTimer.Tick += ResizeTimer_Tick;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Delay the initial creation slightly to ensure the control has been rendered
            resizeTimer.Start();
        }
        private void OpenLabware()
        {
            LabwareDefinitionsWindow labwareDefinitionsWindow = new LabwareDefinitionsWindow("Data Source=" + Parameters.LabwareDatabase);
            labwareDefinitionsWindow.ShowDialog();
        }

        private void CreateMicroplateStacker()
        {
            // Store existing plates before clearing
            var existingPlates = new List<(Path plate, int row, int column)>();
            foreach (var kvp in Shelves)
            {
                var grid = kvp.Value;
                var plates = grid.Children.OfType<Path>().Where(p => p.Tag is Plate).ToList();
                foreach (var plate in plates)
                {
                    existingPlates.Add((plate, kvp.Key.Item1, kvp.Key.Item2));
                    grid.Children.Remove(plate);
                }
            }
            VirtualizingStackPanel.SetIsVirtualizing(StackerGrid, true);
            VirtualizingStackPanel.SetVirtualizationMode(StackerGrid, VirtualizationMode.Recycling);

            double aspectRatio = 4.0 / 1; // Width to height ratio for each stacker
            double horizontalMargin = 15;
            double verticalMargin = 5;

            StackerGrid.Children.Clear();
            StackerGrid.RowDefinitions.Clear();
            StackerGrid.ColumnDefinitions.Clear();
            Shelves.Clear();

            for (int i = Rows - 1; i >= 0; i--)
            {
                StackerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            }

            for (int j = 0; j < Columns; j++)
            {
                StackerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            double totalWidth = StackerGridViewbox.ActualWidth;
            double totalHeight = StackerGridViewbox.ActualHeight;

            // Calculate the maximum possible size for each stacker
            double maxStackerWidth = (totalWidth - (Columns + 1) * horizontalMargin) / Columns;
            double maxStackerHeight = (totalHeight - (Rows + 1) * verticalMargin) / Rows;

            // Determine the actual size while maintaining the aspect ratio
            double stackerWidth, stackerHeight;
            if (maxStackerWidth / maxStackerHeight > aspectRatio)
            {
                // Height is the limiting factor
                stackerHeight = maxStackerHeight;
                stackerWidth = stackerHeight * aspectRatio;
            }
            else
            {
                // Width is the limiting factor
                stackerWidth = maxStackerWidth;
                stackerHeight = stackerWidth / aspectRatio;
            }

            // Ensure minimum size
            stackerWidth = Math.Max(4, stackerWidth);
            stackerHeight = Math.Max(2, stackerHeight);

            for (int i = 0; i < Rows - 1; i++)
            {
                for (int j = 0; j < Columns; j++)
                {
                    Path shelf = CreateShelf(stackerWidth, stackerHeight);
                    Grid containerGrid = new Grid();
                    containerGrid.Children.Add(shelf);
                    containerGrid.Margin = new Thickness(horizontalMargin / 2, verticalMargin / 2,
                                                       horizontalMargin / 2, verticalMargin / 2);

                    // Add SizeChanged handler
                    containerGrid.SizeChanged += (s, e) =>
                    {
                        var plates = containerGrid.Children.OfType<Path>()
                            .Where(p => p.Tag is Plate).ToList();
                        foreach (var plate in plates)
                        {
                            UpdatePlateSize(containerGrid, plate);
                        }
                    };

                    Grid.SetRow(containerGrid, Rows - 2 - i);
                    Grid.SetColumn(containerGrid, j);
                    StackerGrid.Children.Add(containerGrid);

                    Shelves[(i, j)] = containerGrid;
                }
            }
            // Add column numbers
            for (int j = 0; j < Columns; j++)
            {
                TextBlock columnNumber = new TextBlock
                {
                    Text = (ColumnShift(j + 1)).ToString(),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = FontWeights.Bold
                };

                Grid.SetRow(columnNumber, Rows - 1); // Place in the last row
                Grid.SetColumn(columnNumber, j);
                StackerGrid.Children.Add(columnNumber);
            }

            // Restore plates after recreating shelves
            foreach (var (plate, row, column) in existingPlates)
            {
                if (Shelves.TryGetValue((row, column), out Grid containerGrid))
                {
                    UpdatePlateSize(containerGrid, plate);
                    containerGrid.Children.Add(plate);
                }
            }
        }

        private Path CreateShelf(double width, double height)
        {
            double cornerRadius = Math.Min(width, height) * 0.15;

            var geometry = new RectangleGeometry(
                new Rect(0, 0, width, height),
                cornerRadius,
                cornerRadius
            );

            return new Path
            {
                Data = geometry,
                Fill = backgroundColor,
                Stroke = Brushes.Transparent,
                StrokeThickness = 1
            };


            // For "U" shaped shelf
            //double thickness = Math.Min(width, height) * 0.1; // Adjust thickness as needed

            //var pathFigure = new PathFigure
            //{
            //    StartPoint = new Point(0, 0),
            //    Segments = new PathSegmentCollection
            //    {
            //        new LineSegment(new Point(0, height), true),
            //        new LineSegment(new Point(width, height), true),
            //        new LineSegment(new Point(width, 0), true),
            //        new LineSegment(new Point(width - thickness, 0), true),
            //        new LineSegment(new Point(width - thickness, height - thickness), true),
            //        new LineSegment(new Point(thickness, height - thickness), true),
            //        new LineSegment(new Point(thickness, 0), true),
            //        new LineSegment(new Point(0, 0), true)
            //    }
            //};

            //var pathGeometry = new PathGeometry();
            //pathGeometry.Figures.Add(pathFigure);

            //return new Path
            //{
            //    Data = pathGeometry,
            //    Fill = Brushes.Black,
            //    Stroke = Brushes.Transparent,
            //    StrokeThickness = 1
            //};
        }

        public void AddPlateToShelf(int row, int column, Brush fillColor, Plate plate)
        {
            if (Shelves.TryGetValue((row, column), out Grid containerGrid))
            {
                Path shelf = containerGrid.Children.OfType<Path>().FirstOrDefault();
                if (shelf?.Data is RectangleGeometry shelfGeometry)
                {
                    double width = shelfGeometry.Rect.Width;
                    double height = shelfGeometry.Rect.Height;
                    if (width <= 0 || height <= 0)
                    {
                        // Wait for layout to complete
                        containerGrid.LayoutUpdated += (s, e) =>
                        {
                            if (containerGrid.ActualWidth > 0 && containerGrid.ActualHeight > 0)
                            {
                                CreatePlateInShelf(containerGrid, containerGrid.ActualWidth,
                                                 containerGrid.ActualHeight, fillColor, plate);
                            }
                        };
                    }
                    else
                    {
                        CreatePlateInShelf(containerGrid, width, height,
                                         fillColor, plate);
                    }
                }
            }
        }

        private void CreatePlateInShelf(Grid containerGrid, double width, double height,
                                      Brush fillColor, Plate plate)
        {
            Path visualPlate = CreateShelf(width, height);
            visualPlate.Tag = plate;
            visualPlate.Fill = fillColor;
            visualPlate.Opacity = 0.8;

            // Enable dragging
            visualPlate.MouseLeftButtonDown += Plate_MouseLeftButtonDown;
            visualPlate.MouseMove += Plate_MouseMove;
            visualPlate.MouseLeftButtonUp += Plate_MouseLeftButtonUp;

            containerGrid.Children.Add(visualPlate);
        }
        private void UpdatePlateSize(Grid container, Path plate)
        {
            Path shelf = container.Children.OfType<Path>().FirstOrDefault();
            if (shelf?.Data is RectangleGeometry shelfGeometry)
            {
                var newGeometry = new RectangleGeometry(
                    new Rect(0, 0, shelfGeometry.Rect.Width, shelfGeometry.Rect.Height),
                    shelfGeometry.RadiusX,
                    shelfGeometry.RadiusY
                );
                plate.Data = newGeometry;
            }
        }

        private TranslateTransform dragTransform;
        private CompositionTarget compositionTarget;
        private bool isDragging = false;
        private Point startPoint;
        private Path draggedPlate;
        private Grid sourceGrid;
        private const double dragThreshold = 5.0; // Pixels of movement before considering it a drag
        private Point dragStartPosition;
        private bool isPositionInitialized = false;
        private Point offset;
        private Point initialPlatePosition;

        private void Plate_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            draggedPlate = sender as Path;
            if (draggedPlate != null)
            {
                draggedPlate.CaptureMouse();
                startPoint = e.GetPosition(MainGrid); // Use MainGrid for consistent coordinate space
                sourceGrid = draggedPlate.Parent as Grid;
                initialPlatePosition = draggedPlate.TranslatePoint(new Point(0, 0), MainGrid);

                dragTransform = new TranslateTransform();
                draggedPlate.RenderTransform = dragTransform;

                sourceGrid.Children.Remove(draggedPlate);
                MainGrid.Children.Add(draggedPlate);

                UpdatePlateSize(sourceGrid, draggedPlate);
                dragTransform.X = initialPlatePosition.X;
                dragTransform.Y = initialPlatePosition.Y;

                Panel.SetZIndex(draggedPlate, 1000);
                e.Handled = true;
            }
        }

        private void Plate_MouseMove(object sender, MouseEventArgs e)
        {
            if (draggedPlate == null) return;

            Point currentPosition = e.GetPosition(MainGrid);

            if (!isDragging)
            {
                Vector difference = startPoint - currentPosition;
                if (Math.Abs(difference.X) > dragThreshold || Math.Abs(difference.Y) > dragThreshold)
                {
                    isDragging = true;
                }
            }

            if (isDragging)
            {
                double offsetX = currentPosition.X - startPoint.X;
                double offsetY = currentPosition.Y - startPoint.Y;

                dragTransform.X = initialPlatePosition.X + offsetX;
                dragTransform.Y = initialPlatePosition.Y + offsetY;
            }

            e.Handled = true;
            }

        private void Plate_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (draggedPlate != null)
            {
                draggedPlate.ReleaseMouseCapture();
                if (isDragging)
                {
                    try
                    {
                        Point currentPosition = e.GetPosition(StackerGrid);
                        HitTestResult result = VisualTreeHelper.HitTest(StackerGrid, currentPosition);

                        Grid targetGrid = null;
                        if (result != null)
                        {
                            DependencyObject current = result.VisualHit;
                            while (current != null && current is FrameworkElement)
                            {
                                if (current is Grid grid && Shelves.ContainsValue(grid))
                                {
                                    targetGrid = grid;
                                    break;
                                }
                                current = VisualTreeHelper.GetParent(current);
                            }
                        }

                        if (targetGrid != null && targetGrid != sourceGrid)
                        {
                            MainGrid.Children.Remove(draggedPlate);
                            targetGrid.Children.Add(draggedPlate);
                            dragTransform.X = 0;
                            dragTransform.Y = 0;


                            if (draggedPlate.Tag is Plate plate)
                            {
                                var targetPosition = GetGridPosition(targetGrid);
                                if (targetPosition.HasValue)
                                {
                                    if (plate is SourcePlate sourcePlate)
                                    {
                                        sourcePlate.PositionInStack = targetPosition.Value.row;
                                        sourcePlate.Stack = ColumnShift(targetPosition.Value.column + 1);
                                    }
                                    else if (plate is DestinationPlate destPlate)
                                    {
                                        destPlate.PositionInStack = targetPosition.Value.row;
                                        destPlate.Stack = ColumnShift(targetPosition.Value.column + 1);
                                    }
                                }
                            }
                        }
                        else
                        {
                            MainGrid.Children.Remove(draggedPlate);
                            dragTransform.X = 0;
                            dragTransform.Y = 0;
                            sourceGrid.Children.Add(draggedPlate);
                        }
                    }
                    finally
                    {
                         Panel.SetZIndex(draggedPlate, 0);
                    }
                }
                else
                {
                    MainGrid.Children.Remove(draggedPlate);
                    dragTransform.X = 0;
                    dragTransform.Y = 0;
                    sourceGrid.Children.Add(draggedPlate);
                    HandlePlateClick();
                }

                isDragging = false;
                draggedPlate = null;
                dragTransform = null;
                e.Handled = true;
            }
        }

        // helper method to find the grid position
        private (int row, int column)? GetGridPosition(Grid grid)
        {
            foreach (var kvp in Shelves)
            {
                if (kvp.Value == grid)
                {
                    return kvp.Key;
                }
            }
            return null;
        }

        private void HandlePlateClick()
        {
            if (draggedPlate.Tag is Plate plate)
            {
                draggedPlate.Fill = (draggedPlate.Fill == selectedPlateColor)
                    ? unselectedPlateColor
                    : selectedPlateColor;

                if (plate is SourcePlate)
                {
                    ViewModel.SelectedSourcePlate = plate;
                }
                else if (plate is DestinationPlate)
                {
                    ViewModel.SelectedDestinationPlate = plate;
                }
            }
        }

        public void OnPlatesChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (var item in e.NewItems)
                {
                    if (item.GetType() == typeof(SourcePlate))
                    {
                        AddPlateToShelf(((SourcePlate)item).PositionInStack, ColumnShift(((SourcePlate)item).Stack), unselectedPlateColor, (SourcePlate)item);
                    }
                    else if (item.GetType() == typeof(DestinationPlate))
                    {
                        AddPlateToShelf(((DestinationPlate)item).PositionInStack, ColumnShift(((DestinationPlate)item).Stack), unselectedPlateColor, (DestinationPlate)item);
                    }
                }
            }
        }

        private int ColumnShift(int originalColumn)
        {
            int newColumn = ((originalColumn - 1 + Offset) % Columns + Columns) % Columns + 1;
            return newColumn;
        }

        //private void Plate_MouseLeftButtonDown(object sender, EventArgs e)
        //{
        //    if (sender is Rectangle clickedPlate)
        //    {
        //        clickedPlate.Fill = (clickedPlate.Fill == selectedPlateColor) ? unselectedPlateColor : selectedPlateColor;
        //        if (clickedPlate.Tag.GetType() == typeof(SourcePlate))
        //        {
        //            ViewModel.SelectedSourcePlate = (Plate)clickedPlate.Tag;
        //        }
        //        else if (clickedPlate.Tag.GetType() == typeof(DestinationPlate))
        //        {
        //            ViewModel.SelectedDestinationPlate = (Plate)clickedPlate.Tag;
        //        }
        //    }
        //}

        private void SelectPlate(object sender, EventArgs e)
        {
            
        }

        public event EventHandler PlateSelected;

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Reset and start the timer on each size change
            resizeTimer.Stop();
            resizeTimer.Start();
        }

        private void ResizeTimer_Tick(object sender, EventArgs e)
        {
            resizeTimer.Stop();
            CreateMicroplateStacker();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            WindowDragRequested?.Invoke(this, EventArgs.Empty);
        }

        private void MaximizeWindow(MainViewModel ViewModel)
        {
            if (ViewModel.IsMaximized)
            {
                this.WindowState = WindowState.Normal;
                ViewModel.IsMaximized = false;
            }
            else
            {
                this.WindowState = WindowState.Maximized;
                ViewModel.IsMaximized = true;
            }
        }

        public event EventHandler WindowDragRequested;
    }
}