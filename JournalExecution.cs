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
using System.Windows.Controls;
using System.Collections.Generic;

namespace Integration
{
    public class InstrumentEvents
    {
        public delegate Task EpsonEventHandler(string toolId, CancellationToken cancellationToken);

        public event EpsonEventHandler OnToolAttached;
        public event EpsonEventHandler OnToolDetached;
        public event EpsonEventHandler OnWashCompleted;
        public event EpsonEventHandler OnTransferCompleted;
        public event EpsonEventHandler OnToolSafe;

        public async Task RaiseToolAttached(string toolId, CancellationToken cancellationToken)
        {
            if (OnToolAttached != null)
            {
                //TODO: what if pause happens here? need to re set all toolid states attached to false..
                await OnToolAttached(toolId, cancellationToken);
                SetToolState(toolId, "attached", true);
                SetToolState(toolId, "safe", true);
            }
        }

        public async Task RaiseToolDetached(string toolId, CancellationToken cancellationToken)
        {
            if (OnToolDetached != null)
            {
                await OnToolDetached(toolId, cancellationToken);
                SetToolState(toolId, "attached", false);
            }
        }

        public async Task RaiseWashCompleted(string toolId, CancellationToken cancellationToken)
        {
            if (OnWashCompleted != null)
            {
                await OnWashCompleted(toolId, cancellationToken);
                SetToolState(toolId, "washed", true);
            }
        }

        public async Task RaiseTransferCompleted(string toolId, CancellationToken cancellationToken)
        {
            if (OnTransferCompleted != null)
            {
                SetToolState(toolId, "safe", false);
                await OnTransferCompleted(toolId, cancellationToken);
                SetToolState(toolId, "transferred", true);
                SetToolState(toolId, "washed", false);
            }
        }

        public async Task RaiseToolSafe(string toolId, CancellationToken cancellationToken)
        {
            if (OnToolSafe != null)
            {
                await OnToolSafe(toolId, cancellationToken);
                SetToolState(toolId, "safe", true);
            }
        }


        public delegate Task KX2PlateEventHandler(string plateType, CancellationToken cancellationToken);
        public delegate Task KX2EventHandler(CancellationToken cancellationToken);

        public event KX2PlateEventHandler OnPlateGrabbedFromHotel;
        public event KX2PlateEventHandler OnPlatePlacedToStage;
        public event KX2PlateEventHandler OnPlateGrabbedFromStage;
        public event KX2PlateEventHandler OnPlatePlacedToHotel;
        public event KX2EventHandler OnArmSafe;
        
        public async Task RaisePlateGrabbedFromHotel(string plateType, CancellationToken cancellationToken)
        {
            if (OnArmSafe != null)
            {
                await OnPlateGrabbedFromHotel(plateType, cancellationToken);
                SetArmState("plate_gripped", true);
            }
        }
        public async Task RaisePlatePlacedToStage(string plateType, CancellationToken cancellationToken)
        {
            if (OnArmSafe != null)
            {
                SetArmState("safe", false);
                await OnPlatePlacedToStage(plateType, cancellationToken);
                SetArmState("plate_gripped", false);
            }
        }
        public async Task RaisePlateGrabbedFromStage(string plateType, CancellationToken cancellationToken)
        {
            if (OnArmSafe != null)
            {
                SetArmState("safe", false);
                await OnPlateGrabbedFromStage(plateType, cancellationToken);
                SetArmState("plate_gripped", true);
            }
        }
        public async Task RaisePlatePlacedToHotel(string plateType, CancellationToken cancellationToken)
        {
            if (OnArmSafe != null)
            {
                await OnPlatePlacedToHotel(plateType, cancellationToken);
                SetArmState("plate_gripped", false);
            }
        }
        public async Task RaiseArmSafe(CancellationToken cancellationToken)
        {
            if (OnArmSafe != null)
            {
                await OnArmSafe(cancellationToken);
                SetArmState("safe", true);
            }
        }

        public delegate Task CarouselEventHandler(CancellationToken cancellationToken);
        public event CarouselEventHandler OnMoved;

        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, bool>> _toolStates = new ConcurrentDictionary<string, ConcurrentDictionary<string, bool>>();
        private readonly ConcurrentDictionary<string, bool> _armStates = new ConcurrentDictionary<string, bool>();
        private readonly ConcurrentDictionary<string, bool> _carouselStates = new ConcurrentDictionary<string, bool>();
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _eventSemaphores = new ConcurrentDictionary<string, SemaphoreSlim>();

        public string SerializeToolStates()
        {
            return JsonSerializer.Serialize(_toolStates);
        }

        public void DeserializeToolStates(string serializedToolStates)
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

        public async Task WaitForArmState(string state, bool expectedValue, CancellationToken cancellationToken)
        {
            var armState = _armStates.GetOrAdd(state, expectedValue);

            if (armState == expectedValue)
            {
                return; // Tool is already in the expected state
            }

            var semaphoreKey = $"arm_{state}_{expectedValue}";
            var semaphore = _eventSemaphores.GetOrAdd(semaphoreKey, _ => new SemaphoreSlim(0, 1));
            await semaphore.WaitAsync(cancellationToken);
        }

        private void SetArmState(string state, bool value)
        {
            var armState = _armStates.GetOrAdd(state, value);

            var semaphoreKey = $"arm_{state}_{value}";
            if (_eventSemaphores.TryGetValue(semaphoreKey, out var semaphore))
            {
                semaphore.Release();
            }
        }

        public async Task WaitForCarouselState(string state, bool expectedValue, CancellationToken cancellationToken)
        {
            var carouselState = _carouselStates.GetOrAdd(state, expectedValue);

            if (carouselState == expectedValue)
            {
                return; // Tool is already in the expected state
            }

            var semaphoreKey = $"carousel_{state}_{expectedValue}";
            var semaphore = _eventSemaphores.GetOrAdd(semaphoreKey, _ => new SemaphoreSlim(0, 1));
            await semaphore.WaitAsync(cancellationToken);
        }

        private void SetCarouselState(string state, bool value)
        {
            var carouselState = _carouselStates.GetOrAdd(state, value);

            var semaphoreKey = $"carousel_{state}_{value}";
            if (_eventSemaphores.TryGetValue(semaphoreKey, out var semaphore))
            {
                semaphore.Release();
            }
        }

        public void ResetEvents()
        {
            // Sets all states according to a fresh, ready-to-run pin transfer configuration
            _toolStates.Clear();
            _armStates.Clear();
            _carouselStates.Clear();
            foreach (var semaphore in _eventSemaphores.Values)
            {
                semaphore.Dispose();
            }
            _eventSemaphores.Clear();

            SetArmState("plate_gripped", false);
            SetArmState("safe", false);
            SetToolState("33", "attached", false);
            SetToolState("96", "attached", false);
            SetToolState("100", "attached", false);
            SetToolState("300", "attached", false);
            SetToolState("33", "washed", false);
            SetToolState("96", "washed", false);
            SetToolState("100", "washed", false);
            SetToolState("300", "washed", false);
            SetToolState("33", "safe", false);
            SetToolState("96", "safe", false);
            SetToolState("100", "safe", false);
            SetToolState("300", "safe", false);
            SetCarouselState("safe", true);
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
                    await RunKX2CommandAsync(commandString, ct);
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
                await _events.WaitForToolState("33", "attached", false, ct);
                await _events.WaitForToolState("100", "attached", false, ct);
                await _events.WaitForToolState("300", "attached", false, ct);
                await _events.WaitForToolState("96", "attached", false, ct);
                await _events.RaiseToolAttached(toolId, ct);
            }
            else if (commandString.StartsWith("Detach"))
            {
                await _events.WaitForToolState(toolId, "attached", true, ct);
                await _events.RaiseToolDetached(toolId, ct);
            }
            else if (commandString.StartsWith("Wash"))
            {
                await _events.WaitForToolState(toolId, "attached", true, ct);
                await _events.RaiseWashCompleted(toolId, ct);
            }
            else if (commandString.StartsWith("Transfer"))
            {
                await _events.WaitForToolState(toolId, "washed", true, ct);
                await _events.WaitForArmState("safe", true, ct);
                await _events.RaiseTransferCompleted(toolId, ct);
            }
            else if (commandString.StartsWith("Move Safe"))
            {
                await _events.WaitForToolState(toolId, "transferred", true, ct);
                await _events.RaiseToolSafe(toolId, ct);
            }
            else
            {
                throw new ArgumentException($"No logic implemented for command: {commandString}");
            }
        }
        public async Task WaitForAnyToolSafe(IEnumerable<string> toolIds, CancellationToken cancellationToken)
        {
            if (toolIds == null || !toolIds.Any())
                throw new ArgumentException("At least one tool ID must be provided.", nameof(toolIds));

            var tasks = toolIds.Select(toolId => _events.WaitForToolState(toolId, "safe", true, cancellationToken));
            await Task.WhenAny(tasks);
        }
        //private async Task RunKX2CommandAsync(string commandString, CancellationToken ct)
        //{
        //    var toolIds = new[] { "tool1", "tool2", "tool3", "tool4" };
        //    if (commandString.StartsWith("Get Source from"))
        //    {
        //        if (commandString.Contains("Stack"))
        //        {
        //            await _events.WaitForCarouselState("safe", true, ct);
        //            await _events.RaisePlateGrabbedFromHotel("source", ct);
        //        }
        //        else if (commandString.Contains("Stage"))
        //        {
        //            await WaitForAnyToolSafe(toolIds, ct);
        //            await _events.RaisePlateGrabbedFromStage("source", ct);
        //        }
        //    }
        //    else if (commandString.StartsWith("Get Destination from"))
        //    {
        //        if (commandString.Contains("Stack"))
        //        {
        //            await _events.WaitForCarouselState("safe", true, ct);
        //            await _events.RaisePlateGrabbedFromHotel("destination", ct);
        //        }
        //        else if (commandString.Contains("Stage"))
        //        {
        //            await WaitForAnyToolSafe(toolIds, ct);
        //            await _events.RaisePlateGrabbedFromStage("destination", ct);
        //        }
        //    }
        //    else if (commandString.StartsWith("Set Source to"))
        //    {
        //        if (commandString.Contains("Stack"))
        //        {
        //            await _events.WaitForCarouselState("safe", true, ct);
        //            await _events.RaisePlatePlacedToHotel("source", ct);
        //        }
        //        else if (commandString.Contains("Stage"))
        //        {
        //            await WaitForAnyToolSafe(toolIds, ct);
        //            await _events.RaisePlatePlacedToHotel("source", ct);
        //        }
        //    }
        //    else if (commandString.StartsWith("Set Destination to"))
        //    {
        //        if (commandString.Contains("Stack"))
        //        {
        //            await _events.WaitForCarouselState("safe", true, ct);
        //            await _events.RaisePlatePlacedToHotel("destination", ct);
        //        }
        //        else if (commandString.Contains("Stage"))
        //        {
        //            await _events.RaisePlatePlacedToHotel("destination", ct);
        //        }
        //    }
        //    else if (commandString.StartsWith("Move Safe"))
        //    {
        //        await _events.RaiseArmSafe(ct);
        //    }
        //}

        private async Task RunKX2CommandAsync(string commandString, CancellationToken ct)
        {
            var toolIds = new[] { "33", "96", "100", "300" };
            var parts = commandString.Split(' ');

            if (parts.Length < 2) return;

            var action = parts[0];

            if (action == "Move" && parts.Length >= 2 && parts[1] == "Safe")
            {
                await _events.RaiseArmSafe(ct);
                return;
            }

            if (parts.Length < 4) return;

            var type = parts[1].ToLower();
            var location = parts[3].ToLower();

            switch (action)
            {
                case "Get":
                    await HandleGetCommand(type, location, toolIds, ct);
                    break;
                case "Set":
                    await HandleSetCommand(type, location, toolIds, ct);
                    break;
            }
        }
        private async Task HandleGetCommand(string type, string location, string[] toolIds, CancellationToken ct)
        {
            if (location == "stack")
            {
                await _events.WaitForCarouselState("safe", true, ct);
                await _events.RaisePlateGrabbedFromHotel(type, ct);
            }
            else if (location == "stage")
            {
                await WaitForAnyToolSafe(toolIds, ct);
                await _events.RaisePlateGrabbedFromStage(type, ct);
            }
        }
        private async Task HandleSetCommand(string type, string location, string[] toolIds, CancellationToken ct)
        {
            if (location == "stack")
            {
                await _events.WaitForCarouselState("safe", true, ct);
                await _events.RaisePlatePlacedToHotel(type, ct);
            }
            else if (location == "stage")
            {
                await WaitForAnyToolSafe(toolIds, ct);
                await _events.RaisePlatePlacedToStage(type, ct);
            }
        }
        private string ExtractToolId(string commandString)
        {
            // Use a regular expression to match the pattern "Tool" followed by one or more digits
            // Assumes that the only numerical values in commandString represent the tool size
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

        public async Task RunCommandsAsync(string journalID, int commandID, CancellationToken ct)
        {
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