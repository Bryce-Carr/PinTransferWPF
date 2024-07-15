using System;
using System.Collections.Generic;
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
        private CancellationTokenSource _cts;

        public MainWindow()
        {
            InitializeComponent();
            string connectionString = "Data Source=" + Parameters.LoggingDatabase;
            _events = new InstrumentEvents();
            _parser = new JournalParser(connectionString, _events);
            _epsonRunner = new CommandRunner(_parser, "Epson");
            _kx2Runner = new CommandRunner(_parser, "KX2");

            SetupEventHandlers();
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
                RunLogger runLogger = new RunLogger();
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
                runLogger.CreateJournal(journalInfo);
                runLogger.CreateRun(runInfo);
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
            StatusTextBlock.Text = string.Empty; // Clear previous text
            AppendStatus("Running commands...");

            _cts = new CancellationTokenSource();

            try
            {
                await Task.WhenAll(
                    _epsonRunner.RunCommandsAsync("testJournal", 1, _cts.Token),
                    _kx2Runner.RunCommandsAsync("testJournal", 1, _cts.Token)
                );
                AppendStatus("All commands completed successfully.");
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

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            CancelButton.IsEnabled = false;
            StatusTextBlock.Text = "Cancelling...";
        }
    }
}