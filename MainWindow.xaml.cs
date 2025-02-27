using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Data.Entity.Core.Common.CommandTrees.ExpressionBuilder;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Integration;
using RCAPINet;
using static Integration.InstrumentEvents;
using static Integration.RunLogger;
using CommunityToolkit.Mvvm;
using CommunityToolkit.Mvvm.ComponentModel;
using ViewModels;
using PinTransferParameters;
using System.Collections.Specialized;
using System.Data.Common;
using System.Windows.Controls.Primitives;
using System.Net.Sockets;

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
            stackerWidth = Math.Max(1, stackerWidth);
            stackerHeight = Math.Max(1, stackerHeight);

            for (int i = 0; i < Rows - 1; i++)
            {
                for (int j = 0; j < Columns; j++)
                {
                    Path shelf = CreateShelf(stackerWidth, stackerHeight);
                    Grid containerGrid = new Grid();
                    containerGrid.Children.Add(shelf);
                    containerGrid.Margin = new Thickness(horizontalMargin / 2, verticalMargin / 2, horizontalMargin / 2, verticalMargin / 2);

                    Grid.SetRow(containerGrid, Rows - 2 - i);
                    Grid.SetColumn(containerGrid, j);
                    StackerGrid.Children.Add(containerGrid);

                    // Store the containerGrid for later reference
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

        //public void AddPlateToShelf(int row, int column, Brush fillColor, Plate plate)
        //{
        //    //if (Shelves.TryGetValue((row, column), out Grid containerGrid))
        //    //{
        //    //    containerGrid.UpdateLayout();
        //    //    Path Shelf = containerGrid.Children[0] as Path; // Assuming the U shape is the first child
        //    //    if (Shelf == null) return; // Exit if we can't find the U shape

        //    //    double containerWidth = containerGrid.ActualWidth;
        //    //    double containerHeight = containerGrid.ActualHeight;

        //    //    if (containerWidth <= 0 || containerHeight <= 0)
        //    //    {
        //    //        // If ActualWidth/Height are not set, use the Width/Height properties
        //    //        containerWidth = containerGrid.Width;
        //    //        containerHeight = containerGrid.Height;
        //    //    }

        //    //    double uThickness = Math.Min(containerWidth, containerHeight) * 0.1; // U shape thickness

        //    //    // Calculate rectangle dimensions
        //    //    double rectWidth = containerWidth - (4 * uThickness);
        //    //    double rectHeight = containerHeight - (2 * uThickness);

        //    //    Rectangle visualPlate = new Rectangle
        //    //    {
        //    //        Tag = plate,
        //    //        Width = Math.Max(0, rectWidth),
        //    //        Height = Math.Max(0, rectHeight),
        //    //        VerticalAlignment = VerticalAlignment.Center,
        //    //        HorizontalAlignment = HorizontalAlignment.Center,
        //    //        Fill = fillColor
        //    //    };
        //    //    // Add the event handler
        //    //    visualPlate.MouseLeftButtonDown += Plate_MouseLeftButtonDown;
        //    //    //TODO unsubscribe when plate deleted?
        //    //    // Add the rectangle to the existing containerGrid
        //    //    containerGrid.Children.Add(visualPlate);
        //    //}
        //    if (Shelves.TryGetValue((row, column), out Grid containerGrid))
        //    {
        //        containerGrid.UpdateLayout();

        //        double containerWidth = containerGrid.ActualWidth;
        //        double containerHeight = containerGrid.ActualHeight;

        //        if (containerWidth <= 0 || containerHeight <= 0)
        //        {
        //            containerWidth = containerGrid.Width;
        //            containerHeight = containerGrid.Height;
        //        }

        //        // Create a plate with the same dimensions as the shelf
        //        Path visualPlate = CreateShelf(containerWidth, containerHeight);
        //        visualPlate.Tag = plate;
        //        visualPlate.Fill = fillColor;
        //        visualPlate.Opacity = 0.8; // Makes it slightly transparent to see overlap

        //        // Enable dragging
        //        visualPlate.MouseLeftButtonDown += Plate_MouseLeftButtonDown;
        //        visualPlate.MouseMove += Plate_MouseMove;
        //        visualPlate.MouseLeftButtonUp += Plate_MouseLeftButtonUp;

        //        containerGrid.Children.Add(visualPlate);
        //    }
        //}

        public void AddPlateToShelf(int row, int column, Brush fillColor, Plate plate)
        {
            if (Shelves.TryGetValue((row, column), out Grid containerGrid))
            {
                containerGrid.UpdateLayout();

                double containerWidth = containerGrid.ActualWidth;
                double containerHeight = containerGrid.ActualHeight;

                if (containerWidth <= 0 || containerHeight <= 0)
                {
                    containerWidth = containerGrid.Width;
                    containerHeight = containerGrid.Height;
                }

                Path visualPlate = CreateShelf(containerWidth, containerHeight);
                visualPlate.Tag = plate;
                visualPlate.Fill = fillColor;
                visualPlate.Opacity = 0.8;

                // Add hardware acceleration - corrected syntax
                RenderOptions.SetEdgeMode(visualPlate, EdgeMode.Aliased);
                RenderOptions.SetBitmapScalingMode(visualPlate, BitmapScalingMode.LowQuality);
                visualPlate.CacheMode = new BitmapCache();

                // Enable dragging
                visualPlate.MouseLeftButtonDown += Plate_MouseLeftButtonDown;
                visualPlate.MouseMove += Plate_MouseMove;
                visualPlate.MouseLeftButtonUp += Plate_MouseLeftButtonUp;

                containerGrid.Children.Add(visualPlate);
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

        private void Plate_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            draggedPlate = sender as Path;
            if (draggedPlate != null)
            {
                // Calculate offset between mouse and plate's top-left corner
                Point mousePos = e.GetPosition(draggedPlate);
                offset = new Point(mousePos.X, mousePos.Y);

                sourceGrid = draggedPlate.Parent as Grid;

                // Initialize transform
                dragTransform = new TranslateTransform();
                draggedPlate.RenderTransform = dragTransform;

                // Set up hardware acceleration
                RenderOptions.SetEdgeMode(draggedPlate, EdgeMode.Aliased);
                RenderOptions.SetBitmapScalingMode(draggedPlate, BitmapScalingMode.LowQuality);
                draggedPlate.CacheMode = new BitmapCache();

                // Store original size and position
                double originalWidth = draggedPlate.Width;
                double originalHeight = draggedPlate.Height;

                // Get absolute position before removing from source
                Point platePosition = draggedPlate.TranslatePoint(new Point(0, 0), MainGrid);

                // Move to main grid
                sourceGrid.Children.Remove(draggedPlate);
                MainGrid.Children.Add(draggedPlate);

                // Set initial position in main grid
                draggedPlate.Width = originalWidth;
                draggedPlate.Height = originalHeight;
                Canvas.SetLeft(draggedPlate, platePosition.X);
                Canvas.SetTop(draggedPlate, platePosition.Y);
                Panel.SetZIndex(draggedPlate, 1000);

                draggedPlate.CaptureMouse();
                CompositionTarget.Rendering += UpdateDragPosition;
                e.Handled = true;
            }
        }

        private void UpdateDragPosition(object sender, EventArgs e)
        {
            if (draggedPlate != null)
            {
                Point currentPosition = Mouse.GetPosition(MainGrid);
                Canvas.SetLeft(draggedPlate, currentPosition.X - offset.X);
                Canvas.SetTop(draggedPlate, currentPosition.Y - offset.Y);
            }
        }

        private void Plate_MouseMove(object sender, MouseEventArgs e)
        {
            if (draggedPlate == null) return;

            if (!isDragging)
            {
                Point currentPosition = e.GetPosition(null);
                Vector difference = startPoint - currentPosition;

                if (Math.Abs(difference.X) > dragThreshold || Math.Abs(difference.Y) > dragThreshold)
                {
                    isDragging = true;
                }
            }

            e.Handled = true;
        }

        private void Plate_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (draggedPlate != null)
            {
                draggedPlate.ReleaseMouseCapture();
                CompositionTarget.Rendering -= UpdateDragPosition;

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

                        if (targetGrid != null)
                        {
                            try
                            {
                                // Remove from main grid and add to target
                                MainGrid.Children.Remove(draggedPlate);
                                dragTransform.X = 0;
                                dragTransform.Y = 0;
                                targetGrid.Children.Add(draggedPlate);

                                draggedPlate.Width = targetGrid.ActualWidth;
                                draggedPlate.Height = targetGrid.ActualHeight;

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
                            catch (Exception ex)
                            {
                                // Return to source grid if error
                                MainGrid.Children.Remove(draggedPlate);
                                dragTransform.X = 0;
                                dragTransform.Y = 0;
                                sourceGrid.Children.Add(draggedPlate);
                                System.Diagnostics.Debug.WriteLine($"Error moving plate: {ex.Message}");
                            }
                        }
                        else
                        {
                            // Return to source grid if no valid target
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