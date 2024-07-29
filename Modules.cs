using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Integration
{
    public static class PlateSerializer
    {
        public static string SerializePlates(List<Plate> plates)
        {
            return JsonConvert.SerializeObject(plates, new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All,
                Formatting = Formatting.Indented,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore
            });
        }

        public static List<Plate> DeserializePlates(string json)
        {
            return JsonConvert.DeserializeObject<List<Plate>>(json, new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore
            });
        }
    }

    // Custom JsonConverter for Tuple<int, int>
    public class TupleConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(Tuple<int, int>);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var jObject = JObject.Load(reader);
            int item1 = jObject["Item1"].Value<int>();
            int item2 = jObject["Item2"].Value<int>();
            return new Tuple<int, int>(item1, item2);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var tuple = (Tuple<int, int>)value;
            var jObject = new JObject
            {
                ["Item1"] = tuple.Item1,
                ["Item2"] = tuple.Item2
            };
            jObject.WriteTo(writer);
        }
    }

    // Update the Plate class to include JsonConverter attribute
    [JsonConverter(typeof(PlateConverter))]
    public class Plate
    {
        public Dictionary<string, bool> Status { get; set; }

        private string _id;
        public string ID
        {
            get => _id;
            set => _id = value;
        }

        private int _stack;
        public int Stack
        {
            get => _stack;
            set => _stack = value;
        }

        private int _finalStack;
        public int FinalStack
        {
            get => _finalStack;
            set => _finalStack = value;
        }

        private int _positionInStack;
        public int PositionInStack
        {
            get => _positionInStack;
            set => _positionInStack = value;
        }

        public int _finalPositionInStack;
        public int FinalPositionInStack
        {
            get => _finalPositionInStack;
            set => _finalPositionInStack = value;
        }
        public Plate()
        {
            Status = new Dictionary<string, bool>();
        }
    }

    // Custom JsonConverter for Plate and its derived classes
    public class PlateConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return typeof(Plate).IsAssignableFrom(objectType);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            JObject jObject = JObject.Load(reader);
            string typeString = jObject["$type"]?.Value<string>();

            if (string.IsNullOrEmpty(typeString))
            {
                return null;
            }

            Type type = Type.GetType(typeString);
            object instance = Activator.CreateInstance(type);

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (jObject.TryGetValue(prop.Name, out JToken propValue))
                {
                    if (prop.PropertyType == typeof(Dictionary<string, bool>))
                    {
                        var dict = propValue.ToObject<Dictionary<string, bool>>();
                        prop.SetValue(instance, dict);
                    }
                    else if (prop.PropertyType == typeof(Dictionary<string, int>))
                    {
                        var dict = propValue.ToObject<Dictionary<string, int>>();
                        prop.SetValue(instance, dict);
                    }
                    else if (prop.PropertyType == typeof(Tuple<int, int>))
                    {
                        var item1 = propValue["Item1"].Value<int>();
                        var item2 = propValue["Item2"].Value<int>();
                        prop.SetValue(instance, new Tuple<int, int>(item1, item2));
                    }
                    else
                    {
                        prop.SetValue(instance, propValue.ToObject(prop.PropertyType, serializer));
                    }
                }
            }

            return instance;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            JObject jObject = new JObject();
            Type type = value.GetType();

            jObject.Add("$type", type.AssemblyQualifiedName);

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                object propValue = prop.GetValue(value);
                if (propValue != null)
                {
                    if (prop.PropertyType == typeof(Dictionary<string, bool>))
                    {
                        var dict = (Dictionary<string, bool>)propValue;
                        jObject.Add(prop.Name, JObject.FromObject(dict));
                    }
                    else if (prop.PropertyType == typeof(Dictionary<string, int>))
                    {
                        var dict = (Dictionary<string, int>)propValue;
                        jObject.Add(prop.Name, JObject.FromObject(dict));
                    }
                    else if (prop.PropertyType == typeof(Tuple<int, int>))
                    {
                        var tuple = (Tuple<int, int>)propValue;
                        jObject.Add(prop.Name, new JObject
                        {
                            ["Item1"] = tuple.Item1,
                            ["Item2"] = tuple.Item2
                        });
                    }
                    else
                    {
                        jObject.Add(prop.Name, JToken.FromObject(propValue, serializer));
                    }
                }
            }

            jObject.WriteTo(writer);
        }
    }

    public class DestinationPlate : Plate
    {
        public Dictionary<string, int> SourcePlates { get; set; }
        public DestinationPlate()
        {
            SourcePlates = new Dictionary<string, int>();
        }
        public void AddSourcePlate(string sourceId, int transferVolume)
        {
            if (SourcePlates.ContainsKey(sourceId))
            {
                SourcePlates[sourceId] += transferVolume;
            }
            else
            {
                SourcePlates.Add(sourceId, transferVolume);
            }
        }
    }

    public class SourcePlate : Plate
    {
        public Tuple<int, int> Replicates { get; set; } // K:V = volume:replicates
    }

    public abstract class Instrument
    {
    }

    public abstract class Mover
    {
    }

    public abstract class PlateStacker
    {
        public int _stackCapacity;
        public Type _plateType;

        public int StackCapacity
        {
            get => _stackCapacity;
            set
            {
                if (value > 0)
                {
                    _stackCapacity = value;
                }
                else
                {
                    throw new ArgumentException("Please pass a positive value", nameof(value));
                }
            }
        }
        public List<Plate> Plates { get; }

        public PlateStacker(int capacity)
        {
            StackCapacity = capacity;
            Plates = new List<Plate>();
        }
        public virtual void ClearAllPlates()
        {
            var removedPlates = new List<Plate>(Plates);
            Plates.Clear();
            foreach (var plate in removedPlates)
            {
                Console.WriteLine($"Plate {plate.ID} removed from stacker");
            }
        }

        public abstract void RemovePlate(Plate plate);
        public abstract void AddPlate(Plate plate);
        public abstract bool IsFull { get; }
        public abstract int FindPlatePosition(string plateId);
        public abstract int FindPlateFinalPosition(string plateId);
        public abstract Plate RemovePlate(int position);
    }

    public class HotelStacker : PlateStacker
    {
        public HotelStacker(int capacity) : base(capacity)
        {
        }

        public override bool IsFull => Plates.Count == StackCapacity;

        public override void AddPlate(Plate plate)
        {
            if (IsFull)
            {
                throw new InvalidOperationException("Stacker is full");
            }
            Plates.Add(plate);
            Console.WriteLine($"Plate {plate.ID} added to hotel stacker at position {Plates.Count - 1}");
        }

        public override void RemovePlate(Plate plate)
        {
            if (Plates.Remove(plate))
            {
                Console.WriteLine($"Plate {plate.ID} removed from hotel stacker");
            }
            else
            {
                throw new InvalidOperationException("Plate not in stacker");
            }
        }

        public override Plate RemovePlate(int position)
        {
            if (position < 1 || position > Plates.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(position), "Invalid plate position");
            }
            var plate = Plates[position];
            Plates.RemoveAt(position);
            Console.WriteLine($"Plate {plate.ID} removed from hotel stacker at position {position}");
            return plate;
        }

        public override int FindPlatePosition(string plateId)
        {
            var plate = Plates.Find(p => p.ID == plateId);
            return plate?.PositionInStack ?? -1; // Or another sentinel value
        }

        public override int FindPlateFinalPosition(string plateId)
        {
            var plate = Plates.Find(p => p.ID == plateId);
            return plate?.FinalPositionInStack ?? -1; // Or another sentinel value
        }
    }

    public class SequentialStacker : PlateStacker
    {
        // TODO implement this?
        public string _storeMode;

        public SequentialStacker(int capacity) : base(capacity)
        {
        }

        public override bool IsFull => Plates.Count >= StackCapacity;

        public override void AddPlate(Plate plate)
        {
            if (IsFull)
            {
                throw new InvalidOperationException("Stacker is full");
            }
            if (_plateType == null)
            {
                _plateType = plate.GetType();
            }
            else if (plate.GetType() != _plateType)
            {
                throw new InvalidOperationException("Sequential stacker can only contain one plate type");
            }
            Plates.Add(plate);
            Console.WriteLine($"Plate {plate.ID} added to sequential stacker at the top");
        }

        public override void RemovePlate(Plate plate)
        {
            if (Plates.Count == 0)
            {
                throw new InvalidOperationException("Stacker is empty");
            }
            if (Plates[Plates.Count - 1] != plate)
            {
                throw new InvalidOperationException("Can only remove the top plate from a sequential stacker");
            }
            Plates.RemoveAt(Plates.Count - 1);
            Console.WriteLine($"Plate {plate.ID} removed from the top of sequential stacker");
        }

        public override Plate RemovePlate(int position)
        {
            if (position != Plates.Count - 1)
            {
                throw new InvalidOperationException("Can only remove the top plate from a sequential stacker");
            }
            var plate = Plates[position];
            Plates.RemoveAt(position);
            Console.WriteLine($"Plate {plate.ID} removed from the top of sequential stacker");
            return plate;
        }

        public override int FindPlatePosition(string plateId)
        {
            var plate = Plates.Find(p => p.ID == plateId);
            return plate?.PositionInStack ?? -1; // Or another sentinel value
        }

        public override int FindPlateFinalPosition(string plateId)
        {
            var plate = Plates.Find(p => p.ID == plateId);
            return plate?.FinalPositionInStack ?? -1; // Or another sentinel value
        }
    }

    public class Carousel<T> where T : PlateStacker
    {
        public List<T> Stackers { get; }
        public int CurrentPosition { get; private set; }

        public Carousel(int numberOfStackers, int stackerCapacity, Func<int, T> stackerFactory)
        {
            Stackers = Enumerable.Range(0, numberOfStackers)
                .Select(i => stackerFactory(stackerCapacity))
                .ToList();
            CurrentPosition = 0;
        }

        public List<Plate> RemoveAllPlates()
        {
            List<Plate> removedPlates = new List<Plate>();

            foreach (var stacker in Stackers)
            {
                var stackerPlates = stacker.Plates.ToList();
                removedPlates.AddRange(stackerPlates);
                stacker.ClearAllPlates();
            }

            Console.WriteLine($"Removed {removedPlates.Count} plates from the carousel");
            return removedPlates;
        }

        public void RotateToPosition(int position)
        {
            if (position < 1 || position > Stackers.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(position), "Invalid carousel position");
            }
            CurrentPosition = position;
            Console.WriteLine($"Carousel rotated to position {position}");
        }

        public (int stackerIndex, int platePosition) GetPlateLocation(string plateId)
        {
            for (int stackerIdx = 0; stackerIdx < Stackers.Count; stackerIdx++)
            {
                int platePosition = Stackers[stackerIdx].FindPlatePosition(plateId);
                if (platePosition != -1)
                {
                    return (stackerIdx + 1, platePosition);
                }
            }
            throw new InvalidOperationException($"Plate {plateId} not found in carousel");
        }

        public (int stackerIndex, int platePosition) GetPlateFinalLocation(string plateId)
        {
            for (int i = 0; i < Stackers.Count; i++)
            {
                int platePosition = Stackers[i].FindPlateFinalPosition(plateId);
                if (platePosition != -1)
                {
                    return (i, platePosition);
                }
            }
            throw new InvalidOperationException($"Plate {plateId} not found in carousel");
        }

        public void AddPlate(Plate plate, int Stack)
        {
            //var availableStacker = Stackers.FirstOrDefault(s => !s.IsFull);
            //if (availableStacker == null)
            //{
            //    throw new InvalidOperationException("All stackers are full");
            //}
            var availableStacker = Stackers[Stack - 1];
            availableStacker.AddPlate(plate);
        }

        public void RemovePlate(Plate plate)
        {
            //var (stackerIndex, platePosition) = GetPlateLocation(plateId);
            Stackers[plate.Stack - 1].RemovePlate(plate);
        }
    }
}