using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using RCAPINet;

namespace Integration
{
    internal class InstrumentController
    {
        internal readonly Dictionary<string, string> Messages = new Dictionary<string, string>
        {
            {"OpenGripper?", "Are you sure you want to open the gripper?"},
            {"GripperHoldingPlate", "Warning: Gripper may be holding plate"},
            {"PlacementLocationOccupied", "Can't place plate to that location because it may contain another plate! Aborting..."},
            {"PlateMissing", "There appears to be a plate missing at that location!"},
            {"IgnoreWarning?", "Would you like to ignore this warning, and continue?"},
            {"InitializeRobotArm?", "Would you like to initialize the Robot arm?"},
            {"ConfirmDeviceInitialization", "Click yes to initialize the device"},
            {"RemoveLastPlate", "Unable to remove plate in the middle of the run, removing the last plate..."},
            {"InitializeCarousel?", "Would you like to initialize the Carousel?"},
            {"InitializeEpson?", "Would you like to initialize the Epson?"},
            {"InitializeAllDevices?", "Initialize all the devices?"},
            {"DeviceInitialization", "Device Initialization"},
            {"InitializeEachDevice?", "Would you like to initialize each device?"},
            {"NoDevicesInitialized", "No Devices Initialized!"}
        };

        internal KX2RobotControlNamespace.KX2RobotControl KX2; // declare KX2
        internal CarouselControlNamespace.CarouselControl CS6; // declare carousel
        internal Spel m_spel; // declare m_spel
        internal MessageBoxResult messageBoxResult;
        private Window _owner;

        internal InstrumentController(Window owner)
        {
            _owner = owner;
            // create KX2 obj
            KX2 = new KX2RobotControlNamespace.KX2RobotControl();
            // create Carousel obj
            CS6 = new CarouselControlNamespace.CarouselControl();
            // instantiate m_spel
            m_spel = new Spel();
        }

        internal void ShowOverrideDeviceInitialization()
        {
            messageBoxResult = MessageBox.Show(_owner, Messages["InitializeAllDevices?"], Messages["DeviceInitialization"], MessageBoxButton.YesNo);

            if (messageBoxResult == MessageBoxResult.Yes)
            {
                //// initialize KX2 object
                InitializeAllDevices();

            }
            else if (messageBoxResult == MessageBoxResult.No)
            {
                InitializeEachDeviceSeparately();
            }
        }

        internal async void InitializeAllDevices()
        {
            await Task.Run(async () =>
            {
                Task initializeSpel = InitializeSpelAsync();
                // ensure Epson Motors are on 
                if (!m_spel.MotorsOn) { m_spel.MotorsOn = true; }
                Task initializeKX2 = InitializeKX2Async();
                Task initializeCS = InitializeCSAsync();

                await Task.WhenAll(initializeSpel, initializeKX2, initializeCS);
            });


        }

        internal async Task InitializeSpelAsync()
        {
            await Task.Run(() =>
            {
                m_spel.Initialize();

                m_spel.Project = Parameters.m_spel_ProjectFilePath;// \ is escape character
                m_spel.Reset();
                m_spel.RebuildProject();

                m_spel.EventReceived += new RCAPINet.Spel.EventReceivedEventHandler(m_spel_EventReceived);
                //m_spel.AsyncMode = true; // When the AsyncMode property is true and you execute an asynchronous method,
                // the method will be started and control will return immediately back to
                // the.NET application for further processing.
                m_spel.EnableEvent(SpelEvents.AllTasksStopped, false); // gets rid of return prompt that m_spel return after executing a task
                m_spel.DisableMsgDispatch = true;
            });
        }

        //  interpret KX2 error codes
        internal void KX2GetErrorCode(short ret)
        {
            if (ret != 0) { MessageBox.Show(_owner, KX2.GetErrorCode(ret)); }
        }

        internal void InitializeArm()
        {
            short ret;
            ret = KX2.Initialize();
            KX2GetErrorCode(ret);
        }

        internal async Task InitializeKX2Async()
        {
            await Task.Run(() =>
            {
                //// initialize KX2 object
                InitializeArm();
            });
        }

        //  interprets Carousel error codes
        internal void CS6GetErrorCode(short ret)
        {
            if (ret != 0) { MessageBox.Show(_owner, CS6.GetErrorCode(ret)); }
        }

        //  initialize CS
        internal void InitializeCS()
        {
            short ret;
            ret = CS6.Initialize(Parameters.CarouselParameterFile);
            CS6GetErrorCode(ret);
        }

        internal async Task InitializeCSAsync()
        {
            await Task.Run(() =>
            {
                //// initialize Carousel object
                InitializeCS();
            });
        }

        internal void InitializeEachDeviceSeparately()
        {
            messageBoxResult = MessageBox.Show(_owner, Messages["InitializeEachDevice?"], Messages["DeviceInitialization"], MessageBoxButton.YesNo);

            if (messageBoxResult == MessageBoxResult.Yes)
            {
                messageBoxResult = MessageBox.Show(_owner, Messages["InitializeRobotArm?"], Messages["ConfirmDeviceInitialization"], MessageBoxButton.YesNo);

                if (messageBoxResult == MessageBoxResult.Yes)
                {
                    //// initialize KX2 object
                    InitializeArm();
                }
                else if (messageBoxResult == MessageBoxResult.No)
                {
                    // do nothing
                    // or skip device initializations
                }

                messageBoxResult = MessageBox.Show(_owner, Messages["InitializeCarousel?"], Messages["ConfirmDeviceInitialization"], MessageBoxButton.YesNo);

                if (messageBoxResult == MessageBoxResult.Yes)
                {
                    //// initialize Carousel object
                    InitializeCS();
                }
                else if (messageBoxResult == MessageBoxResult.No)
                {
                    // do nothing
                    // or skip device initializations
                }

                messageBoxResult = MessageBox.Show(_owner, Messages["InitializeEpson?"], Messages["ConfirmDeviceInitialization"], MessageBoxButton.YesNo);

                if (messageBoxResult == MessageBoxResult.Yes)
                {
                    m_spel.Initialize();

                    m_spel.Project = Parameters.m_spel_ProjectFilePath; // \ is escape character
                    m_spel.RebuildProject(); // resets errors

                    m_spel.EventReceived += new RCAPINet.Spel.EventReceivedEventHandler(m_spel_EventReceived);
                    m_spel.AsyncMode = true; // When the AsyncMode property is true and you execute an asynchronous method,
                                             // the method will be started and control will return immediately back to
                                             // the.NET application for further processing.

                }
                else if (messageBoxResult == MessageBoxResult.No)
                {
                    // do nothing 
                    // or skip device initializations
                }
            }
            else { MessageBox.Show(_owner, Messages["NoDevicesInitialized"]); }
        }

        // event handler for Epson
        #pragma warning disable IDE1006 // Naming Styles
        public void m_spel_EventReceived(object sender, RCAPINet.SpelEventArgs e)
        #pragma warning restore IDE1006 // Naming Styles
        {
            if (e.Event == SpelEvents.EstopOn)
            {
                KX2.EmergencyStop(); // stop robot arm
            }

            MessageBox.Show(_owner, "recieved event" + e.Event);

        }
        internal void MovetoTeachPoint(string tp)
        {
            int Timeout = 0;
            byte Index = 0;
            KX2.TeachPointMoveTo(tp, Parameters.ArmSpeed, Parameters.ArmAccel, true, TimeoutMsec: ref Timeout, false, Index: ref Index);
        }

        internal void GetPlateFromStage(string plateID)
        {
            short ret;

            if (plateID.Contains("source"))
            {
                ret = KX2.ScriptRun("", "GetStageSourcePlate", true);
            }
            else if (plateID.Contains("destination"))
            {
                ret = KX2.ScriptRun("", "GetStageDestPlate", true);
            }
            else
            {
                throw new InvalidOperationException("Plate has to be source or destination");
            }
            KX2GetErrorCode(ret);
        }

        internal void SetPlateToStage(string plateID)
        {
            short ret;
            
            if (plateID.Contains("source"))
            {
                ret = KX2.ScriptRun("", "PutStageSourcePlate", true);
            }
            else if (plateID.Contains("destination"))
            {
                ret = KX2.ScriptRun("", "PutStageDestPlate", true);
            }
            else
            {
                throw new InvalidOperationException("Plate has to be source or destination");
            }
            KX2GetErrorCode(ret);
        }

        internal void GetPlateFromStack(string plateID, short stackCapacity, short location)
        {
            short ret;
            KX2.SetMovePathMode(KX2RobotControlNamespace.KX2RobotControl.eMovePathMode.Linear);
            //ret = KX2.RemovePlateFromHotel(TopTeachpoint, BottomTeachpoint, RetractTeachpoint, HotelCapacity, GetPlateSelfLocation(plate), GripperLiftHeight, ArmSpeed, GripperTimeDelay, true, 0, 0, Waypoint, true, true);

            if (plateID.Contains("source"))
            {
                ret = KX2.RemovePlateFromHotel(Parameters.TopTeachpointSource, Parameters.BottomTeachpointSource,
                    Parameters.RetractTeachpointSource, stackCapacity, location,
                    Parameters.GripperLiftHeight, Parameters.ArmSpeed, Parameters.GripperTimeDelay, true);
            }
            else if (plateID.Contains("destination"))
            {
                ret = KX2.RemovePlateFromHotel(Parameters.TopTeachpointDestination, Parameters.BottomTeachpointDestination,
                                    Parameters.RetractTeachpointDestination, stackCapacity, location,
                                    Parameters.GripperLiftHeight, Parameters.ArmSpeed, Parameters.GripperTimeDelay, true);
            }
            else
            {
                throw new InvalidOperationException("Plate has to be source or destination");
            }
            KX2GetErrorCode(ret);

            KX2.SetMovePathMode(KX2RobotControlNamespace.KX2RobotControl.eMovePathMode.Joint);
        }

        internal void SetPlateToStack(string plateID, short stackCapacity, short location)
        {
            short ret;
            KX2.SetMovePathMode(KX2RobotControlNamespace.KX2RobotControl.eMovePathMode.Linear);

            if (plateID.Contains("source"))
            {
                ret = KX2.PlacePlateInHotel(Parameters.TopTeachpointSource, Parameters.BottomTeachpointSource,
                    Parameters.RetractTeachpointSource, stackCapacity, location, Parameters.GripperLiftHeight,
                    Parameters.ArmSpeed, Parameters.GripperTimeDelay, true);
            }
            else if (plateID.Contains("destination"))
            {
                MovetoTeachPoint("SafeLow");
                MovetoTeachPoint("Away_Far");
                ret = KX2.PlacePlateInHotel(Parameters.TopTeachpointDestination, Parameters.BottomTeachpointDestination,
                    Parameters.RetractTeachpointDestination, stackCapacity, location, Parameters.GripperLiftHeight,
                    Parameters.ArmSpeed, Parameters.GripperTimeDelay, true);
            }
            else
            {
                throw new InvalidOperationException("Plate has to be source or destination");
            }

            KX2GetErrorCode(ret);
            // set movepathmode back to joint to avoid interpolation errors
            KX2.SetMovePathMode(KX2RobotControlNamespace.KX2RobotControl.eMovePathMode.Joint);
        }

        internal void RotateCarousel(int stackNum, string plateType)
        {
            int stackToMove = 0;
            switch (plateType)
            {
                case "source":
                    stackToMove = stackNum;
                    break;
                case "destination":
                    stackToMove = stackNum == 6 ? 1 : stackNum + 1;
                    break;
            }
            MoveCS((short)stackToMove);
        }

        private void MoveCS(short stackNum)
        {
            short ret;
            long timeout = 0;
            ret = CS6.Move(stackNum, Parameters.CSVelocity, true, CalculatedMoveTimeMsec: ref timeout);
            CS6GetErrorCode(ret);
        }

        private void AllRelaysOff()
        {
            try
            {
                m_spel.Call("AllRelaysOff");
            }
            catch (SpelException ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        // Stop CS

        private void StopCS()
        {
            short ret;
            ret = CS6.StopMove();
            CS6GetErrorCode((short)ret);
        }

        internal async void StopAll()
        {
            // TODO do these at same time async
            m_spel.Stop(SpelStopType.StopAllTasks); // stops all Epson
            m_spel.ResetAbort();
            AllRelaysOff(); // turn off all I/O devices
            KX2.EmergencyStop(); // stops Arm
            StopCS(); // stops carousel
        }
    }
}