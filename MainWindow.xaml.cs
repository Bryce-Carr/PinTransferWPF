using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Integration;
using static Integration.InstrumentEvents;
using static Integration.RunLogger;

namespace PinTransferWPF
{
    using StackType = HotelStacker;
    public partial class MainWindow : Window
    {
        private readonly InstrumentEvents _events;
        private JournalParser<StackType> _parser;
        private CommandRunner<StackType> _epsonRunner;
        private CommandRunner<StackType> _kx2Runner;
        private RunLogger _runLogger;
        private readonly Carousel<StackType> _carousel;
        private CancellationTokenSource _cts;
        private int _numStacks;
        private int _stackCapacity;

        public MainWindow()
        {
            InitializeComponent();
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

            SetupEventHandlers();
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
            _events.OnCarouselRotated += async (stacker, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"Carousel rotated to {stacker}");
                await Task.Delay(2000, ct);
            };
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

        private void tools_Clicked(object sender, RoutedEventArgs e)
        {
            toolsComboBox.IsDropDownOpen = !toolsComboBox.IsDropDownOpen;
        }

        private void btnCreateRun_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                List<SourcePlate> SourcePlates = new List<SourcePlate>();
                List<DestinationPlate> DestinationPlates = new List<DestinationPlate>();
                for (int sourceCount = 1; sourceCount < 3; sourceCount++)
                {
                    SourcePlates.Add(new SourcePlate
                    {
                        ID = "source_" + sourceCount.ToString(),
                        Stack = Math.Abs(sourceCount - 1) / _stackCapacity + 1,
                        FinalStack = Math.Abs(sourceCount - 1) / _stackCapacity + 1,
                        PositionInStack = sourceCount,
                        // TODO add logic for this for sequential stackers
                        FinalPositionInStack = sourceCount,
                        Status = new Dictionary<string, bool>()
                        {
                            { "pinned", false }
                        },
                        Replicates = new Tuple<int, int>(100, 2)
                    });
                }
                foreach (var sourcePlate in SourcePlates)
                {
                    for(int replicate = 1; replicate <= sourcePlate.Replicates.Item2; replicate++)
                    {
                        int stackNum;
                        DestinationPlates.Add(new DestinationPlate
                        {
                            ID = "destination_" + (DestinationPlates.Count() + 1).ToString(),
                            Stack = stackNum = _numStacks - ((Math.Abs(DestinationPlates.Count - 1) / _stackCapacity)),
                            FinalStack = stackNum = _numStacks - ((Math.Abs(DestinationPlates.Count - 1) / _stackCapacity)),
                            PositionInStack = DestinationPlates.Count() + 1 - ((_numStacks - stackNum) * _stackCapacity),
                            FinalPositionInStack = DestinationPlates.Count() + 1 - ((_numStacks - stackNum) * _stackCapacity),
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
            string currentJournalID = "testJournal"; // Replace with actual journal ID
            _events.ResetEvents();
            string serializedPlates = _runLogger.LoadRunState(currentJournalID).InitialPlates;
            List<Plate> deserializedPlates = PlateSerializer.DeserializePlates(serializedPlates);
            _events._plates = deserializedPlates;
            PopulateCarousel(deserializedPlates);

            RunCommandsButton.IsEnabled = false;
            CancelButton.IsEnabled = true;
            StatusTextBlock.Text = string.Empty;
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
                RunCommandsButton.IsEnabled = true;
                CancelButton.IsEnabled = false;
            }
        }

        private void ResumeButton_Click( object sender, RoutedEventArgs e)
        {
            var lastRunState = _runLogger.LoadRunState("testJournal"); // TODO: Replace with actual journal ID
            ResumeRun(lastRunState);
        }
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            CancelButton.IsEnabled = false;
            StatusTextBlock.Text += "Cancelling...";
        }
    }
}