using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using RCAPINet;
using PinTransferParameters;

namespace Integration
{
    public class InstrumentController
    {
        public readonly Dictionary<string, string> Messages = new Dictionary<string, string>
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

        public delegate Task InstrumentEventHandler(CancellationToken cancellationToken);
        public event InstrumentEventHandler OnInstrumentInitialization;
        public async Task RaiseInstrumentInitialization(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (OnInstrumentInitialization != null)
            {
                await OnInstrumentInitialization(cancellationToken);
            }
        }

        public KX2RobotControlNamespace.KX2RobotControl KX2; // declare KX2
        public CarouselControlNamespace.CarouselControl CS6; // declare carousel
        public Spel m_spel; // declare m_spel
        public MessageBoxResult messageBoxResult;
        private Window _owner;

        public InstrumentController(Window owner)
        {
            _owner = owner;
            // create KX2 obj
            KX2 = new KX2RobotControlNamespace.KX2RobotControl();
            // create Carousel obj
            CS6 = new CarouselControlNamespace.CarouselControl();
            // instantiate m_spel
            m_spel = new Spel();

            OnInstrumentInitialization += async (ct) =>
            {
                ct.ThrowIfCancellationRequested();

            };
        }

        public void ShowOverrideDeviceInitialization()
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

        public async void InitializeAllDevices()
        {
            Task initializeSpel = InitializeSpelAsync();
            // ensure Epson Motors are on 
            if (!m_spel.MotorsOn) { m_spel.MotorsOn = true; }
            Task initializeKX2 = InitializeKX2Async();
            Task initializeCS = InitializeCSAsync();

            await Task.WhenAll(initializeSpel, initializeKX2, initializeCS);
        }

        public async Task InitializeSpelAsync()
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
        public void HandleKX2ErrorCode(short ret)
        {
            //if (ret == 2)
            //{
            //    string resumeLine = KX2.GetErrorCode(ret);

            //}
            //if (ret != 0) { MessageBox.Show(_owner, KX2.GetErrorCode(ret)); }
        }

        public void InitializeArm()
        {
            short ret;
            KX2.UseDefaultGripperState(false);
            ret = KX2.Initialize();
            HandleKX2ErrorCode(ret);
        }

        public async Task InitializeKX2Async()
        {
            await Task.Run(() =>
            {
                //// initialize KX2 object
                InitializeArm();
            });
        }

        //  interprets Carousel error codes
        public void CS6GetErrorCode(short ret)
        {
            //if (ret != 0) { MessageBox.Show(_owner, CS6.GetErrorCode(ret)); }
        }

        //  initialize CS
        public void InitializeCS()
        {
            short ret;
            ret = CS6.Initialize(Parameters.CarouselParameterFile);
            CS6GetErrorCode(ret);
        }

        public async Task InitializeCSAsync()
        {
            await Task.Run(() =>
            {
                //// initialize Carousel object
                InitializeCS();
            });
        }

        public void InitializeEachDeviceSeparately()
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

            //MessageBox.Show(_owner, "recieved event" + e.Event);

        }
        public void MovetoTeachPoint(string tp)
        {
            int Timeout = 0;
            byte Index = 0;
            KX2.TeachPointMoveTo(tp, Parameters.ArmSpeed, Parameters.ArmAccel, true, TimeoutMsec: ref Timeout, false, Index: ref Index);
        }
        public short GetPlateFromStage(string plateID)
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
            HandleKX2ErrorCode(ret);
            return ret;
        }

        public short GetPlateFromStage(string plateID, short resumeLine)
        {
            short ret;

            if (plateID.Contains("source"))
            {
                ret = KX2.ScriptResume("", "GetStageSourcePlate", resumeLine, true);
            }
            else if (plateID.Contains("destination"))
            {
                ret = KX2.ScriptResume("", "GetStageDestPlate", resumeLine, true);
            }
            else
            {
                throw new InvalidOperationException("Plate has to be source or destination");
            }
            HandleKX2ErrorCode(ret);
            return ret;
        }

        public short SetPlateToStage(string plateID)
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
            HandleKX2ErrorCode(ret);
            return ret;
        }
        public short SetPlateToStage(string plateID, short resumeLine)
        {
            short ret;

            if (plateID.Contains("source"))
            {
                ret = KX2.ScriptResume("", "PutStageSourcePlate", resumeLine, true);
            }
            else if (plateID.Contains("destination"))
            {
                ret = KX2.ScriptResume("", "PutStageDestPlate", resumeLine, true);
            }
            else
            {
                throw new InvalidOperationException("Plate has to be source or destination");
            }
            HandleKX2ErrorCode(ret);
            return ret;
        }

        public short GetPlateFromStack(string plateID, short stackCapacity, short location)
        {
            short ret;

            KX2.SetMovePathMode(KX2RobotControlNamespace.KX2RobotControl.eMovePathMode.Linear);

            if (plateID.Contains("source"))
            {
                string teachPoint = "Home";
                // Get arm position; if at SafeLow, move to Away_Far before going to destination stacker
                // Arm cannot move directly to destination stacker from SafeLow
                short armPosition = KX2.DetermineTeachPoint(TeachPoint: ref teachPoint, null);
                if (armPosition == 0)
                {
                    KX2.SetMovePathMode(KX2RobotControlNamespace.KX2RobotControl.eMovePathMode.Joint);
                    MovetoTeachPoint("SourceSafe");
                }

                KX2.SetMovePathMode(KX2RobotControlNamespace.KX2RobotControl.eMovePathMode.Linear);
                ret = KX2.RemovePlateFromHotel(Parameters.TopTeachpointSource, Parameters.BottomTeachpointSource,
                Parameters.RetractTeachpointSource, stackCapacity, location,
                    Parameters.GripperLiftHeight, Parameters.ArmSpeed, Parameters.GripperTimeDelay, true);
                KX2.SetMovePathMode(KX2RobotControlNamespace.KX2RobotControl.eMovePathMode.Joint);
            }
            else if (plateID.Contains("destination"))
            {
                string teachPoint = "SafeLow";
                // Get arm position; if at SafeLow, move to Away_Far before going to destination stacker
                // Arm cannot move directly to destination stacker from SafeLow
                short armPosition = KX2.DetermineTeachPoint(TeachPoint: ref teachPoint, null);
                if (armPosition == 0)
                {
                    KX2.SetMovePathMode(KX2RobotControlNamespace.KX2RobotControl.eMovePathMode.Joint);
                    MovetoTeachPoint("Away_Far");
                }

                KX2.SetMovePathMode(KX2RobotControlNamespace.KX2RobotControl.eMovePathMode.Linear);
                ret = KX2.RemovePlateFromHotel(Parameters.TopTeachpointDestination, Parameters.BottomTeachpointDestination,
                                    Parameters.RetractTeachpointDestination, stackCapacity, location,
                Parameters.GripperLiftHeight, Parameters.ArmSpeed, Parameters.GripperTimeDelay, true);
                // set movepathmode back to joint to avoid interpolation errors
                KX2.SetMovePathMode(KX2RobotControlNamespace.KX2RobotControl.eMovePathMode.Joint);

                MovetoTeachPoint("Away_Far");
            }
            else
            {
                throw new InvalidOperationException("Plate has to be source or destination");
            }
            HandleKX2ErrorCode(ret);

            return ret;
        }
        public short GetPlateFromStack(string plateID, short stackCapacity, short location, short resumeLine)
        {
            short ret;

            KX2.SetMovePathMode(KX2RobotControlNamespace.KX2RobotControl.eMovePathMode.Linear);
            //ret = KX2.RemovePlateFromHotel(TopTeachpoint, BottomTeachpoint, RetractTeachpoint, HotelCapacity, GetPlateSelfLocation(plate), GripperLiftHeight, ArmSpeed, GripperTimeDelay, true, 0, 0, Waypoint, true, true);

            if (plateID.Contains("source"))
            {
                ret = KX2.RemovePlateFromHotelResume(resumeLine, Parameters.TopTeachpointSource, Parameters.BottomTeachpointSource,
                Parameters.RetractTeachpointSource, stackCapacity, location,
                    Parameters.GripperLiftHeight, Parameters.ArmSpeed, Parameters.GripperTimeDelay, true);
            }
            else if (plateID.Contains("destination"))
            {
                ret = KX2.RemovePlateFromHotelResume(resumeLine, Parameters.TopTeachpointDestination, Parameters.BottomTeachpointDestination,
                Parameters.RetractTeachpointDestination, stackCapacity, location,
                Parameters.GripperLiftHeight, Parameters.ArmSpeed, Parameters.GripperTimeDelay, true);
            }
            else
            {
                throw new InvalidOperationException("Plate has to be source or destination");
            }
            HandleKX2ErrorCode(ret);

            KX2.SetMovePathMode(KX2RobotControlNamespace.KX2RobotControl.eMovePathMode.Joint);
            return ret;
        }

        public short SetPlateToStack(string plateID, short stackCapacity, short location)
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
                KX2.SetMovePathMode(KX2RobotControlNamespace.KX2RobotControl.eMovePathMode.Joint);
                MovetoTeachPoint("SafeLow");
                MovetoTeachPoint("Away_Far");
                KX2.SetMovePathMode(KX2RobotControlNamespace.KX2RobotControl.eMovePathMode.Linear);
                ret = KX2.PlacePlateInHotel(Parameters.TopTeachpointDestination, Parameters.BottomTeachpointDestination,
                Parameters.RetractTeachpointDestination, stackCapacity, location, Parameters.GripperLiftHeight,
                    Parameters.ArmSpeed, Parameters.GripperTimeDelay, true);
            }
            else
            {
                throw new InvalidOperationException("Plate has to be source or destination");
            }

            HandleKX2ErrorCode(ret);
            // set movepathmode back to joint to avoid interpolation errors
            KX2.SetMovePathMode(KX2RobotControlNamespace.KX2RobotControl.eMovePathMode.Joint);
            return ret;
        }
        public short SetPlateToStack(string plateID, short stackCapacity, short location, int resumeLine)
        {
            short ret;
            KX2.SetMovePathMode(KX2RobotControlNamespace.KX2RobotControl.eMovePathMode.Linear);

            if (plateID.Contains("source"))
            {
                ret = KX2.PlacePlateInHotelResume(resumeLine, Parameters.TopTeachpointSource, Parameters.BottomTeachpointSource,
                Parameters.RetractTeachpointSource, stackCapacity, location, Parameters.GripperLiftHeight,
                    Parameters.ArmSpeed, Parameters.GripperTimeDelay, true);
            }
            else if (plateID.Contains("destination"))
            {
                ret = KX2.PlacePlateInHotelResume(resumeLine, Parameters.TopTeachpointDestination, Parameters.BottomTeachpointDestination,
                Parameters.RetractTeachpointDestination, stackCapacity, location, Parameters.GripperLiftHeight,
                    Parameters.ArmSpeed, Parameters.GripperTimeDelay, true);
            }
            else
            {
                throw new InvalidOperationException("Plate has to be source or destination");
            }

            HandleKX2ErrorCode(ret);
            // set movepathmode back to joint to avoid interpolation errors
            KX2.SetMovePathMode(KX2RobotControlNamespace.KX2RobotControl.eMovePathMode.Joint);
            return ret;
        }

        public void RotateCarousel(int stackNum, string plateType)
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

        public async void StopAll()
        {
            // TODO do these at same time async
            m_spel.Stop(SpelStopType.StopAllTasks); // stops all Epson
            m_spel.ResetAbort();
            //AllRelaysOff(); // turn off all I/O devices
            KX2.EmergencyStop(); // stops Arm
            StopCS(); // stops carousel
        }

        public void OnShutdown()
        {
            if (m_spel != null)
            {
                AllRelaysOff(); // turn off all I/O devices 

                // unsubscribe from m_spel events to prevent memory leaks
                m_spel.EventReceived -= new RCAPINet.Spel.EventReceivedEventHandler(m_spel_EventReceived);

                // When your application exits, you need to execute Dispose for each Spel class instance. 
                // This can be done in your main form's FormClosed event. If Dispose is not executed, 
                // the application will not shutdown properly

                m_spel.MotorsOn = false;
                m_spel.Dispose();
            }

            if (KX2 != null)
            {
                // Shutdown KX2
                KX2.ShutDown();

                // Call this method just prior to closing the connection to the DLL to allow 
                // the DLL to close all forms and to terminate all classes.If this method is
                // not used, a memory-write error may occur.
                KX2.PreClassTerminateCleanup();
            }

            if (CS6 != null)
            {
                // shutdown carousel
                CS6 = null;
            }
        }
    }
}