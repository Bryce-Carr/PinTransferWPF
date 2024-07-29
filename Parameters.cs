using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PinTransferWPF
{
    internal static class Parameters
    {
        // Databases
        private static string _LabwareDatabase = "labware.db";
        internal static string LabwareDatabase 
        {
            get { return _LabwareDatabase; }
            set { _LabwareDatabase = value; }
        }

        private static string _LoggingDatabase = "run_log.db";
        internal static string LoggingDatabase
        {
            get { return _LoggingDatabase; }
            set { _LoggingDatabase = value; }
        }

        internal static int numStacks = 6;

        // Epson SCARA
        private static string m_spel_ProjectFilePath = "C:\\EpsonRC70\\projects\\PinTransfer\\main\\main.sprj"; // '\' is escape character
        private static double offset = 0;

        // KX2 Arm
        private static double HomeArmSpeed = 50;
        private static double ArmSpeed = 75;
        private static double ArmAccel = 50;
        private static double AxisNum = 4;
        private static string TopTeachpoint, BottomTeachpoint, RetractTeachpoint, Waypoint; // some of the parameters required by
                                                                                     // Get/Put from/to hotel methods
        // KX2 Gripper
        private static double GripperOpenPos = 6; // 9/1/23 new gripper new open value 
        private static double GripperSpeed = 100; // gripper motor speed; 
        private static double GripperLiftHeight = 1; // Lift height when grapping a plate from shelf
        private static double GripperTimeDelay = 10; // indicated in msecs; not needed with servo gripper

        // Carousel
        private static string CarouselParameterFile =
            "C:\\Program Files (x86)\\PeakCarouselControl\\Carousel ParametersCS.08537.ini"; // Carousel Parameter File 
        public static double CSVelocity = 60; // Carousel Speed
        private static double HotelCapacity = 25; // # of shelves in hotel stacker
    }
}
