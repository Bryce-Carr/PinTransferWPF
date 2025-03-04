using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Integration;
using PinTransferParameters;
using System.Collections.Specialized;
using System.Diagnostics;

namespace ViewModels
{
    using static Integration.RunLogger;
    using StackType = HotelStacker;
    public partial class MainViewModel : ObservableObject
    {
        public InstrumentController _instrumentController;
        public InstrumentEvents _events;
        private JournalParser<StackType> _parser;
        private CommandRunner<StackType> _epsonRunner;
        private CommandRunner<StackType> _kx2Runner;
        private RunLogger _runLogger;
        private Carousel<StackType> _carousel;
        private CancellationTokenSource _cts;
        private int _numStacks = Parameters.numStacks;
        private int _stackCapacity = 25; //TODO calculate programatically
        string connectionString = "Data Source=" + Parameters.LoggingDatabase;
        private ObservableCollection<SourcePlate> _sourcePlates;
        public ObservableCollection<SourcePlate> SourcePlates
        {
            get => _sourcePlates;
            set
            {
                if (value != _sourcePlates)
                {
                    _sourcePlates = value;
                    OnPropertyChanged(nameof(SourcePlates));
                }
            }
        }
        private ObservableCollection<DestinationPlate> _destinationPlates;
        public ObservableCollection<DestinationPlate> DestinationPlates
        {
            get => _destinationPlates;
            set
            {
                if (value != _destinationPlates)
                {
                    _destinationPlates = value;
                    OnPropertyChanged(nameof(DestinationPlates));
                }
            }
        }

        public MainViewModel(InstrumentController instrumentController, string connectionString)
        {
            SourcePlates = new ObservableCollection<SourcePlate>();
            DestinationPlates = new ObservableCollection<DestinationPlate>();
            Func<int, StackType> stackerFactory = _stackCapacity => new StackType(_stackCapacity);
            _carousel = new Carousel<StackType>(_numStacks, _stackCapacity, stackerFactory);
            _instrumentController = instrumentController;
            _events = new InstrumentEvents();
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

        [ObservableProperty]
        private WindowState windowState = WindowState.Normal;

        [ObservableProperty]
        private string statusText;

        private bool _canRunCommands = true;
        public bool CanRunCommands
        {
            get => _canRunCommands;
            set => SetProperty(ref _canRunCommands, value);
        }
        [ObservableProperty]
        private bool canCancel;

        [ObservableProperty]
        private bool isMaximized;

        [ObservableProperty]
        private ObservableCollection<string> fileOptions = new ObservableCollection<string>
        {
            "Save Script",
            "Load Script"
        };

        [ObservableProperty]
        private string selectedFileOption;

        [ObservableProperty]
        private ObservableCollection<string> toolOptions = new ObservableCollection<string>
        {
            "Labware Manager"
        };

        [ObservableProperty]
        private string selectedToolOption;

        public RunInfo RunInfo { get; set; } = new RunInfo
        {
            RunID = null,
            TimeRun = DateTime.Now,
            ScreenNumber = -1,
            UserName = "Bryce",
            JournalID = "testJournal"
        };

        private void AppendStatus(string message)
        {
            StatusText += $"{DateTime.Now:HH:mm:ss} - {message}\n";
        }

        [ObservableProperty]
        private Plate selectedSourcePlate;
        
        [ObservableProperty]
        private Plate selectedDestinationPlate;

        [ObservableProperty]
        private int sourcesToAdd;

        [ObservableProperty]
        private int replicatesOfSourcesToAdd;

        [ObservableProperty]
        private int volumeOfSourcesToAdd;

        [RelayCommand]
        private void CreatePlates()
        {
            //TODO account for existing plates
            int StartingStack;
            int FinalStack;
            int StartingPosition;
            int FinalPosition;
            int existingSourcePlatesCount = SourcePlates.Count;

            for (int sourceCount = existingSourcePlatesCount; sourceCount < existingSourcePlatesCount + SourcesToAdd; sourceCount++)
            {
                int currentSourcePlates = SourcePlates.Count();
                StartingStack = currentSourcePlates / (_stackCapacity);
                FinalStack = StartingStack;
                StartingPosition = currentSourcePlates - ((StartingStack) * (_stackCapacity));
                FinalPosition = StartingPosition;
                if (typeof(StackType) == typeof(HotelStacker))
                {
                    FinalStack = StartingStack;
                    FinalPosition = StartingPosition;
                }
                else if (typeof(StackType) == typeof(SequentialStacker))
                {
                    FinalStack = StartingStack * 2; //TODO fix this...
                    FinalPosition = currentSourcePlates - ((FinalStack) * (_stackCapacity));
                    //if (FinalStack > _numStacks)
                    if (FinalStack > 2)
                    {
                        throw new InvalidOperationException("Too many plates...");
                    }
                }
                SourcePlates.Add(new SourcePlate
                {
                    ID = "source_" + sourceCount.ToString(),
                    Stack = StartingStack,
                    FinalStack = FinalStack,
                    PositionInStack = StartingPosition,
                    FinalPositionInStack = FinalPosition,
                    Status = new Dictionary<string, bool>()
                        {
                            { "pinned", false }
                        },
                    Replicates = new Tuple<int, int>(VolumeOfSourcesToAdd, ReplicatesOfSourcesToAdd)
                });
            }

            foreach (var sourcePlate in SourcePlates.Skip(existingSourcePlatesCount))
            {
                for (int replicate = 1; replicate <= sourcePlate.Replicates.Item2; replicate++)
                {
                    StartingStack = (_numStacks - 1) - (DestinationPlates.Count / _stackCapacity);
                    FinalStack = StartingStack;
                    StartingPosition = DestinationPlates.Count() - (((_numStacks - 1) - StartingStack) * _stackCapacity);
                    FinalPosition = StartingPosition;
                    if (typeof(StackType) == typeof(HotelStacker))
                    {
                        FinalStack = StartingStack;
                        FinalPosition = StartingPosition;
                    }
                    else if (typeof(StackType) == typeof(SequentialStacker))
                    {
                        FinalStack = (_numStacks - 1) - ((Math.Abs(DestinationPlates.Count - 1) / _stackCapacity) * 2);
                        FinalPosition = DestinationPlates.Count() - (((_numStacks - 1) - FinalStack) * _stackCapacity);
                        if (FinalStack > (_numStacks - 1))
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
                    DestinationPlate plate = DestinationPlates.ToList().Find(dp => dp.ID == "destination_" + (DestinationPlates.Count()).ToString());
                    plate.AddSourcePlate(sourcePlate.ID, sourcePlate.Replicates.Item2);
                }
            }
        }

        [RelayCommand]
        private void CreateRun()
        {
            //TODO fix sequential logic
            try
            {
                JournalInfo journalInfo = new JournalInfo
                {
                    JournalID = "testJournal2",
                    SourcePlates = SourcePlates.ToList(),
                    DestinationPlates = DestinationPlates.ToList()
                };

                _runLogger.CreateJournal(journalInfo);
                _runLogger.CreateRun(RunInfo);

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

        [RelayCommand]
        private async Task StartRun()
        {
            if (Parameters.UsingInstruments)
            {
                if (!_instrumentController.KX2.IsInitialized())
                {
                    await Task.Run(() =>
                    {
                        _instrumentController.InitializeArm();
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

            CanRunCommands = false;
            CanCancel = true;
            StatusText = string.Empty;
            AppendStatus("Running commands...");

            _cts = new CancellationTokenSource();

            // make carousel from starting plate positions for journal

            try
            {
                await Task.WhenAll(
                    _epsonRunner.RunCommandsAsync(currentJournalID, 1, _cts.Token),
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
                CanRunCommands = true;
                CanCancel = false;
            }
        }

        [RelayCommand]
        private async Task ResumeRun()
        {
            var lastRunState = _runLogger.LoadRunState("testJournal"); // TODO: Replace with actual journal ID
            if (Parameters.UsingInstruments)
            {
                if (!_instrumentController.KX2.IsInitialized())
                {
                    await Task.Run(() =>
                    {
                        _instrumentController.InitializeArm();
                    });
                }
            }
            ResumeRun(lastRunState);
        }

        [RelayCommand]
        private void CancelRun()
        {
            CanCancel = false;
            AppendStatus("Cancelling...");
            _instrumentController.KX2.ScriptStop();
            _cts?.Cancel();
            if (Parameters.UsingInstruments)
            {
                _instrumentController.StopAll();
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

            CanRunCommands = false;
            CanCancel = true;
            StatusText = string.Empty;
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
                CanRunCommands = true;
                CanCancel = false;
            }
        }
        
        partial void OnSelectedFileOptionChanged(string value)
        {
            if (value == "Save Script")
            {
                OpenSaveScriptWindow();
            }
            else if (value == "Load Script")
            {
                OpenLoadScriptWindow();
            }
        }

        partial void OnSelectedSourcePlateChanged(Plate value)
        {
            // TODO find source plate in visual stack and select
        }
        partial void OnSelectedDestinationPlateChanged(Plate value)
        {
            // TODO find destination plate in visual stack and select
        }

        private void OpenSaveScriptWindow()
        {
            // Logic to open Save Script window
        }

        private void OpenLoadScriptWindow()
        {
            // Logic to open Load Script window

        }
        partial void OnSelectedToolOptionChanged(string value)
        {
            if (value == "Labware Manager")
            {
                OpenLabware();
            }
        }
        private void OpenLabware()
        {
            OpenLabwareRequested?.Invoke(this, EventArgs.Empty);
        }
        public event EventHandler OpenLabwareRequested;

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
                        _instrumentController.m_spel.Call("OpenClamps");
                    }
                    else if (state == "close")
                    {
                        _instrumentController.m_spel.Call("CloseClamps");
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
                            _instrumentController.m_spel.Call("AttachSM");
                            break;
                        case "100":
                            _instrumentController.m_spel.Call("AttachMD");
                            break;
                        case "300":
                            _instrumentController.m_spel.Call("AttachLG");
                            break;
                        case "96":
                            _instrumentController.m_spel.Call("Attach96");
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
                            _instrumentController.m_spel.Call("DetachSM");
                            break;
                        case "100":
                            _instrumentController.m_spel.Call("DetachMD");
                            break;
                        case "300":
                            _instrumentController.m_spel.Call("DetachLG");
                            break;
                        case "96":
                            _instrumentController.m_spel.Call("Detach96");
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
                        _instrumentController.m_spel.Call("WashFake");
                    }
                    else
                    {
                        switch (toolId)
                        {
                            case "33":
                                _instrumentController.m_spel.Call("WashSM");
                                break;
                            case "100":
                                _instrumentController.m_spel.Call("WashMD");
                                break;
                            case "300":
                                _instrumentController.m_spel.Call("WashLG");
                                break;
                            case "96":
                                _instrumentController.m_spel.Call("Wash96");
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
                            _instrumentController.m_spel.Call("TransferSM");
                            break;
                        case "100":
                            _instrumentController.m_spel.Call("TransferMD");
                            break;
                        case "300":
                            _instrumentController.m_spel.Call("TransferLG");
                            break;
                        case "96":
                            _instrumentController.m_spel.Call("Transfer96");
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
                    _instrumentController.m_spel.Call("MoveSafe");
                });
            };

            //KX2
            _events.OnArmSafe += async (ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"Safe Move for Arm");
                await Task.Run(() =>
                {
                    _instrumentController.MovetoTeachPoint("SafeLow");
                });
            };

            _events.OnArmHome += async (ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"Arm homed");
                await Task.Run(() =>
                {
                    _instrumentController.KX2.WarningIdleStartTimeUpdate(); //Suppress warning/buzzer
                    ret = _instrumentController.KX2.TeachPointMoveTo("Home", Parameters.HomeArmSpeed, Parameters.ArmAccel, true, TimeoutMsec: ref timeout, SendEventWhenMoveDone: false, Index: ref index);
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
                        errorCode = _instrumentController.GetPlateFromStack(plateID, (short)_stackCapacity, (short)location);
                    }
                    else
                    {
                        errorCode = _instrumentController.GetPlateFromStack(plateID, (short)_stackCapacity, (short)location, (short)_events.ResumeLine);
                    }
                    Int32.TryParse(_instrumentController.KX2.GetErrorCode(2), out _events.ResumeLine);
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
                        errorCode = _instrumentController.GetPlateFromStage(plateID);
                    }
                    else
                    {
                        errorCode = _instrumentController.GetPlateFromStage(plateID, (short)_events.ResumeLine);
                    }
                    Int32.TryParse(_instrumentController.KX2.GetErrorCode(2), out _events.ResumeLine);
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
                        errorCode = _instrumentController.SetPlateToStack(plateID, (short)_stackCapacity, (short)location);
                    }
                    else
                    {
                        errorCode = _instrumentController.SetPlateToStack(plateID, (short)_stackCapacity, (short)location, (short)_events.ResumeLine);
                    }
                    Int32.TryParse(_instrumentController.KX2.GetErrorCode(2), out _events.ResumeLine);
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
                        errorCode = _instrumentController.SetPlateToStage(plateID);
                    }
                    else
                    {
                        errorCode = _instrumentController.SetPlateToStage(plateID, (short)_events.ResumeLine);
                    }
                    Int32.TryParse(_instrumentController.KX2.GetErrorCode(2), out _events.ResumeLine);
                });
            };

            // Carousel
            _events.OnCarouselRotated += async (stacker, plateType, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"Carousel rotated to {stacker}");
                await Task.Run(() =>
                {
                    _instrumentController.RotateCarousel(stacker, plateType);
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

        [RelayCommand]
        private void MinimizeWindow()
        {
            MinimizeWindowRequested?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler MinimizeWindowRequested;

        [RelayCommand]
        private void MaximizeWindow()
        {
            MaximizeWindowRequested?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler MaximizeWindowRequested;

        [RelayCommand]
        private void CloseWindow()
        {
            CloseWindowRequested?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler CloseWindowRequested;

    }
}