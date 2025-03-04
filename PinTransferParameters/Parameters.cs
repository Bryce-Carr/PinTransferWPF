using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

 namespace PinTransferParameters
    {
        public static class Parameters
        {
            // For testing/debugging
            public static bool Testing = true;
            public static bool UsingInstruments = false;

            // Databases
            public static string _LabwareDatabase = "labware.db";
            public static string LabwareDatabase
            {
                get { return _LabwareDatabase; }
                set { _LabwareDatabase = value; }
            }

            public static string _LoggingDatabase = "run_log.db";
            public static string LoggingDatabase
            {
                get { return _LoggingDatabase; }
                set { _LoggingDatabase = value; }
            }

            public static int numStacks = 6;

            // Epson SCARA
            public static string m_spel_ProjectFilePath = "C:\\EpsonRC70\\projects\\PinTransfer\\main\\main.sprj"; // '\' is escape character
            public static double offset = 0;

            // KX2 Arm
            public static double HomeArmSpeed = 50;
            public static double ArmSpeed = 75;
            public static double ArmAccel = 50;
            public static double AxisNum = 4;
            //public static string TopTeachpoint, BottomTeachpoint, RetractTeachpoint, Waypoint; // some of the parameters required by

            public static string TopTeachpointSource = "Stack1_Top";
            public static string BottomTeachpointSource = "Stack1_Bottom";
            public static string RetractTeachpointSource = "Stack1_Retract";
            public static string WaypointSource = "Stack1_Top_Up";

            public static string TopTeachpointDestination = "Away_Top";
            public static string BottomTeachpointDestination = "Away_Bottom";
            public static string RetractTeachpointDestination = "Away_Retract";
            public static string WaypointDestination = "Away_Top_Up";

            // Get/Put from/to hotel methods
            // KX2 Gripper
            public static double GripperOpenPos = 6; // 9/1/23 new gripper new open value 
            public static double GripperSpeed = 100; // gripper motor speed; 
            public static double GripperLiftHeight = 6; // Lift height when grapping a plate from shelf
            public static int GripperTimeDelay = 0; // indicated in msecs; not needed with servo gripper

            // Carousel
            public static string CarouselParameterFile =
                "C:\\Program Files (x86)\\PeakCarouselControl\\Carousel ParametersCS.08537.ini"; // Carousel Parameter File 
            public static double CSVelocity = 60; // Carousel Speed
            public static double HotelCapacity = 25; // # of shelves in hotel stacker
        }
    }
