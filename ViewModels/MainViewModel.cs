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
using System.Windows.Data;
using System.Windows.Controls;
using System.Windows.Input;

namespace ViewModels
{
    using static Integration.RunLogger;
    using StackType = HotelStacker;

    public class AddButtonViewModel : ObservableObject
    {
        public string Type { get; set; }  // "Source" or "Destination"
        public ICommand AddCommand { get; }

        public AddButtonViewModel(string type, ICommand addCommand)
        {
            Type = type;
            AddCommand = addCommand;
        }
    }

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
        private ObservableCollection<int> _pinVolumes;
        public ObservableCollection<int> PinVolumes
        {
            get => _pinVolumes;
            set
            {
                if (value != _pinVolumes)
                {
                    _pinVolumes = value;
                    OnPropertyChanged(nameof(PinVolumes));
                }
            }
        }
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

        [ObservableProperty]
        private ObservableCollection<object> _sourceItemsWithAddButton;

        [ObservableProperty]
        private ObservableCollection<object> _destinationItemsWithAddButton;

        [ObservableProperty]
        private bool _isInSourceAddMode = false;

        [ObservableProperty]
        private bool _isInDestAddMode = false;

        [ObservableProperty]
        private int _selectedTransferVolume = 100; //TODO fix

        public MainViewModel(InstrumentController instrumentController, string connectionString)
        {
            SourcePlates = new ObservableCollection<SourcePlate>();
            DestinationPlates = new ObservableCollection<DestinationPlate>();
            PinVolumes = new ObservableCollection<int>();
            Func<int, StackType> stackerFactory = _stackCapacity => new StackType(_stackCapacity);
            _carousel = new Carousel<StackType>(_numStacks, _stackCapacity, stackerFactory);
            _instrumentController = instrumentController;
            _events = new InstrumentEvents();
            _runLogger = new RunLogger(connectionString);
            _parser = new JournalParser<StackType>(connectionString, _events, _carousel);
            _epsonRunner = new CommandRunner<StackType>(_parser, "Epson", _runLogger, _events);
            _kx2Runner = new CommandRunner<StackType>(_parser, "KX2", _runLogger, _events);

            SourceItemsWithAddButton = new ObservableCollection<object>();
            DestinationItemsWithAddButton = new ObservableCollection<object>();

            // Create add buttons
            var addSourceButton = new AddButtonViewModel("Source", new RelayCommand<string>(_ => StartAddSourcePlate()));
            var addDestinationButton = new AddButtonViewModel("Destination", new RelayCommand<string>(_ => StartAddDestinationPlate()));


            // Watch for changes to plates collections
            SourcePlates.CollectionChanged += (s, e) => UpdateSourceItemsCollection(addSourceButton);
            DestinationPlates.CollectionChanged += (s, e) => UpdateDestinationItemsCollection(addDestinationButton);

            // Initialize with add buttons
            UpdateSourceItemsCollection(addSourceButton);
            UpdateDestinationItemsCollection(addDestinationButton);

            // TODO: fix hardcoding..
            PinVolumes.Add(33);
            PinVolumes.Add(100);
            PinVolumes.Add(300);

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

        private void UpdateSourceItemsCollection(AddButtonViewModel addButton)
        {
            SourceItemsWithAddButton.Clear();
            foreach (var plate in SourcePlates)
                SourceItemsWithAddButton.Add(plate);
            SourceItemsWithAddButton.Add(addButton);
        }

        private void UpdateDestinationItemsCollection(AddButtonViewModel addButton)
        {
            DestinationItemsWithAddButton.Clear();
            foreach (var plate in DestinationPlates)
                DestinationItemsWithAddButton.Add(plate);
            DestinationItemsWithAddButton.Add(addButton);
        }
        private void StartAddSourcePlate()
        {
            // Clear all selections first
            ClearAllPlateSelections();

            // Exit other add mode if active
            if (IsInDestAddMode)
            {
                IsInDestAddMode = false;

            }
            IsInSourceAddMode = true;
            StatusText += "Please select destination plates to link, then click Confirm Add Source Plate\n";

            // Notify UI that we're in add mode
            OnPropertyChanged(nameof(IsInSourceAddMode));
        }

        private void StartAddDestinationPlate()
        {
            // Clear all selections first
            ClearAllPlateSelections();

            if (IsInSourceAddMode)
            {
                IsInSourceAddMode = false;
            }

            IsInDestAddMode = true;
            StatusText += "Please select source plates to link, then click Confirm Add Destination Plate\n";

            // Notify UI that we're in add mode
            OnPropertyChanged(nameof(IsInDestAddMode));
        }

        // AI
        [RelayCommand]
        private void ConfirmAddSourcePlate()
        {
            if (!IsInSourceAddMode) return;

            // Get selected destination plates
            var selectedDestPlates = new List<DestinationPlate>();
            foreach (var destPlate in DestinationPlates)
            {
                if (destPlate.IsSelected)
                    selectedDestPlates.Add(destPlate);
            }

            if (selectedDestPlates.Count == 0)
            {
                StatusText += "No destination plates selected. Canceled adding source plate.\n";
                IsInSourceAddMode = false;
                return;
            }

            // Create new source plate
            int currentSourcePlates = SourcePlates.Count();
            int startingStack = currentSourcePlates / _stackCapacity;
            int finalStack = startingStack;
            int startingPosition = currentSourcePlates - ((startingStack) * (_stackCapacity));
            int finalPosition = startingPosition;

            var newSourcePlate = new SourcePlate
            {
                ID = "source_" + (SourcePlates.Count + 1).ToString(),
                Stack = startingStack,
                FinalStack = finalStack,
                PositionInStack = startingPosition,
                FinalPositionInStack = finalPosition,
                Status = new Dictionary<string, bool>() { { "pinned", false } },
                Replicates = new Tuple<int, int>(SelectedTransferVolume, selectedDestPlates.Count)
            };

            // Add the source plate
            SourcePlates.Add(newSourcePlate);

            // Link to selected destination plates
            foreach (var destPlate in selectedDestPlates)
            {
                destPlate.AddSourcePlate(newSourcePlate.ID, SelectedTransferVolume);
            }

            StatusText += $"Added source plate {newSourcePlate.ID} linked to {selectedDestPlates.Count} destination plates\n";
            IsInSourceAddMode = false;

            // Set as selected plate after it's added
            HighlightSelectedPlate(newSourcePlate);
        }

        [RelayCommand]
        private void ConfirmAddDestinationPlate()
        {
            if (!IsInDestAddMode) return;

            // Get selected source plates
            var selectedSourcePlates = new List<SourcePlate>();
            foreach (var sourcePlate in SourcePlates)
            {
                if (sourcePlate.IsSelected)
                    selectedSourcePlates.Add(sourcePlate);
            }

            if (selectedSourcePlates.Count == 0)
            {
                StatusText += "No source plates selected. Canceled adding destination plate.\n";
                IsInDestAddMode = false;
                return;
            }

            // Calculate position for the new destination plate
            int startingStack = (_numStacks - 1) - (DestinationPlates.Count / _stackCapacity);
            int finalStack = startingStack;
            int startingPosition = DestinationPlates.Count() - (((_numStacks - 1) - startingStack) * _stackCapacity);
            int finalPosition = startingPosition;

            // Create the new destination plate
            var newDestPlate = new DestinationPlate
            {
                ID = "destination_" + (DestinationPlates.Count + 1).ToString(),
                Stack = startingStack,
                FinalStack = finalStack,
                PositionInStack = startingPosition,
                FinalPositionInStack = finalPosition,
                Status = new Dictionary<string, bool>() { { "pinned", false } }
            };

            // Add the source connections
            foreach (var sourcePlate in selectedSourcePlates)
            {
                newDestPlate.AddSourcePlate(sourcePlate.ID, SelectedTransferVolume);
            }

            // Add the destination plate
            DestinationPlates.Add(newDestPlate);

            StatusText += $"Added destination plate {newDestPlate.ID} linked to {selectedSourcePlates.Count} source plates\n";
            IsInDestAddMode = false;

            // Set as selected plate after it's added
            HighlightSelectedPlate(newDestPlate);
        }

        // AI
        // Add this event to your MainViewModel class
        public event EventHandler MultiSelectionReset;

        // Then modify the CancelAddPlate method
        [RelayCommand]
        private void CancelAddPlate()
        {
            if (IsInSourceAddMode)
            {
                IsInSourceAddMode = false;
                StatusText += "Canceled adding source plate.\n";

                // Reset selections
                foreach (var destPlate in DestinationPlates)
                {
                    destPlate.IsSelected = false;
                    destPlate.SelectionColor = null;
                }
            }

            if (IsInDestAddMode)
            {
                IsInDestAddMode = false;
                StatusText += "Canceled adding destination plate.\n";

                // Reset selections
                foreach (var sourcePlate in SourcePlates)
                {
                    sourcePlate.IsSelected = false;
                    sourcePlate.SelectionColor = null;
                }
            }

            // Raise the event to notify MainWindow to reset its multi-selection state
            MultiSelectionReset?.Invoke(this, EventArgs.Empty);

            // Notify that plate visuals need to be updated
            PlateVisualsNeedUpdate?.Invoke(this, EventArgs.Empty);
        }

        //AI
        [RelayCommand]
        private void DeletePlate(object parameter)
        {
            if (parameter is SourcePlate sourcePlate)
            {
                // First, remove references from destination plates
                foreach (var destPlate in DestinationPlates.ToList())
                {
                    destPlate.SourcePlates.Remove(sourcePlate.ID);
                }

                // Remove the plate from the carousel if it exists there
                if (_carousel != null)
                {
                    try
                    {
                        // Try to find the plate in any stacker
                        bool found = false;
                        for (int i = 0; i < _carousel.Stackers.Count; i++)
                        {
                            // Check if the plate exists in this stacker's Plates collection
                            if (_carousel.Stackers[i].Plates.Any(p => p.ID == sourcePlate.ID))
                            {
                                // Update the plate's Stack property to match where it actually is
                                sourcePlate.Stack = i + 1;
                                _carousel.RemovePlate(sourcePlate);
                                found = true;
                                break;
                            }
                        }

                        if (!found)
                        {
                            // If not found in any stacker, skip carousel removal
                            StatusText += $"Note: Plate {sourcePlate.ID} was not found in carousel stackers\n";
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log exception but continue with deletion
                        StatusText += $"Warning: {ex.Message} (Carousel removal)\n";
                    }
                }

                // Then remove the plate itself
                SourcePlates.Remove(sourcePlate);
                StatusText += $"Deleted source plate {sourcePlate.ID}\n";
            }
            else if (parameter is DestinationPlate destPlate)
            {
                // Remove the plate from carousel if it exists there
                if (_carousel != null)
                {
                    try
                    {
                        // Try to find the plate in any stacker
                        bool found = false;
                        for (int i = 0; i < _carousel.Stackers.Count; i++)
                        {
                            // Check if the plate exists in this stacker's Plates collection
                            if (_carousel.Stackers[i].Plates.Any(p => p.ID == destPlate.ID))
                            {
                                // Update the plate's Stack property to match where it actually is
                                destPlate.Stack = i + 1;
                                _carousel.RemovePlate(destPlate);
                                found = true;
                                break;
                            }
                        }

                        if (!found)
                        {
                            // If not found in any stacker, skip carousel removal
                            StatusText += $"Note: Plate {destPlate.ID} was not found in carousel stackers\n";
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log exception but continue with deletion
                        StatusText += $"Warning: {ex.Message} (Carousel removal)\n";
                    }
                }

                // Remove the destination plate
                DestinationPlates.Remove(destPlate);
                StatusText += $"Deleted destination plate {destPlate.ID}\n";
            }

            // Clear any selections
            ClearAllPlateSelections();

            // Update UI collections
            UpdateSourceItemsCollection(new AddButtonViewModel("Source", new RelayCommand<string>(_ => StartAddSourcePlate())));
            UpdateDestinationItemsCollection(new AddButtonViewModel("Destination", new RelayCommand<string>(_ => StartAddDestinationPlate())));

            // Trigger visual update for the stacker
            PlateVisualsNeedUpdate?.Invoke(this, EventArgs.Empty);
        }

        //AI
        [RelayCommand]
        private void ClearAllPlates()
        {
            // Ask for confirmation
            //MessageBoxResult result = MessageBox.Show("Are you sure you want to delete all plates?",
            //                                         "Confirmation",
            //                                         MessageBoxButton.YesNo,
            //                                         MessageBoxImage.Question);
            MessageBoxResult result = MessageBoxResult.Yes;
            if (result == MessageBoxResult.Yes)
            {
                // Clear carousel if it exists
                if (_carousel != null)
                {
                    try
                    {
                        // Use the built-in method to remove all plates
                        _carousel.RemoveAllPlates();
                    }
                    catch (Exception ex)
                    {
                        // Log exception but continue with clearing
                        StatusText += $"Warning: {ex.Message} (Carousel clearing)\n";
                    }
                }

                // Clear all plates
                SourcePlates.Clear();
                DestinationPlates.Clear();

                // Update the collections with add buttons
                UpdateSourceItemsCollection(new AddButtonViewModel("Source", new RelayCommand<string>(_ => StartAddSourcePlate())));
                UpdateDestinationItemsCollection(new AddButtonViewModel("Destination", new RelayCommand<string>(_ => StartAddDestinationPlate())));

                // Trigger visual update for the stacker
                PlateVisualsNeedUpdate?.Invoke(this, EventArgs.Empty);

                StatusText += "All plates cleared.\n";
            }
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
            JournalID = "testJournal8"
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

            // Select the last created source plate if any were created
            if (SourcesToAdd > 0)
            {
                var lastSourcePlate = SourcePlates.LastOrDefault();
                if (lastSourcePlate != null)
                {
                    HighlightSelectedPlate(lastSourcePlate);
                }
            }
            // If no source plates were created but destination plates were, select the last destination plate
            else if (DestinationPlates.Count > 0)
            {
                var lastDestPlate = DestinationPlates.LastOrDefault();
                if (lastDestPlate != null)
                {
                    HighlightSelectedPlate(lastDestPlate);
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
                    JournalID = "testJournal8",
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
            string currentJournalID = "testJournal8"; // Replace with actual journal ID
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
            var lastRunState = _runLogger.LoadRunState("testJournal8"); // TODO: Replace with actual journal ID
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

        // Add these properties to MainViewModel class
        [ObservableProperty]
        private SourcePlate _currentlySelectedSourcePlate;

        [ObservableProperty]
        private DestinationPlate _currentlySelectedDestinationPlate;

        // These methods handle the visualization of selected plates
        // Add this to MainViewModel class
        public event EventHandler PlateVisualsNeedUpdate;

        // AI
        public void HighlightSelectedPlate(Plate selectedPlate)
        {
            // Clear previous selections first
            ClearAllPlateSelections();

            if (selectedPlate is SourcePlate sourcePlate)
            {
                // Set this plate as selected with primary color
                CurrentlySelectedSourcePlate = sourcePlate;
                sourcePlate.IsSelected = true;
                sourcePlate.SelectionColor = "Primary";

                // Clear any previously selected destination plate
                CurrentlySelectedDestinationPlate = null;

                // Highlight linked destination plates with secondary color
                foreach (var linkedDestPlate in DestinationPlates)
                {
                    if (linkedDestPlate.SourcePlates.Any(sp => sp.Key == sourcePlate.ID))
                    {
                        linkedDestPlate.IsSelected = true;
                        linkedDestPlate.SelectionColor = "Secondary";
                    }
                }
            }
            else if (selectedPlate is DestinationPlate destPlate)
            {
                // Set this plate as selected with primary color
                CurrentlySelectedDestinationPlate = destPlate;
                destPlate.IsSelected = true;
                destPlate.SelectionColor = "Primary";

                // Clear any previously selected source plate
                CurrentlySelectedSourcePlate = null;

                // Highlight linked source plates with secondary color
                foreach (var linkedSourcePlate in SourcePlates)
                {
                    if (destPlate.SourcePlates.Any(sp => sp.Key == linkedSourcePlate.ID))
                    {
                        linkedSourcePlate.IsSelected = true;
                        linkedSourcePlate.SelectionColor = "Secondary";
                    }
                }
            }

            // Trigger UI update
            OnPropertyChanged(nameof(SourcePlates));
            OnPropertyChanged(nameof(DestinationPlates));

            // Notify that plate visuals need to be updated
            PlateVisualsNeedUpdate?.Invoke(this, EventArgs.Empty);
        }

        public void ClearAllPlateSelections()
        {
            foreach (var plate in SourcePlates)
            {
                plate.IsSelected = false;
                plate.SelectionColor = null;
            }

            foreach (var plate in DestinationPlates)
            {
                plate.IsSelected = false;
                plate.SelectionColor = null;
            }

            CurrentlySelectedSourcePlate = null;
            CurrentlySelectedDestinationPlate = null;

            // Notify that plate visuals need to be updated
            PlateVisualsNeedUpdate?.Invoke(this, EventArgs.Empty);
        }

        //AI
        public void TogglePlateSelection(Plate plate)
        {
            if (IsInSourceAddMode && plate is DestinationPlate destPlate)
            {
                // In source add mode, toggle destination plate selection
                destPlate.IsSelected = !destPlate.IsSelected;
                destPlate.SelectionColor = destPlate.IsSelected ? "Secondary" : null;
                OnPropertyChanged(nameof(DestinationPlates));
                PlateVisualsNeedUpdate?.Invoke(this, EventArgs.Empty);
            }
            else if (IsInDestAddMode && plate is SourcePlate sourcePlate)
            {
                // In destination add mode, toggle source plate selection
                sourcePlate.IsSelected = !sourcePlate.IsSelected;
                sourcePlate.SelectionColor = sourcePlate.IsSelected ? "Secondary" : null;
                OnPropertyChanged(nameof(SourcePlates));
                PlateVisualsNeedUpdate?.Invoke(this, EventArgs.Empty);
            }
            else if (!IsInSourceAddMode && !IsInDestAddMode)
            {
                // Normal mode, use normal highlighting
                HighlightSelectedPlate(plate);
            }
        }

        partial void OnSelectedSourcePlateChanged(Plate value)
        {
            if (value != null)
            {
                HighlightSelectedPlate(value);
            }
        }

        partial void OnSelectedDestinationPlateChanged(Plate value)
        {
            if (value != null)
            {
                HighlightSelectedPlate(value);
            }
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