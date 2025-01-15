using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
using Integration;
using RCAPINet;
using static Integration.InstrumentEvents;
using static Integration.RunLogger;

namespace PinTransferWPF
{
    using StackType = HotelStacker;
    public partial class MainWindow : Window
    {
        MessageBoxResult messageBoxResult;
        private InstrumentController InstrumentController;
        private readonly InstrumentEvents _events;
        private JournalParser<StackType> _parser;
        private CommandRunner<StackType> _epsonRunner;
        private CommandRunner<StackType> _kx2Runner;
        private RunLogger _runLogger;
        private readonly Carousel<StackType> _carousel;
        private CancellationTokenSource _cts;
        private int _numStacks;
        private int _stackCapacity;
        private DispatcherTimer resizeTimer;
        private Dictionary<(int, int), Grid> Shelves = new Dictionary<(int, int), Grid>();


        public MainWindow()
        {
            InitializeComponent();

            // Define grid rows and columns
            for (int i = 0; i < 3; i++)
            {
                StackerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                StackerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            CreateMicroplateStacker();
            SizeChanged += MainWindow_SizeChanged;

            InstrumentController = new InstrumentController(this);
            if (Parameters.UsingInstruments)
            {
                InstrumentController.InitializeAllDevices();

                this.Closing += new CancelEventHandler(MainWindow_Closing);

                void MainWindow_Closing(object sender, CancelEventArgs e)
                {

                    InstrumentController.OnShutdown();
                }
            }


            string connectionString = "Data Source=" + Parameters.LoggingDatabase;
            _events = new InstrumentEvents();
            _numStacks = Parameters.numStacks;
            _stackCapacity = 25; // TODO: replace this with a function that will determine stack capacity if using sequential stackers
            Func<int, StackType> stackerFactory = _stackCapacity => new StackType(_stackCapacity);
            _carousel = new Carousel<StackType>(_numStacks, _stackCapacity, stackerFactory);

            _runLogger = new RunLogger(connectionString);
            _parser = new JournalParser<StackType>(connectionString, _events, _carousel);
            _epsonRunner = new CommandRunner<StackType>(_parser, "Epson", _runLogger, _events);
            _kx2Runner = new CommandRunner<StackType>(_parser, "KX2", _runLogger, _events);
            if (Parameters.UsingInstruments)
            {
                SetupEventHandlers();
            }
            else
            {
                SetupEventHandlersText();
            }
            CheckForUnfinishedRun(connectionString);

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
        private void CheckForUnfinishedRun(string connectionString)
        {
            var lastUnfinishedRunState = _runLogger.LoadMostRecentUnfinishedRunState();
            if (lastUnfinishedRunState != null)
            {
                var result = MessageBox.Show($"Last run (Journal ID: {lastUnfinishedRunState.JournalID}) wasn't finished. Do you want to resume?", "Resume Run", MessageBoxButton.YesNo);
                if (result == MessageBoxResult.Yes)
                {
                    ResumeRun(lastUnfinishedRunState);
                }
            }
        }

        private async void ResumeRun(RunState runState)
        {
            // make carousel from saved plate positions
            _events.DeserializeToolStates(runState.SerializedToolStates);
            _events.DeserializeArmStates(runState.SerializedArmStates);
            _events.DeserializeStageStates(runState.SerializedStageStates);
            _events.DeserializeCarouselStates(runState.SerializedCarouselStates);
            string serializedPlates = runState.Plates;
            List<Plate> deserializedPlates = PlateSerializer.DeserializePlates(serializedPlates);
            PopulateCarousel(deserializedPlates);
            _events._plates = deserializedPlates;
            _events.ResumeLine = runState.ResumeLine;

            RunCommandsButton.IsEnabled = false;
            CancelButton.IsEnabled = true;
            StatusTextBlock.Text = string.Empty;
            AppendStatus($"Resuming run for Journal ID: {runState.JournalID}...");

            _cts = new CancellationTokenSource();

            try
            {
                await Task.WhenAll(
                    _epsonRunner.RunCommandsAsync(runState.JournalID, runState.EpsonCommandID, _cts.Token),
                    _kx2Runner.RunCommandsAsync(runState.JournalID, runState.KX2CommandID, _cts.Token)
                );
            }
            catch (OperationCanceledException)
            {
                AppendStatus("Command execution was cancelled.");
            }
            catch (Exception ex)
            {
                AppendStatus($"An error occurred: {ex.Message}");
            }
            finally
            {
                if (!_cts.Token.IsCancellationRequested)
                {
                    AppendStatus("All commands completed successfully.");
                    _runLogger.MarkRunAsCompleted(runState.JournalID);
                }
                RunCommandsButton.IsEnabled = true;
                CancelButton.IsEnabled = false;
            }
        }

        private void AppendStatus(string message)
        {
            Dispatcher.InvokeAsync(() =>
            {
                StatusTextBlock.Text += $"{DateTime.Now:HH:mm:ss} - {message}\n";
                StatusScrollViewer.ScrollToVerticalOffset(StatusScrollViewer.ScrollableHeight);
            });
        }

        private void SetupEventHandlers()
        {
            //TODO add appropriate checks before opening grippers, etc..
            short ret;
            int timeout = 0;
            byte index = 0;
            short errorCode = 0;

            // Clamps
            _events.OnClampsStateChanged += async (state, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"Clamps {state}");
                await Task.Run(() =>
                {
                    if (state == "open")
                    {
                        InstrumentController.m_spel.Call("OpenClamps");
                    }
                    else if (state == "close")
                    {
                        InstrumentController.m_spel.Call("CloseClamps");
                    }
                });
            };

            // Epson
            _events.OnToolAttached += async (toolId, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"Attaching {toolId}");
                await Task.Run(() =>
                {
                    switch (toolId)
                    {
                        case "33":
                            InstrumentController.m_spel.Call("AttachSM");
                            break;
                        case "100":
                            InstrumentController.m_spel.Call("AttachMD");
                            break;
                        case "300":
                            InstrumentController.m_spel.Call("AttachLG");
                            break;
                        case "96":
                            InstrumentController.m_spel.Call("Attach96");
                            break;
                    }
                });
            };

            _events.OnToolDetached += async (toolId, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"Detaching {toolId}");
                await Task.Run(() =>
                {
                    switch (toolId)
                    {
                        case "33":
                            InstrumentController.m_spel.Call("DetachSM");
                            break;
                        case "100":
                            InstrumentController.m_spel.Call("DetachMD");
                            break;
                        case "300":
                            InstrumentController.m_spel.Call("DetachLG");
                            break;
                        case "96":
                            InstrumentController.m_spel.Call("Detach96");
                            break;
                    }
                });
            };

            _events.OnWashCompleted += async (toolId, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"Washing {toolId}");
                await Task.Run(() =>
                {
                    if (Parameters.Testing)
                    {
                        InstrumentController.m_spel.Call("WashFake");
                    }
                    else
                    {
                        switch (toolId)
                        {
                            case "33":
                                InstrumentController.m_spel.Call("WashSM");
                                break;
                            case "100":
                                InstrumentController.m_spel.Call("WashMD");
                                break;
                            case "300":
                                InstrumentController.m_spel.Call("WashLG");
                                break;
                            case "96":
                                InstrumentController.m_spel.Call("Wash96");
                                break;
                        }
                    }
                });
            };

            _events.OnTransferCompleted += async (toolId, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"Transfering {toolId}");
                await Task.Run(() =>
                {
                    switch (toolId)
                    {
                        case "33":
                            InstrumentController.m_spel.Call("TransferSM");
                            break;
                        case "100":
                            InstrumentController.m_spel.Call("TransferMD");
                            break;
                        case "300":
                            InstrumentController.m_spel.Call("TransferLG");
                            break;
                        case "96":
                            InstrumentController.m_spel.Call("Transfer96");
                            break;
                    }
                });
            };

            _events.OnToolSafe += async (toolId, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"Safe Move {toolId}");
                await Task.Run(() =>
                {
                    InstrumentController.m_spel.Call("MoveSafe");
                });
            };

            //KX2
            _events.OnArmSafe += async (ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"Safe Move for Arm");
                await Task.Run(() =>
                {
                    InstrumentController.MovetoTeachPoint("SafeLow");
                });
            };

            _events.OnArmHome += async (ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"Arm homed");
                await Task.Run(() =>
                {
                    InstrumentController.KX2.WarningIdleStartTimeUpdate(); //Suppress warning/buzzer
                    ret = InstrumentController.KX2.TeachPointMoveTo("Home", Parameters.HomeArmSpeed, Parameters.ArmAccel, true, TimeoutMsec: ref timeout, SendEventWhenMoveDone: false, Index: ref index);
                });
            };

            _events.OnPlateGrabbedFromSequential += async (plateID, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"{plateID} grabbed from hotel");
                await Task.Run(() =>
                {
                    // TODO
                });
            };

            _events.OnPlateGrabbedFromHotel += async (plateID, location, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"{plateID} grabbed from hotel location {location}");
                await Task.Run(() =>
                {
                    if (_events.ResumeLine == 0)
                    {
                        errorCode = InstrumentController.GetPlateFromStack(plateID, (short)_stackCapacity, (short)location);
                    }
                    else
                    {
                        errorCode = InstrumentController.GetPlateFromStack(plateID, (short)_stackCapacity, (short)location, (short)_events.ResumeLine);
                    }
                    Int32.TryParse(InstrumentController.KX2.GetErrorCode(2), out _events.ResumeLine);
                });
            };

            _events.OnPlateGrabbedFromStage += async (plateID, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"{plateID} grabbed from stage");
                await Task.Run(() =>
                {
                    if (_events.ResumeLine == 0)
                    {
                        errorCode = InstrumentController.GetPlateFromStage(plateID);
                    }
                    else
                    {
                        errorCode = InstrumentController.GetPlateFromStage(plateID, (short)_events.ResumeLine);
                    }
                    Int32.TryParse(InstrumentController.KX2.GetErrorCode(2), out _events.ResumeLine);
                });
            };

            _events.OnPlatePlacedToStack += async (plateID, location, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"{plateID} placed to hotel location {location}");
                await Task.Run(() =>
                {
                    if (_events.ResumeLine == 0)
                    {
                        errorCode = InstrumentController.SetPlateToStack(plateID, (short)_stackCapacity, (short)location);
                    }
                    else
                    {
                        errorCode = InstrumentController.SetPlateToStack(plateID, (short)_stackCapacity, (short)location, (short)_events.ResumeLine);
                    }
                    Int32.TryParse(InstrumentController.KX2.GetErrorCode(2), out _events.ResumeLine);
                });
            };

            _events.OnPlatePlacedToStage += async (plateID, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"{plateID} placed to stage");
                await Task.Run(() =>
                {
                    if (_events.ResumeLine == 0)
                    {
                        errorCode = InstrumentController.SetPlateToStage(plateID);
                    }
                    else
                    {
                        errorCode = InstrumentController.SetPlateToStage(plateID, (short)_events.ResumeLine);
                    }
                    Int32.TryParse(InstrumentController.KX2.GetErrorCode(2), out _events.ResumeLine);
                });
            };

            // Carousel
            _events.OnCarouselRotated += async (stacker, plateType, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"Carousel rotated to {stacker}");
                await Task.Run(() =>
                {
                    InstrumentController.RotateCarousel(stacker, plateType);
                });
            };
        }

        private void SetupEventHandlersText()
        {
            // Clamps
            _events.OnClampsStateChanged += async (state, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"Clamps {state}");
                if (state == "open")
                {
                    // open clamps
                }
                else if (state == "close")
                {
                    // close clamps
                }
                await Task.Delay(1000, ct);
            };

            // Epson
            _events.OnToolAttached += async (toolId, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"Tool {toolId} attached");
                await Task.Delay(1000, ct);
            };

            _events.OnToolDetached += async (toolId, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"Tool {toolId} detached");
                await Task.Delay(1000, ct);
            };

            _events.OnWashCompleted += async (toolId, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"Wash for Tool {toolId}");
                await Task.Delay(2000, ct);
            };

            _events.OnTransferCompleted += async (toolId, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"Transfer for Tool {toolId}");
                await Task.Delay(1500, ct);
            };

            _events.OnToolSafe += async (toolId, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"Safe Move for Tool {toolId}");
                await Task.Delay(1000, ct);
            };

            //KX2
            _events.OnArmSafe += async (ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"Safe Move for Arm");
                await Task.Delay(1000, ct);
            };

            _events.OnArmHome += async (ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"Arm homed");
                await Task.Delay(1000, ct);
            };

            _events.OnPlateGrabbedFromSequential += async (plateID, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"{plateID} grabbed from hotel");
                await Task.Delay(2000, ct);
            };

            _events.OnPlateGrabbedFromHotel += async (plateID, location, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"{plateID} grabbed from hotel location {location}");
                await Task.Delay(2000, ct);
            };

            _events.OnPlateGrabbedFromStage += async (plateID, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"{plateID} grabbed from stage");
                await Task.Delay(2000, ct);
            };

            _events.OnPlatePlacedToStack += async (plateID, location, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"{plateID} placed to hotel location {location}");
                await Task.Delay(2000, ct);
            };

            _events.OnPlatePlacedToStage += async (plateID, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"{plateID} placed to stage");
                await Task.Delay(2000, ct);
            };

            // Carousel
            _events.OnCarouselRotated += async (stacker, plateType, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"Carousel rotated to {stacker}");
                await Task.Delay(2000, ct);
            };
        }

        private void btnCreateRun_Click(object sender, RoutedEventArgs e)
        {
            int StartingStack;
            int FinalStack;
            int StartingPosition;
            int FinalPosition;
            //TODO fix sequential logic
            try
            {
                List<SourcePlate> SourcePlates = new List<SourcePlate>();
                List<DestinationPlate> DestinationPlates = new List<DestinationPlate>();
                for (int sourceCount = 1; sourceCount < 3; sourceCount++)
                {
                    StartingStack = Math.Abs(sourceCount - 1) / _stackCapacity + 1;
                    FinalStack = StartingStack;
                    StartingPosition = sourceCount;
                    FinalPosition = StartingPosition;
                    if (typeof(StackType) == typeof(HotelStacker))
                    {
                        FinalStack = StartingStack;
                        FinalPosition = sourceCount;
                    }
                    else if (typeof(StackType) == typeof(SequentialStacker))
                    {
                        FinalStack = StartingStack * 2;
                        FinalPosition = sourceCount - ((FinalStack - 1) * _stackCapacity);
                        if (FinalStack > _numStacks)
                        {
                            throw new InvalidOperationException("Too many plates...");
                        }
                    }
                    SourcePlates.Add(new SourcePlate
                    {
                        ID = "source_" + sourceCount.ToString(),
                        Stack = StartingStack,
                        FinalStack = FinalStack,
                        PositionInStack = sourceCount,
                        FinalPositionInStack = FinalPosition,
                        Status = new Dictionary<string, bool>()
                        {
                            { "pinned", false }
                        },
                        Replicates = new Tuple<int, int>(100, 2)
                    });
                }
                foreach (var sourcePlate in SourcePlates)
                {
                    for (int replicate = 1; replicate <= sourcePlate.Replicates.Item2; replicate++)
                    {
                        StartingStack = _numStacks - ((Math.Abs(DestinationPlates.Count - 1) / _stackCapacity));
                        FinalStack = StartingStack;
                        StartingPosition = DestinationPlates.Count() + 1 - ((_numStacks - StartingStack) * _stackCapacity);
                        FinalPosition = StartingPosition;
                        if (typeof(StackType) == typeof(HotelStacker))
                        {
                            FinalStack = StartingStack;
                            FinalPosition = StartingPosition;
                        }
                        else if (typeof(StackType) == typeof(SequentialStacker))
                        {
                            FinalStack = _numStacks - ((Math.Abs(DestinationPlates.Count - 1) / _stackCapacity) * 2);
                            FinalPosition = DestinationPlates.Count() + 1 - ((_numStacks - FinalStack) * _stackCapacity);
                            if (FinalStack > _numStacks)
                            {
                                throw new InvalidOperationException("Too many plates.");
                            }
                        }
                        DestinationPlates.Add(new DestinationPlate
                        {
                            ID = "destination_" + (DestinationPlates.Count() + 1).ToString(),
                            Stack = StartingStack,
                            FinalStack = FinalStack,
                            PositionInStack = StartingPosition,
                            FinalPositionInStack = FinalPosition,
                            Status = new Dictionary<string, bool>()
                            {
                                { "pinned", false }
                            }

                        });
                        DestinationPlate plate = DestinationPlates.Find(dp => dp.ID == "destination_" + (DestinationPlates.Count()).ToString());
                        plate.AddSourcePlate(sourcePlate.ID, sourcePlate.Replicates.Item2);
                    }
                }
                JournalInfo journalInfo = new JournalInfo
                {
                    JournalID = "testJournal",
                    SourcePlates = SourcePlates,
                    DestinationPlates = DestinationPlates
                };
                RunInfo runInfo = new RunInfo
                {
                    RunID = null,
                    TimeRun = DateTime.Now,
                    ScreenNumber = -1,
                    UserName = "Bryce",
                    JournalID = "testJournal"
                };
                _runLogger.CreateJournal(journalInfo);
                _runLogger.CreateRun(runInfo);

                // Create the dictionary of plates
                var platesDictionary = new Dictionary<string, Tuple<int, int>>();

                // Add SourcePlates to the dictionary
                foreach (var plate in SourcePlates)
                {
                    platesDictionary[plate.ID] = new Tuple<int, int>(plate.Stack, plate.PositionInStack);
                }

                // Add DestinationPlates to the dictionary
                foreach (var plate in DestinationPlates)
                {
                    platesDictionary[plate.ID] = new Tuple<int, int>(plate.Stack, plate.PositionInStack);
                }
                // make initial runstate
                _events.ResetEvents();
                List<Plate> allPlates = new List<Plate>();
                allPlates.AddRange(SourcePlates);
                allPlates.AddRange(DestinationPlates);
                _events._plates = allPlates;
                _epsonRunner.SaveRunState(journalInfo.JournalID, 1);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void PopulateCarousel(List<Plate> deserializedPlates)
        {
            _ = _carousel.RemoveAllPlates();
            foreach (Plate plate in deserializedPlates)
            {
                _carousel.AddPlate(plate, plate.Stack);
            }
        }
        private async void RunCommandsButton_Click(object sender, RoutedEventArgs e)
        {
            if (Parameters.UsingInstruments)
            {
                if (!InstrumentController.KX2.IsInitialized())
                {
                    await Task.Run(() =>
                    {
                        InstrumentController.InitializeArm();
                    });
                }
            }
            string currentJournalID = "testJournal"; // Replace with actual journal ID
            _events.ResetEvents();
            string serializedPlates = _runLogger.LoadRunState(currentJournalID).InitialPlates;
            List<Plate> deserializedPlates = PlateSerializer.DeserializePlates(serializedPlates);
            _events._plates = deserializedPlates;
            PopulateCarousel(deserializedPlates);
            _events.ResumeLine = 0;
            _kx2Runner.SaveRunState(currentJournalID, 1);

            RunCommandsButton.IsEnabled = false;
            CancelButton.IsEnabled = true;
            StatusTextBlock.Text = string.Empty;
            AppendStatus("Running commands...");

            _cts = new CancellationTokenSource();

            // make carousel from starting plate positions for journal

            try
            {
                await Task.WhenAll(
                    //_epsonRunner.RunCommandsAsync(currentJournalID, 1, _cts.Token),
                    _kx2Runner.RunCommandsAsync(currentJournalID, 1, _cts.Token)
                );
            }
            catch (OperationCanceledException)
            {
                AppendStatus("Command execution was cancelled.");
            }
            catch (Exception ex)
            {
                AppendStatus($"An error occurred: {ex.Message}");
            }
            finally
            {
                if (!_cts.Token.IsCancellationRequested)
                {
                    AppendStatus("All commands completed successfully.");
                    _runLogger.MarkRunAsCompleted(currentJournalID);
                }
                RunCommandsButton.IsEnabled = true;
                CancelButton.IsEnabled = false;
            }
        }

        private async void ResumeButton_Click(object sender, RoutedEventArgs e)
        {
            var lastRunState = _runLogger.LoadRunState("testJournal"); // TODO: Replace with actual journal ID
            if (Parameters.UsingInstruments)
            {
                if (!InstrumentController.KX2.IsInitialized())
                {
                    await Task.Run(() =>
                    {
                        InstrumentController.InitializeArm();
                    });
                }
            }
            ResumeRun(lastRunState);
        }

        private async void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            CancelButton.IsEnabled = false;
            StatusTextBlock.Text += "Cancelling...";
            InstrumentController.KX2.ScriptStop();
            _cts?.Cancel();
            if (Parameters.UsingInstruments)
            {
                InstrumentController.StopAll();
            }
        }

        private void toolsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (toolsComboBox.SelectedIndex == 0) // "Open New Menu" option
            {
                LabwareDefinitionsWindow labwareDefinitionsWindow = new LabwareDefinitionsWindow("Data Source=" + Parameters.LabwareDatabase);
                labwareDefinitionsWindow.ShowDialog();
                toolsComboBox.SelectedIndex = -1;
            }
        }

        private void fileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (toolsComboBox.SelectedIndex == 0) // "Open New Menu" option
            {

            }
        }

        private void tools_Clicked(object sender, RoutedEventArgs e)
        {
            toolsComboBox.IsDropDownOpen = !toolsComboBox.IsDropDownOpen;
        }

        private void file_Clicked(object sender, RoutedEventArgs e)
        {
            fileComboBox.IsDropDownOpen = !fileComboBox.IsDropDownOpen;
        }

        private void MinimizeWindow(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void MaximizeClick(object sender, RoutedEventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
            {
                this.WindowState = WindowState.Normal;
                MaximizeButton.Template = (ControlTemplate)this.Resources["MaximizeButtonControlTemplate"];
            }
            else
            {
                this.WindowState = WindowState.Maximized;
                MaximizeButton.Template = (ControlTemplate)this.Resources["RestoreButtonControlTemplate"];
            }
        }

        private void CloseWindow(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void CreateMicroplateStacker()
        {
            int Rows = 26;
            int Columns = 6;
            double aspectRatio = 4.0 / 1; // Width to height ratio for each stacker
            double horizontalMargin = 15;
            double verticalMargin = 5;

            StackerGrid.Children.Clear();
            StackerGrid.RowDefinitions.Clear();
            StackerGrid.ColumnDefinitions.Clear();
            Shelves.Clear();

            for (int i = 0; i < Rows; i++)
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
                    if ((j == 2 && i == 23) | (j == 2 && i == 24) | (j == 3 && i == 24) | (j == 3 && i == 23) | (j == 3 && i == 22) | (j == 3 && i == 21))
                    {
                        shelf.Fill = Brushes.Black;
                    }
                    else
                    {
                        shelf.Fill = Brushes.LightGray;
                    }
                    Grid containerGrid = new Grid();
                    containerGrid.Children.Add(shelf);
                    containerGrid.Margin = new Thickness(horizontalMargin / 2, verticalMargin / 2, horizontalMargin / 2, verticalMargin / 2);

                    Grid.SetRow(containerGrid, i);
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
                    Text = (j + 1).ToString(),
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
            double thickness = Math.Min(width, height) * 0.1; // Adjust thickness as needed

            var pathFigure = new PathFigure
            {
                StartPoint = new Point(0, 0),
                Segments = new PathSegmentCollection
                {
                    new LineSegment(new Point(0, height), true),
                    new LineSegment(new Point(width, height), true),
                    new LineSegment(new Point(width, 0), true),
                    new LineSegment(new Point(width - thickness, 0), true),
                    new LineSegment(new Point(width - thickness, height - thickness), true),
                    new LineSegment(new Point(thickness, height - thickness), true),
                    new LineSegment(new Point(thickness, 0), true),
                    new LineSegment(new Point(0, 0), true)
                }
            };

            var pathGeometry = new PathGeometry();
            pathGeometry.Figures.Add(pathFigure);

            return new Path
            {
                Data = pathGeometry,
                Fill = Brushes.Black,
                Stroke = Brushes.Transparent,
                StrokeThickness = 1
            };
        }

        public void AddPlateToShelf(int row, int column, Brush fillColor)
        {
            if (Shelves.TryGetValue((row, column), out Grid containerGrid))
            {
                containerGrid.UpdateLayout();
                Path Shelf = containerGrid.Children[0] as Path; // Assuming the U shape is the first child
                if (Shelf == null) return; // Exit if we can't find the U shape

                double containerWidth = containerGrid.ActualWidth;
                double containerHeight = containerGrid.ActualHeight;

                if (containerWidth <= 0 || containerHeight <= 0)
                {
                    // If ActualWidth/Height are not set, use the Width/Height properties
                    containerWidth = containerGrid.Width;
                    containerHeight = containerGrid.Height;
                }

                double uThickness = Math.Min(containerWidth, containerHeight) * 0.1; // U shape thickness

                // Calculate rectangle dimensions
                double rectWidth = containerWidth - (4 * uThickness);
                double rectHeight = containerHeight - (2 * uThickness);

                Rectangle plate = new Rectangle
                {
                    Width = Math.Max(0, rectWidth),
                    Height = Math.Max(0, rectHeight),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Fill = Brushes.Coral
            };
                // Add the event handler
                plate.MouseLeftButtonDown += Plate_MouseLeftButtonDown;

                // Add the rectangle to the existing containerGrid
                containerGrid.Children.Add(plate);
            }
        }

        private void Plate_MouseLeftButtonDown(object sender, EventArgs e)
        {
            if (sender is Rectangle clickedPlate)
            {
                clickedPlate.Fill = (clickedPlate.Fill == Brushes.Coral) ? Brushes.MediumSeaGreen : Brushes.Coral;
            }
        }

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
            AddPlateToShelf(24, 2, Brushes.MediumSeaGreen);
            AddPlateToShelf(23, 2, Brushes.MediumSeaGreen);
            AddPlateToShelf(21, 3, Brushes.Coral);
            AddPlateToShelf(24, 3, Brushes.Coral);
            AddPlateToShelf(23, 3, Brushes.Coral);
            AddPlateToShelf(22, 3, Brushes.Coral);
        }

        private void TitleBar_MouseDown(object sender, EventArgs e)
        {
            this.DragMove();
        }
    }
}