using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Integration
{
    internal static class Parameters
    {
        // Databases
        internal static string _LabwareDatabase = "labware.db";
        internal static string LabwareDatabase 
        {
            get { return _LabwareDatabase; }
            set { _LabwareDatabase = value; }
        }

        internal static string _LoggingDatabase = "run_log.db";
        internal static string LoggingDatabase
        {
            get { return _LoggingDatabase; }
            set { _LoggingDatabase = value; }
        }

        internal static int numStacks = 6;

        // Epson SCARA
        internal static string m_spel_ProjectFilePath = "C:\\EpsonRC70\\projects\\PinTransfer\\main\\main.sprj"; // '\' is escape character
        internal static double offset = 0;

        // KX2 Arm
        internal static double HomeArmSpeed = 50;
        internal static double ArmSpeed = 75;
        internal static double ArmAccel = 50;
        internal static double AxisNum = 4;
        //internal static string TopTeachpoint, BottomTeachpoint, RetractTeachpoint, Waypoint; // some of the parameters required by

        internal static string TopTeachpointSource = "Stack1_Top";
        internal static string BottomTeachpointSource = "Stack1_Bottom";
        internal static string RetractTeachpointSource = "Stack1_Retract";
        internal static string WaypointSource = "Stack1_Top_Up";

        internal static string TopTeachpointDestination = "Away_Top";
        internal static string BottomTeachpointDestination = "Away_Bottom";
        internal static string RetractTeachpointDestination = "Away_Retract";
        internal static string WaypointDestination = "Away_Top_Up";

        // Get/Put from/to hotel methods
        // KX2 Gripper
        internal static double GripperOpenPos = 6; // 9/1/23 new gripper new open value 
        internal static double GripperSpeed = 100; // gripper motor speed; 
        internal static double GripperLiftHeight = 1; // Lift height when grapping a plate from shelf
        internal static int GripperTimeDelay = 10; // indicated in msecs; not needed with servo gripper

        // Carousel
        internal static string CarouselParameterFile =
            "C:\\Program Files (x86)\\PeakCarouselControl\\Carousel ParametersCS.08537.ini"; // Carousel Parameter File 
        public static double CSVelocity = 60; // Carousel Speed
        internal static double HotelCapacity = 25; // # of shelves in hotel stacker
    }
}
