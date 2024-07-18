using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Integration;
using static Integration.InstrumentEvents;
using static Integration.RunLogger;

namespace PinTransferWPF
{
    public partial class MainWindow : Window
    {
        private readonly InstrumentEvents _events;
        private readonly JournalParser _parser;
        private readonly CommandRunner _epsonRunner;
        private readonly CommandRunner _kx2Runner;
        private readonly RunLogger _runLogger;
        private CancellationTokenSource _cts;

        public MainWindow()
        {
            InitializeComponent();
            string connectionString = "Data Source=" + Parameters.LoggingDatabase;
            _events = new InstrumentEvents();
            _parser = new JournalParser(connectionString, _events);
            _runLogger = new RunLogger(connectionString);
            _epsonRunner = new CommandRunner(_parser, "Epson", _runLogger, _events);
            _kx2Runner = new CommandRunner(_parser, "KX2", _runLogger, _events);

            SetupEventHandlers();
            CheckForUnfinishedRun();
        }

        private void CheckForUnfinishedRun()
        {
            var lastUnfinishedRunState = _runLogger.LoadMostRecentUnfinishedRunState();
            if (lastUnfinishedRunState != null)
            {
                var result = MessageBox.Show($"An unfinished run was detected (Journal ID: {lastUnfinishedRunState.JournalID}). Do you want to resume?", "Resume Run", MessageBoxButton.YesNo);
                if (result == MessageBoxResult.Yes)
                {
                    ResumeRun(lastUnfinishedRunState);
                }
            }
        }

        private async void ResumeRun(RunState runState)
        { 
            _events.DeserializeToolStates(runState.SerializedToolStates);
            _events.DeserializeArmStates(runState.SerializedArmStates);
            _events.DeserializeCarouselStates(runState.SerializedCarouselStates);

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
                AppendStatus("All commands completed successfully.");
                _runLogger.MarkRunAsCompleted(runState.JournalID);
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
            // Epson
            _events.OnToolAttached += async (toolId, ct) =>
            {
                AppendStatus($"Tool {toolId} attached");
                await Task.Delay(1000, ct);
            };

            _events.OnToolDetached += async (toolId, ct) =>
            {
                AppendStatus($"Tool {toolId} detached");
                await Task.Delay(1000, ct);
            };

            _events.OnWashCompleted += async (toolId, ct) =>
            {
                AppendStatus($"Wash for Tool {toolId}");
                await Task.Delay(2000, ct);
            };

            _events.OnTransferCompleted += async (toolId, ct) =>
            {
                AppendStatus($"Transfer for Tool {toolId}");
                await Task.Delay(1500, ct);
            };

            _events.OnToolSafe += async (toolId, ct) =>
            {
                AppendStatus($"Safe Move for Tool {toolId}");
                await Task.Delay(1000, ct);
            };

            //KX2
            _events.OnArmSafe += async (ct) =>
            {
                AppendStatus($"Safe Move for Arm");
                await Task.Delay(1000, ct);
            };

            _events.OnPlateGrabbedFromHotel += async (plateType, ct) =>
            {
                AppendStatus($"{plateType} grabbed from hotel");
                await Task.Delay(2000, ct);
            };

            _events.OnPlateGrabbedFromStage += async (plateType, ct) =>
            {
                AppendStatus($"{plateType} grabbed from stage");
                await Task.Delay(2000, ct);
            };

            _events.OnPlatePlacedToHotel += async (plateType, ct) =>
            {
                AppendStatus($"{plateType} placed to hotel");
                await Task.Delay(2000, ct);
            };

            _events.OnPlatePlacedToStage += async (plateType, ct) =>
            {
                AppendStatus($"{plateType} placed to stage");
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
                List<Plate> SourcePlates = new List<Plate>();
                List<Plate> DestinationPlates = new List<Plate>();
                for (int i = 1; i < 3; i++)
                {
                    SourcePlates.Add(new SourcePlate
                    {
                        ID = i.ToString(),
                        Status = new Dictionary<string, bool>()
                        {
                            { "pinned", false }
                        },
                        replicates = new Tuple<int, int>(100, 2)
                    });
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
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void RunCommandsButton_Click(object sender, RoutedEventArgs e)
        {
            _events.ResetEvents();
            RunCommandsButton.IsEnabled = false;
            CancelButton.IsEnabled = true;
            StatusTextBlock.Text = string.Empty;
            AppendStatus("Running commands...");

            _cts = new CancellationTokenSource();

            string currentJournalID = "testJournal"; // Replace with actual journal ID

            try
            {
                await Task.WhenAll(
                    _epsonRunner.RunCommandsAsync(currentJournalID, 1, _cts.Token),
                    _kx2Runner.RunCommandsAsync(currentJournalID, 1, _cts.Token)
                );
                AppendStatus("All commands completed successfully.");
                _runLogger.MarkRunAsCompleted(currentJournalID);
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