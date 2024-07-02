using PinTransferWPF;
using System;
using System.Data.SQLite;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.ComponentModel;
using RCAPINet;
using static Integration.InstrumentEvents;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace Integration
{
    public class InstrumentEvents
    {
        public delegate Task EpsonEventHandler(string toolId, CancellationToken cancellationToken);

        public event EpsonEventHandler OnToolAttached;
        public event EpsonEventHandler OnToolDetached;
        public event EpsonEventHandler OnWashCompleted;
        public event EpsonEventHandler OnTransferCompleted;

        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, bool>> _toolStates = new ConcurrentDictionary<string, ConcurrentDictionary<string, bool>>();
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _eventSemaphores = new ConcurrentDictionary<string, SemaphoreSlim>();

        public string SerializeToolStates()
        {
            return JsonSerializer.Serialize(_toolStates);
        }

        public void RestoreToolStates(string serializedToolStates)
        {
            var restoredStates = JsonSerializer.Deserialize<ConcurrentDictionary<string, ConcurrentDictionary<string, bool>>>(serializedToolStates);
            if (restoredStates != null)
            {
                foreach (var kvp in restoredStates)
                {
                    string toolId = kvp.Key;
                    ConcurrentDictionary<string, bool> states = kvp.Value;
                    _toolStates[toolId] = states;
                }
            }
        }

        public async Task RaiseToolAttached(string toolId, CancellationToken cancellationToken)
        {
            if (OnToolAttached != null)
            {
                await OnToolAttached(toolId, cancellationToken);
                SetToolState(toolId, "Attached", true);
            }
        }

        public async Task RaiseToolDetached(string toolId, CancellationToken cancellationToken)
        {
            if (OnToolDetached != null)
            {
                await OnToolDetached(toolId, cancellationToken);
                SetToolState(toolId, "Attached", false);
            }
        }

        public async Task RaiseWashCompleted(string toolId, CancellationToken cancellationToken)
        {
            if (OnWashCompleted != null)
            {
                await OnWashCompleted(toolId, cancellationToken);
                SetToolState(toolId, "Washed", true);
            }
        }

        public async Task RaiseTransferCompleted(string toolId, CancellationToken cancellationToken)
        {
            if (OnTransferCompleted != null)
            {
                await OnTransferCompleted(toolId, cancellationToken);
                SetToolState(toolId, "Transferred", true);
            }
        }

        public async Task WaitForToolState(string toolId, string state, bool expectedValue, CancellationToken cancellationToken)
        {
            var toolStates = _toolStates.GetOrAdd(toolId, _ => new ConcurrentDictionary<string, bool>());

            if (toolStates.TryGetValue(state, out bool currentValue) && currentValue == expectedValue)
            {
                return; // Tool is already in the expected state
            }

            var semaphoreKey = $"{toolId}_{state}_{expectedValue}";
            var semaphore = _eventSemaphores.GetOrAdd(semaphoreKey, _ => new SemaphoreSlim(0, 1));
            await semaphore.WaitAsync(cancellationToken);
        }

        private void SetToolState(string toolId, string state, bool value)
        {
            var toolStates = _toolStates.GetOrAdd(toolId, _ => new ConcurrentDictionary<string, bool>());
            toolStates[state] = value;

            var semaphoreKey = $"{toolId}_{state}_{value}";
            if (_eventSemaphores.TryGetValue(semaphoreKey, out var semaphore))
            {
                semaphore.Release();
            }
        }

        public void ResetEvents()
        {
            _toolStates.Clear();
            foreach (var semaphore in _eventSemaphores.Values)
            {
                semaphore.Dispose();
            }
            _eventSemaphores.Clear();
        }
    }

    public class ProgressEventArgs : EventArgs
    {
        public int ProgressPercentage { get; }
        public string Status { get; }

        public ProgressEventArgs(int progressPercentage, string status)
        {
            ProgressPercentage = progressPercentage;
            Status = status;
        }
    }

    public class JournalParser
    {
        private readonly string _connectionString;
        private readonly InstrumentEvents _events;
        public JournalParser(string connectionString, InstrumentEvents events)
        {
            _connectionString = connectionString;
            _events = events;
        }
        public string GetNextCommand(string journalID, int commandID, string instrument)
        {
            string commandString;
            // Create a new SQLite connection
            using (var _connection = new SQLiteConnection("Data Source=" + Parameters.LoggingDatabase))
            {
                _connection.Open();
                using (var command = new SQLiteCommand("SELECT COMMAND FROM InstrumentCommands WHERE JournalID = '@JournalID' AND CommandID = '@CommandID' AND Instrument = '@Instrument'", _connection))
                {
                    command.Parameters.AddWithValue("@JournalID", journalID);
                    command.Parameters.AddWithValue("@CommandID", commandID);
                    command.Parameters.AddWithValue("@Instrument", instrument);
                    using (var reader = command.ExecuteReader())
                    {
                        reader.Read();
                        commandString = reader.GetString(0);
                        return commandString;
                    }
                }
            }
        }

        public async Task<string> GetNextCommandAsync(string journalID, int commandID, string instrument)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var command = new SQLiteCommand("SELECT COMMAND FROM InstrumentCommands WHERE JournalID = @JournalID AND CommandID = @CommandID AND Instrument = @Instrument", connection))
                {
                    command.Parameters.AddWithValue("@JournalID", journalID);
                    command.Parameters.AddWithValue("@CommandID", commandID);
                    command.Parameters.AddWithValue("@Instrument", instrument);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            return reader.GetString(0);
                        }
                        return null;
                    }
                }
            }
        }

        public async Task RunNextCommandAsync(string instrument, string commandString, CancellationToken ct)
        {
            switch (instrument)
            {
                case "Epson":
                    await RunEpsonCommandAsync(commandString, ct);
                    break;
                case "KX2":
                    //await RunKX2CommandAsync(commandString, ct);
                    break;
                default:
                    Console.WriteLine("Instrument not supported");
                    break;
            }
        }

        private async Task RunEpsonCommandAsync(string commandString, CancellationToken ct)
        {
            string toolId = ExtractToolId(commandString);

            if (commandString.StartsWith("Attach"))
            {
                await _events.WaitForToolState("33", "Detached", true, ct);
                await _events.WaitForToolState("100", "Detached", true, ct);
                await _events.WaitForToolState("300", "Detached", true, ct);
                await _events.RaiseToolAttached(toolId, ct);
            }
            else if (commandString.StartsWith("Detach"))
            {
                await _events.WaitForToolState(toolId, "Attached", true, ct);
                await _events.RaiseToolDetached(toolId, ct);
            }
            else if (commandString.StartsWith("Wash"))
            {
                await _events.WaitForToolState(toolId, "Attached", true, ct);
                await _events.RaiseWashCompleted(toolId, ct);
            }
            else if (commandString.StartsWith("Transfer"))
            {
                await _events.WaitForToolState(toolId, "Washed", true, ct);
                await _events.RaiseTransferCompleted(toolId, ct);
            }
            else
            {
                throw new ArgumentException($"Unknown command: {commandString}");
            }
        }

        private string ExtractToolId(string commandString)
        {
            // Use a regular expression to match the pattern "Tool" followed by one or more digits
            Match match = Regex.Match(commandString, @"(\d+)");

            if (match.Success)
            {
                // Return the matched tool number
                return match.Groups[1].Value;
            }
            else
            {
                // If no match is found, throw an exception or handle the error as appropriate for your application
                throw new ArgumentException($"Unable to extract Tool ID from command: {commandString}");
            }
        }
    }
    public class CommandRunner
    {
        private readonly JournalParser _parser;
        private readonly string _instrument;

        public CommandRunner(JournalParser parser, string instrument)
        {
            _parser = parser;
            _instrument = instrument;
        }

        public async Task RunCommandsAsync(string journalID, CancellationToken ct)
        {
            int commandID = 1;
            while (!ct.IsCancellationRequested)
            {
                string command = await _parser.GetNextCommandAsync(journalID, commandID, _instrument);
                if (string.IsNullOrEmpty(command))
                {
                    break; // No more commands
                }

                await _parser.RunNextCommandAsync(_instrument, command, ct);
                commandID++;
            }
        }
    }
}