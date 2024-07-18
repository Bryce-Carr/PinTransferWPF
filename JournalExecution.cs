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
using System.Data.SqlTypes;
using System.ComponentModel.Design;

namespace Integration
{
    // Extension method for WaitHandle to support async/await
    public static class WaitHandleExtensions
    {
        public static Task WaitOneAsync(this WaitHandle waitHandle, CancellationToken cancellationToken)
        {
            if (waitHandle == null)
                throw new ArgumentNullException(nameof(waitHandle));

            var tcs = new TaskCompletionSource<bool>();
            var rwh = ThreadPool.RegisterWaitForSingleObject(waitHandle,
                (state, timedOut) => ((TaskCompletionSource<bool>)state).TrySetResult(!timedOut),
                tcs, -1, true);

            var cancelRegistration = cancellationToken.Register(() =>
            {
                rwh.Unregister(null);
                tcs.TrySetCanceled(cancellationToken);
            });

            return tcs.Task.ContinueWith(t =>
            {
                cancelRegistration.Dispose();
                return t;
            }, TaskScheduler.Default).Unwrap();
        }
    }

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
                SetArmState("destination_transferred", true);
            }
        }

        public async Task RaiseToolSafe(string toolId, CancellationToken cancellationToken)
        {
            if (OnToolSafe != null)
            {
                await OnToolSafe(toolId, cancellationToken);
                SetToolState(toolId, "safe", true);
                SetToolState(toolId, "transferred", false);
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
                if (plateType == "destination")
                {
                    SetArmState("destination_transferred", false);
                }
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
        private readonly ConcurrentDictionary<string, ManualResetEventSlim> _eventSignals = new ConcurrentDictionary<string, ManualResetEventSlim>();

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
        public string SerializeArmStates()
        {
            return JsonSerializer.Serialize(_armStates);
        }

        public void DeserializeArmStates(string serializedArmStates)
        {
            var restoredStates = JsonSerializer.Deserialize<ConcurrentDictionary<string, bool>>(serializedArmStates);
            if (restoredStates != null)
            {
                foreach (var kvp in restoredStates)
                {
                    string toolId = kvp.Key;
                    bool states = kvp.Value;
                    _armStates[toolId] = states;
                }
            }
        }
        public string SerializeCarouselStates()
        {
            return JsonSerializer.Serialize(_carouselStates);
        }

        public void DeserializeCarouselStates(string serializedCarouselStates)
        {
            var restoredStates = JsonSerializer.Deserialize<ConcurrentDictionary<string, bool>>(serializedCarouselStates);
            if (restoredStates != null)
            {
                foreach (var kvp in restoredStates)
                {
                    string toolId = kvp.Key;
                    bool states = kvp.Value;
                    _carouselStates[toolId] = states;
                }
            }
        }
        public async Task WaitForToolState(string toolId, string state, bool expectedValue, CancellationToken cancellationToken)
        {
            if (_toolStates.TryGetValue(toolId, out var toolStates) &&
               toolStates.TryGetValue(state, out var currentValue) &&
               currentValue == expectedValue)
            {
                return;
            }

            var signalKey = $"{toolId}_{state}_{expectedValue}";
            //ManualResetEventSlim signal;

            //if (!_eventSignals.TryGetValue(signalKey, out signal))
            //{
            //    signal = new ManualResetEventSlim(false);
            //    if (!_eventSignals.TryAdd(signalKey, signal))
            //    {
            //        // If another thread has added a signal, use that one instead
            //        signal.Dispose();
            //        _eventSignals.TryGetValue(signalKey, out signal);
            //    }
            //}

            //try
            //{
            //    await signal.WaitHandle.WaitOneAsync(cancellationToken);
            //}
            //finally
            //{
            //    // If we're the last waiter, remove the signal
            //    if (signal.IsSet)
            //    {
            //        _eventSignals.TryRemove(signalKey, out _);
            //        signal.Dispose();
            //    }
            //}
            var signal = _eventSignals.GetOrAdd(signalKey, _ => new ManualResetEventSlim(false));

            try
            {
                await signal.WaitHandle.WaitOneAsync(cancellationToken);
            }
            finally
            {
                // If we're the last waiter, remove the signal
                if (signal.IsSet)
                {
                    _eventSignals.TryRemove(signalKey, out _);
                    signal.Dispose();
                }
            }
        }

        private void SetToolState(string toolId, string state, bool value)
        {
            _toolStates.AddOrUpdate(toolId,
                _ => new ConcurrentDictionary<string, bool> { [state] = value },
                (_, existingStates) =>
                {
                    existingStates[state] = value;
                    return existingStates;
                });

            var signalKey = $"{toolId}_{state}_{value}";
            if (_eventSignals.TryRemove(signalKey, out var signal))
            {
                signal.Set(); // This releases all waiting threads
                signal.Dispose();
            }
        }

        public async Task WaitForArmState(string state, bool expectedValue, CancellationToken cancellationToken)
        {
            var armState = _armStates.GetOrAdd(state, expectedValue);

            if (armState == expectedValue)
            {
                return; // arm is already in the expected state
            }

            var signalKey = $"arm_{state}_{expectedValue}";
            var signal = _eventSignals.GetOrAdd(signalKey, _ => new ManualResetEventSlim(false));

            try
            {
                await signal.WaitHandle.WaitOneAsync(cancellationToken);
            }
            finally
            {
                if (signal.IsSet)
                {
                    _eventSignals.TryRemove(signalKey, out _);
                    signal.Dispose();
                }
            }
        }

        private void SetArmState(string state, bool value)
        {
            _armStates.AddOrUpdate(state, value, (key, oldValue) => value);

            var signalKey = $"arm_{state}_{value}";
            if (_eventSignals.TryRemove(signalKey, out var signal))
            {
                signal.Set(); // This releases all waiting threads
                signal.Dispose();
            }
        }

        public async Task WaitForCarouselState(string state, bool expectedValue, CancellationToken cancellationToken)
        {
            var carouselState = _carouselStates.GetOrAdd(state, expectedValue);

            if (carouselState == expectedValue)
            {
                return; // Carousel is already in the expected state
            }

            var signalKey = $"carousel_{state}_{expectedValue}";
            var signal = _eventSignals.GetOrAdd(signalKey, _ => new ManualResetEventSlim(false));

            try
            {
                await signal.WaitHandle.WaitOneAsync(cancellationToken);
            }
            finally
            {
                if (signal.IsSet)
                {
                    _eventSignals.TryRemove(signalKey, out _);
                    signal.Dispose();
                }
            }
        }

        private void SetCarouselState(string state, bool value)
        {
            _carouselStates.AddOrUpdate(state, value, (key, oldValue) => value);

            var signalKey = $"carousel_{state}_{value}";
            if (_eventSignals.TryRemove(signalKey, out var signal))
            {
                signal.Set(); // This releases all waiting threads
                signal.Dispose();
            }
        }

        public void ResetEvents()
        {
            // Sets all states according to a fresh, ready-to-run pin transfer configuration
            _toolStates.Clear();
            _armStates.Clear();
            _carouselStates.Clear();
            foreach (var semaphore in _eventSignals.Values)
            {
                semaphore.Dispose();
            }
            _eventSignals.Clear();

            SetArmState("plate_gripped", false);
            SetArmState("safe", false);
            SetArmState("destination_transferred", false);
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
            SetToolState("33", "transferred", false);
            SetToolState("96", "transferred", false);
            SetToolState("100", "transferred", false);
            SetToolState("300", "transferred", false);
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
                await Task.WhenAll(
                    _events.WaitForToolState(toolId, "washed", true, ct),
                    _events.WaitForArmState("safe", true, ct)
                );

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
                await _events.WaitForArmState("destination_transferred", true, ct);
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
        private readonly RunLogger _runLogger;
        private readonly InstrumentEvents _events;

        public CommandRunner(JournalParser parser, string instrument, RunLogger runLogger, InstrumentEvents events)
        {
            _parser = parser;
            _instrument = instrument;
            _runLogger = runLogger;
            _events = events;
        }

        public async Task RunCommandsAsync(string journalID, int startCommandID, CancellationToken ct)
        {
            int commandID = startCommandID;
            SaveRunState(journalID, commandID);
            while (!ct.IsCancellationRequested)
            {
                string command = await _parser.GetNextCommandAsync(journalID, commandID, _instrument);
                if (string.IsNullOrEmpty(command))
                {
                    break; // No more commands
                }
                try
                {
                    await _parser.RunNextCommandAsync(_instrument, command, ct);
                    commandID++;
                }
                catch (OperationCanceledException)
                {
                    // Handle pause/cancellation
                }
                finally
                {
                    // Save state after each command, even if it was cancelled
                    SaveRunState(journalID, commandID);

                    
                }
            }
        }

        public void SaveRunState(string journalID, int commandID)
        {
            // Fetch the current run state
            var currentState = _runLogger.GetCurrentRunState(journalID);

            // Update only the relevant instrument's command ID
            var runState = new RunState
            {
                JournalID = journalID,
                EpsonCommandID = _instrument == "Epson" ? commandID : currentState.EpsonCommandID,
                KX2CommandID = _instrument == "KX2" ? commandID : currentState.KX2CommandID,
                SerializedToolStates = _events.SerializeToolStates(),
                SerializedArmStates = _events.SerializeArmStates(),
                SerializedCarouselStates = _events.SerializeCarouselStates()
            };

            _runLogger.SaveRunState(runState);
        }
    }
}