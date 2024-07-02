using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Integration
{
    public class Plate
    {
        public Dictionary<string, bool> Status;
        private string _id;
        public string ID
        {
            get
            {
                return _id;
            }
            set
            {
                _id = value;
            }
        }
        
        public Plate()
        {
            Status = new Dictionary<string, bool>();
        }
    }
    public class DestinationPlate : Plate
    {
        public Tuple<string, double> Sources; // name of source plates transferring to this destination and volumes
        public DestinationPlate() : base()
        {
            Status = new Dictionary<string, bool>();
        }
    }
    public class SourcePlate : Plate
    {
        public Tuple<int, int> replicates; // K:V = volume:replicates

        public SourcePlate() : base()
        {
            Status = new Dictionary<string, bool>();
        }
    }
    class Instrument
    {
    }
    class Mover
    {
    }
    class PlateStacker
    {
        private int _stackCapacity;
        public int stackCapacity
        {
            get
            {
                return _stackCapacity;
            }
            set
            {
                if (value > 0)
                {
                    _stackCapacity = value;
                }
                else
                {
                    throw new Exception("Please Pass a Positive Value");
                }
            }
        }
        public List<Plate> Plates;
        public PlateStacker(int capacity)
        {
            _stackCapacity = capacity;
            Plates = new List<Plate>();
        }
        public virtual void Remove_Plate(Plate plate)
        {
            if (Plates.Contains(plate))
            {
                Plates.Remove(plate);
                Console.WriteLine("Plate " + plate.ID + " removed from stacker");
            }
            else
            {
                throw new Exception("Plate not in stacker");
            }
        }
        public virtual void Add_Plate(Plate plate)
        {
            if (!Plates.Contains(plate))
            {
                Plates.Add(plate);
                Console.WriteLine("Plate " + plate.ID + " added to stacker");
            }
            else
            {
                throw new Exception("Plate already in stacker");
            }
        }
    }

    class HotelStacker : PlateStacker
    {
        public List<int> locations;
        public HotelStacker(int capacity) : base(capacity)
        {
            locations = Enumerable.Range(1, capacity).ToList();
        }

        public override void Remove_Plate(Plate plate)
        {
            base.Remove_Plate(plate);
        }
        public override void Add_Plate(Plate plate)
        {
            base.Add_Plate(plate);
        }
    }
    class SequentialStacker : PlateStacker
    {
        public SequentialStacker(int capacity) : base(capacity)
        {
        }
        public override void Remove_Plate(Plate plate)
        {
            base.Remove_Plate(plate);
        }
        public override void Add_Plate(Plate plate)
        {
            base.Add_Plate(plate);
        }
    }
}
