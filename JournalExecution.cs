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
using static System.Net.Mime.MediaTypeNames;
using System.Data.Entity.Core.Common.CommandTrees;
using System.Numerics;
using Newtonsoft.Json;
using System.Diagnostics.Eventing.Reader;

namespace Integration
{
    public class CustomSemaphore : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private int _waitCount;

        public CustomSemaphore(int initialCount, int maxCount)
        {
            _semaphore = new SemaphoreSlim(initialCount, maxCount);
            _waitCount = 0;
        }

        public int CurrentCount => _semaphore.CurrentCount;
        public int WaitingCount => _waitCount;

        public async Task WaitAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _waitCount);
            try
            {
                await _semaphore.WaitAsync(cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _waitCount);
            }
        }

        public void Release()
        {
            _semaphore.Release();
        }

        public void ReleaseAll()
        {
            int count = WaitingCount;
            for (int i = 0; i < count; i++)
            {
                _semaphore.Release();
            }
        }

        public void Dispose()
        {
            _semaphore.Dispose();
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
        
        internal async Task RaiseToolAttached(string toolId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (OnToolAttached != null)
            {
                await OnToolAttached(toolId, cancellationToken);
                SetToolState("any_tool", "attached", true);
                SetToolState(toolId, "attached", true);
                SetToolState(toolId, "safe", true);
            }
        }

        internal async Task RaiseToolDetached(string toolId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (OnToolDetached != null)
            {
                await OnToolDetached(toolId, cancellationToken);
                SetToolState("any_tool", "attached", false);
                SetToolState(toolId, "attached", false);
            }
        }

        internal async Task RaiseWashCompleted(string toolId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (OnWashCompleted != null)
            {
                await OnWashCompleted(toolId, cancellationToken);
                SetToolState(toolId, "washed", true);
            }
        }

        internal async Task RaiseTransferCompleted(string toolId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (OnTransferCompleted != null)
            {
                SetToolState(toolId, "safe", false);
                await OnTransferCompleted(toolId, cancellationToken);
                SetToolState(toolId, "transferred", true);
                SetToolState(toolId, "washed", false);
                SetStageState("destination", "transferred", true);
            }
        }

        internal async Task RaiseToolSafe(string toolId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (OnToolSafe != null)
            {
                await OnToolSafe(toolId, cancellationToken);
                SetToolState(toolId, "safe", true);
                SetToolState(toolId, "transferred", false);
            }
        }


        public delegate Task KX2PlateEventHandler(string plateType, CancellationToken cancellationToken);
        public delegate Task KX2EventHandler(CancellationToken cancellationToken);

        public event KX2PlateEventHandler OnPlateGrabbedFromSequential;
        public event Func<string, int, CancellationToken, Task> OnPlateGrabbedFromHotel;
        public event KX2PlateEventHandler OnPlatePlacedToStage;
        public event KX2PlateEventHandler OnPlateGrabbedFromStage;
        public event Func<string, int, CancellationToken, Task> OnPlatePlacedToStack;
        public event KX2EventHandler OnArmSafe;
        public event KX2EventHandler OnArmHome;

        internal async Task RaisePlateGrabbedFromStack(string plateID, int location, CancellationToken cancellationToken)
        {
            // TODO make sure location is top spot for sequential stacker
            cancellationToken.ThrowIfCancellationRequested();
            if (OnPlateGrabbedFromHotel != null)
            {
                await OnPlateGrabbedFromHotel(plateID, location, cancellationToken);
                SetArmState("plate_gripped", true);
                SetCarouselState("safe", false);
            }
        }
        internal async Task RaisePlatePlacedToStage(string plateID, string plateType, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (OnPlatePlacedToStage != null)
            {
                SetArmState("safe", false);
                await OnPlatePlacedToStage(plateID, cancellationToken);
                SetStageState(plateType, "present", true);
                SetStageState("destination", "transferred", false);
                SetArmState("plate_gripped", false);
            }
        }
        internal async Task RaisePlateGrabbedFromStage(string plateID, string plateType, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (OnPlateGrabbedFromStage != null)
            {
                SetArmState("safe", false);
                await OnPlateGrabbedFromStage(plateID, cancellationToken);
                SetStageState(plateType, "present", false);
                SetArmState("plate_gripped", true);
            }
        }
        internal async Task RaisePlatePlacedToStack(string plateID, int location, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (OnPlatePlacedToStack != null)
            {
                await OnPlatePlacedToStack(plateID, location, cancellationToken);
                SetArmState("plate_gripped", false);
                SetCarouselState("safe", false);
            }
        }
        internal async Task RaiseArmSafe(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (OnArmSafe != null)
            {
                await OnArmSafe(cancellationToken);
                SetArmState("safe", true);
            }
        }
        internal async Task RaiseArmHome(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (OnArmHome != null)
            {
                await OnArmHome(cancellationToken);
            }
        }

        public delegate Task CarouselEventHandler(CancellationToken cancellationToken);
        public event CarouselEventHandler OnMoved;

        public event Func<int, string, CancellationToken, Task> OnCarouselRotated;
        public event Func<string, int, int, CancellationToken, Task> OnPlatePlacedInStacker;
        public event Func<string, int, int, CancellationToken, Task> OnPlateRemovedFromStacker;

        internal async Task RaiseCarouselRotated(int position, string plateType, CancellationToken ct)
        {
            if (OnCarouselRotated != null)
                await OnCarouselRotated(position, plateType, ct);
                SetCarouselState("safe", true);
        }

        internal async Task RaisePlatePlacedInStacker(string plateId, int stackerIndex, int position, CancellationToken ct)
        {
            if (OnPlatePlacedInStacker != null)
                await OnPlatePlacedInStacker(plateId, stackerIndex, position, ct);
        }

        internal async Task RaisePlateRemovedFromStacker(string plateId, int stackerIndex, int position, CancellationToken ct)
        {
            if (OnPlateRemovedFromStacker != null)
                await OnPlateRemovedFromStacker(plateId, stackerIndex, position, ct);
        }
         
        // Clamps
        public event Func<string, CancellationToken, Task> OnClampsStateChanged;

        internal async Task RaiseClampsStateChanged(string state, CancellationToken ct)
        {
            if (OnClampsStateChanged != null)
                await OnClampsStateChanged(state, ct);
            switch (state)
            {
                case "open":
                    SetArmState("clamps_open", true);
                    break;
                case "close":
                    SetArmState("clamps_open", false);
                    break;
            }
        }

        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, bool>> _toolStates = new ConcurrentDictionary<string, ConcurrentDictionary<string, bool>>();
        private readonly ConcurrentDictionary<string, bool> _armStates = new ConcurrentDictionary<string, bool>();
        private readonly ConcurrentDictionary<string, bool> _carouselStates = new ConcurrentDictionary<string, bool>();
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, bool>> _stageStates = new ConcurrentDictionary<string, ConcurrentDictionary<string, bool>>();
        private readonly ConcurrentDictionary<string, CustomSemaphore> _eventSemaphores = new ConcurrentDictionary<string, CustomSemaphore>();
        //private readonly ConcurrentDictionary<string, Tuple<int, int>> _plateLocations = new ConcurrentDictionary<string, Tuple<int, int>>();
        public List<Plate> _plates = new List<Plate>();

        public string SerializeToolStates()
        {
            return System.Text.Json.JsonSerializer.Serialize(_toolStates);
        }

        public void DeserializeToolStates(string serializedToolStates)
        {
            var restoredStates = System.Text.Json.JsonSerializer.Deserialize<ConcurrentDictionary<string, ConcurrentDictionary<string, bool>>>(serializedToolStates);
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
            return System.Text.Json.JsonSerializer.Serialize(_armStates);
        }

        public void DeserializeArmStates(string serializedArmStates)
        {
            var restoredStates = System.Text.Json.JsonSerializer.Deserialize<ConcurrentDictionary<string, bool>>(serializedArmStates);
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

        public string SerializeStageStates()
        {
            return System.Text.Json.JsonSerializer.Serialize(_stageStates);
        }

        public void DeserializeStageStates(string serializedStageStates)
        {
            var restoredStates = System.Text.Json.JsonSerializer.Deserialize<ConcurrentDictionary<string, ConcurrentDictionary<string, bool>>>(serializedStageStates);
            if (restoredStates != null)
            {
                foreach (var kvp in restoredStates)
                {
                    string toolId = kvp.Key;
                    ConcurrentDictionary<string, bool> states = kvp.Value;
                    _stageStates[toolId] = states;
                }
            }
        }

        public string SerializeCarouselStates()
        {
            return System.Text.Json.JsonSerializer.Serialize(_carouselStates);
        }

        public void DeserializeCarouselStates(string serializedCarouselStates)
        {
            var restoredStates = System.Text.Json.JsonSerializer.Deserialize<ConcurrentDictionary<string, bool>>(serializedCarouselStates);
            if (restoredStates != null)
            {
                foreach (var kvp in restoredStates)
                {
                    string state = kvp.Key;
                    bool value = kvp.Value;
                    _carouselStates[state] = value;
                }
            }
        }

        //public string SerializePlates()
        //{
            
        //    return JsonConvert.SerializeObject(_plates, new JsonSerializerSettings
        //    {
        //        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        //        TypeNameHandling = TypeNameHandling.All,
        //        Formatting = Formatting.Indented
        //    });
        //}

        //public List<Plate> DeserializePlates(string serializedPlates)
        //{
            
        //    return JsonConvert.DeserializeObject<List<Plate>>(serializedPlates, new JsonSerializerSettings
        //    {
        //        TypeNameHandling = TypeNameHandling.All
        //    });
        //}

        //public string SerializePlateLocations(Dictionary<string, Tuple<int, int>> plateLocations)
        //{
        //    return JsonSerializer.Serialize(plateLocations);
        //}

        //public string SerializePlateLocations()
        //{
        //    return JsonSerializer.Serialize(_plateLocations);
        //}
        //public ConcurrentDictionary<string, Tuple<int, int>> DeserializePlateLocations(string Plates)
        //{
        //    var restoredLocations = JsonSerializer.Deserialize<ConcurrentDictionary<string, Tuple<int, int>>>(Plates);
        //    if (restoredLocations != null)
        //    {
        //        foreach (var kvp in restoredLocations)
        //        {
        //            string plateID = kvp.Key;
        //            Tuple<int, int> locations = kvp.Value;
        //            _plateLocations[plateID] = locations;
        //        }
        //    }
        //    return restoredLocations;
        //}

        internal async Task WaitForToolState(string toolId, string state, bool expectedValue, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (_toolStates.TryGetValue(toolId, out var toolStates))
            {
                if (toolStates.TryGetValue(state, out bool currentValue) && currentValue == expectedValue)
                {
                    return; // Tool is already in the expected state
                }
            }

            var semaphoreKey = $"{toolId}_{state}_{expectedValue}";
            CustomSemaphore semaphore;

            if (!_eventSemaphores.TryGetValue(semaphoreKey, out semaphore))
            {
                semaphore = new CustomSemaphore(0, 1);
                if (!_eventSemaphores.TryAdd(semaphoreKey, semaphore))
                {
                    // If another thread has added a semaphore, use that one instead
                    _eventSemaphores.TryGetValue(semaphoreKey, out semaphore);
                }
            }

            await semaphore.WaitAsync(ct);
        }

        internal void SetToolState(string toolId, string state, bool value)
        {
            _toolStates.AddOrUpdate(toolId,
                _ => new ConcurrentDictionary<string, bool> { [state] = value },
                (_, existingStates) =>
                {
                    existingStates[state] = value;
                    return existingStates;
                });

            var semaphoreKey = $"{toolId}_{state}_{value}";
            if (_eventSemaphores.TryGetValue(semaphoreKey, out var semaphore))
            {
                semaphore.ReleaseAll();
                _eventSemaphores.TryRemove(semaphoreKey, out _);
            }
        }

        internal async Task WaitForArmState(string state, bool expectedValue, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _armStates.TryGetValue(state, out var armState);

            if (armState == expectedValue)
            {
                return; // arm is already in the expected state
            }

            var semaphoreKey = $"arm_{state}_{expectedValue}";
            var semaphore = _eventSemaphores.GetOrAdd(semaphoreKey, _ => new CustomSemaphore(0, 1));
            await semaphore.WaitAsync(ct);
        }

        internal void SetArmState(string state, bool value)
        {
            _armStates.AddOrUpdate(state, value, (key, oldValue) => value);

            var semaphoreKey = $"arm_{state}_{value}";
            if (_eventSemaphores.TryGetValue(semaphoreKey, out var semaphore))
            {
                semaphore.ReleaseAll();
            }
        }

        internal async Task WaitForCarouselState(string state, bool expectedValue, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _carouselStates.TryGetValue(state, out var carouselState);

            if (carouselState == expectedValue)
            {
                return; // Tool is already in the expected state
            }

            var semaphoreKey = $"carousel_{state}_{expectedValue}";
            var semaphore = _eventSemaphores.GetOrAdd(semaphoreKey, _ => new CustomSemaphore(0, 1));
            await semaphore.WaitAsync(ct);
        }

        internal void SetCarouselState(string state, bool value)
        {
            _carouselStates.AddOrUpdate(state, value, (key, oldValue) => value);

            var semaphoreKey = $"carousel_{state}_{value}";
            if (_eventSemaphores.TryGetValue(semaphoreKey, out var semaphore))
            {
                semaphore.ReleaseAll();
                _eventSemaphores.TryRemove(semaphoreKey, out _);
            }
        }

        internal async Task WaitForStageState(string plateType, string state, bool expectedValue, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (_stageStates.TryGetValue(plateType, out var stageStates))
            {
                if (stageStates.TryGetValue(state, out bool currentValue) && currentValue == expectedValue)
                {
                    return; // Tool is already in the expected state
                }
            }

            var semaphoreKey = $"{plateType}_{state}_{expectedValue}";
            CustomSemaphore semaphore;

            if (!_eventSemaphores.TryGetValue(semaphoreKey, out semaphore))
            {
                semaphore = new CustomSemaphore(0, 1);
                if (!_eventSemaphores.TryAdd(semaphoreKey, semaphore))
                {
                    // If another thread has added a semaphore, use that one instead
                    _eventSemaphores.TryGetValue(semaphoreKey, out semaphore);
                }
            }

            await semaphore.WaitAsync(ct);
        }

        private void SetStageState(string plateType, string state, bool value)
        {
            _stageStates.AddOrUpdate(plateType,
                _ => new ConcurrentDictionary<string, bool> { [state] = value },
                (_, existingStates) =>
                {
                    existingStates[state] = value;
                    return existingStates;
                });

            var semaphoreKey = $"{plateType}_{state}_{value}";
            if (_eventSemaphores.TryGetValue(semaphoreKey, out var semaphore))
            {
                semaphore.ReleaseAll();
                _eventSemaphores.TryRemove(semaphoreKey, out _);
            }
        }

        public void ResetEvents()
        {
            // Sets all states according to a fresh, ready-to-run pin transfer configuration
            _toolStates.Clear();
            _armStates.Clear();
            _stageStates.Clear();
            _carouselStates.Clear();
            _plates.Clear();
            foreach (var semaphore in _eventSemaphores.Values)
            {
                semaphore.Dispose();
            }
            _eventSemaphores.Clear();

            SetArmState("plate_gripped", false);
            SetArmState("safe", false);

            SetToolState("any_tool", "attached", false);
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

            SetStageState("source", "present", false);
            SetStageState("destination", "present", false);
            SetStageState("destination", "lidded", false);
            SetStageState("destination", "transferred", false);

            SetCarouselState("safe", false);
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

    public class JournalParser<T> where T : PlateStacker
    {
        private readonly string _connectionString;
        private readonly InstrumentEvents _events;
        private Carousel<T> _carousel;
        private string _sourcePlateID;
        private string _destinationPlateID;
        public JournalParser(string connectionString, InstrumentEvents events, Carousel<T> carousel)
        {
            _connectionString = connectionString;
            _events = events;
            _carousel = carousel;
        }

        internal async Task<string> GetNextCommandAsync(string journalID, int commandID, string instrument)
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

        internal async Task RunNextCommandAsync(string instrument, string commandString, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
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
            ct.ThrowIfCancellationRequested();
            string toolId = ExtractToolId(commandString);

            if (commandString.StartsWith("Attach"))
            {
                await Task.WhenAll(
                    _events.WaitForToolState("33", "attached", false, ct),
                    _events.WaitForToolState("100", "attached", false, ct),
                    _events.WaitForToolState("300", "attached", false, ct),
                    _events.WaitForToolState("96", "attached", false, ct)
                );
                // Extra precaution in case individual tool events are ever not called at the same time
                await _events.WaitForToolState("any_tool", "attached", false, ct);

                await _events.RaiseToolAttached(toolId, ct);
            }
            else if (commandString.StartsWith("Detach"))
            {
                await Task.WhenAll(
                    _events.WaitForToolState(toolId, "attached", true, ct),
                    _events.WaitForToolState(toolId, "washed", true, ct)
                );

                await _events.RaiseToolDetached(toolId, ct);
            }
            else if (commandString.StartsWith("Wash"))
            {
                await _events.WaitForToolState(toolId, "attached", true, ct);

                await _events.RaiseWashCompleted(toolId, ct);
            }
            else if (commandString.StartsWith("Transfer"))
            {
                await _events.RaiseClampsStateChanged("closed", ct);
                await Task.WhenAll(
                    _events.WaitForToolState(toolId, "washed", true, ct),
                    _events.WaitForArmState("safe", true, ct),
                    _events.WaitForStageState("destination", "transferred", false, ct)
                );
                await _events.WaitForArmState("clamps_open", false, ct);

                await _events.RaiseTransferCompleted(toolId, ct);
                await _events.RaiseClampsStateChanged("open", ct);
            }
            else if (commandString.StartsWith("Move Safe"))
            {
                await Task.WhenAll(
                    _events.WaitForToolState(toolId, "transferred", true, ct),
                    _events.WaitForArmState("safe", true, ct)
                );
                await _events.RaiseToolSafe(toolId, ct);
            }
            else
            {
                throw new ArgumentException($"No logic implemented for command: {commandString}");
            }
        }
        private async Task WaitForAnyToolSafe(IEnumerable<string> toolIds, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (toolIds == null || !toolIds.Any())
                throw new ArgumentException("At least one tool ID must be provided.", nameof(toolIds));

            var tasks = toolIds.Select(toolId => _events.WaitForToolState(toolId, "safe", true, cancellationToken));
            await Task.WhenAny(tasks);
        }

        private async Task RunKX2CommandAsync(string commandString, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var toolIds = new[] { "33", "96", "100", "300" };
            var parts = commandString.Split(' ');

            if (parts.Length < 2) return;

            var action = parts[0];

            if (action == "Move" && parts.Length >= 2 && parts[1] == "Safe")
            {
                await _events.RaiseArmSafe(ct);
                return;
            }
            else if (action == "Move" && parts.Length >= 2 && parts[1] == "Home")
            {
                await _events.RaiseArmHome(ct);
                return;
            }

                if (parts.Length < 4) return;

            var type = parts[1].ToLower();
            var plateID = parts[2].ToLower();
            var location = parts[4].ToLower();

            switch (action)
            {
                case "Get":
                    await HandleGetCommand(type, location, toolIds, plateID, ct);
                    break;
                case "Set":
                    await HandleSetCommand(type, location, toolIds, plateID, ct);
                    break;
            }
        }
        private async Task HandleGetCommand(string plateType, string location, string[] toolIds, string plateID, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (location == "stack")
            {
                (int stackerIndex, int platePosition) plateLocation = _carousel.GetPlateLocation(plateID);
                if (_carousel.CurrentPosition != plateLocation.stackerIndex)
                {
                    await _events.RaiseCarouselRotated(plateLocation.stackerIndex, plateType, ct);
                    _carousel.RotateToPosition(plateLocation.stackerIndex);
                }
                else
                {
                    _events.SetCarouselState("safe", true);
                }

                await Task.WhenAll(
                    _events.WaitForArmState("plate_gripped", false, ct),
                    _events.WaitForCarouselState("safe", true, ct)
                );
                // Last task to wait for
                await _events.WaitForCarouselState("safe", true, ct);

                //switch (plateType)
                //{
                //    case "source":
                //        _sourcePlateID = _carousel.GetNextPlate(plateType);
                //        plateLocation = _carousel.GetPlateLocation(_sourcePlateID);
                //        break;
                //    case "destination":
                //        _destinationPlateID = _carousel.GetNextPlate(plateType);
                //        plateLocation = _carousel.GetPlateLocation(_destinationPlateID);
                //        break;
                //}

                await _events.RaisePlateGrabbedFromStack(plateID, plateLocation.platePosition, ct);
                _carousel.RemovePlate(_events._plates.Find(p => p.ID == plateID));
            }
            else if (location == "stage")
            {
                await Task.WhenAll(
                    _events.WaitForArmState("plate_gripped", false, ct),
                    _events.WaitForStageState(plateType, "present", true, ct)
                );

                // The order of these awaits matters - transfer has to happen and then tool needs to become safe before accessing the stage -can't use Task.WhenAll
                await _events.WaitForStageState("destination", "transferred", true, ct);
                await WaitForAnyToolSafe(toolIds, ct);

                await _events.RaisePlateGrabbedFromStage(plateID, plateType, ct);
            }
        }
        private async Task HandleSetCommand(string plateType, string location, string[] toolIds, string plateID, CancellationToken ct)
        {
            if (location == "stack")
            {
                ct.ThrowIfCancellationRequested();
                (int stackerIndex, int platePosition) plateLocation = (_events._plates.Find(p => p.ID == plateID).Stack, _events._plates.Find(p => p.ID == plateID).FinalPositionInStack);

                if (_carousel.CurrentPosition != plateLocation.stackerIndex)
                {
                    await _events.RaiseCarouselRotated(plateLocation.stackerIndex, plateType, ct);
                    _carousel.RotateToPosition(plateLocation.stackerIndex);
                }
                else
                {
                    _events.SetCarouselState("safe", true);
                }

                await Task.WhenAll(
                    _events.WaitForArmState("plate_gripped", true, ct),
                    _events.WaitForCarouselState("safe", true, ct)
                );
                // Last task to wait for
                await _events.WaitForCarouselState("safe", true, ct);

                await _events.RaisePlatePlacedToStack(plateID, plateLocation.platePosition, ct);
            }
            else if (location == "stage")
            {
                await _events.WaitForStageState(plateType, "present", false, ct);
                await WaitForAnyToolSafe(toolIds, ct);

                await _events.RaisePlatePlacedToStage(plateID, plateType, ct);
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
    public class CommandRunner<T> where T : PlateStacker
    {
        private readonly JournalParser<T> _parser;
        private readonly string _instrument;
        private readonly RunLogger _runLogger;
        private readonly InstrumentEvents _events;

        public CommandRunner(JournalParser<T> parser, string instrument, RunLogger runLogger, InstrumentEvents events)
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
                    // Handle cancellation
                    SaveRunState(journalID, commandID);
                }
                catch (Exception ex)
                {
                    // Log the exception
                    Console.WriteLine($"Unexpected exception: {ex}");
                    throw; // Re-throw the exception if you want to propagate it
                }
                finally
                {
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
                SerializedStageStates = _events.SerializeStageStates(),
                SerializedCarouselStates = _events.SerializeCarouselStates(),
                Plates = PlateSerializer.SerializePlates(_events._plates),
                //Plates = _events.SerializePlates(),
                // assumption that SaveRunState will be called before first time running a journal so that InitialPlates will be initialized because I can't think of a more elegant way to do this..
                //InitialPlates = currentState.InitialPlates.Equals("") ? PlateSerializer.SerializePlates(_events._plates) : currentState.InitialPlates, 
                InitialPlates = currentState.InitialPlates.Equals("") ? PlateSerializer.SerializePlates(_events._plates) : currentState.InitialPlates,
            };

            _runLogger.SaveRunState(runState);
        }
    }
}