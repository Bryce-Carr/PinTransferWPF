using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Integration;
using static Integration.RunLogger;

namespace PinTransferWPF
{
    public partial class MainWindow : Window
    {
        private readonly InstrumentEvents _events;
        private readonly JournalParser _parser;
        private readonly CommandRunner _runner;
        private CancellationTokenSource _cts;

        public MainWindow()
        {
            InitializeComponent();
            string connectionString = "Data Source=" + Parameters.LoggingDatabase;
            _events = new InstrumentEvents();
            _parser = new JournalParser(connectionString, _events);
            _runner = new CommandRunner(_parser, "Epson");

            SetupEventHandlers();
        }

        private void SetupEventHandlers()
        {
            _events.OnToolAttached += async (toolId, ct) =>
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    StatusTextBlock.Text = $"Tool {toolId} attached";
                });
                await Task.Delay(1000, ct);
            };

            _events.OnToolDetached += async (toolId, ct) =>
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    StatusTextBlock.Text = $"Tool {toolId} detached";
                });
                await Task.Delay(1000, ct);
            };

            _events.OnWashCompleted += async (toolId, ct) =>
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    StatusTextBlock.Text = $"Wash completed for Tool {toolId}";
                });
                await Task.Delay(2000, ct);
            };

            _events.OnTransferCompleted += async (toolId, ct) =>
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    StatusTextBlock.Text = $"Transfer completed for Tool {toolId}";
                });
                await Task.Delay(1500, ct);
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
            RunCommandsButton.IsEnabled = false;
            CancelButton.IsEnabled = true;
            StatusTextBlock.Text = "Running commands...";

            _cts = new CancellationTokenSource();

            try
            {
                await _runner.RunCommandsAsync("testJournal", _cts.Token);
                StatusTextBlock.Text = "All commands completed successfully.";
            }
            catch (OperationCanceledException)
            {
                StatusTextBlock.Text = "Command execution was cancelled.";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"An error occurred: {ex.Message}";
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