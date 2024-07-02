//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;
//using System.Windows;
//using System.Windows.Documents;
//using Integration;
//using RCAPINet;

//namespace PinTransferWPF
//{
//    public class EpsonCommands
//    {
//        public void HomeEpson(Spel m_spel)
//        {
//            GetSpelErrorMessage(m_spel);
//            m_spel.Call("MoveHome");
//        }
//        public void RebuildEspon(Spel m_spel)
//        {
//            GetSpelErrorMessage(m_spel);
//            m_spel.RebuildProject();
//        }
//        public void AttachPinTool(Spel m_spel)
//        {
//            GetSpelErrorMessage(m_spel);
//            if (!run.pinToolAttached) // put on pin tool if not attached
//            {
//                if (run.PinToolSizeAttached == "LG" && !run.pin96)
//                {
//                    m_spel.Call("AttachLG");
//                }
//                else if (run.PinToolSizeAttached == "MD")
//                {
//                    m_spel.Call("AttachMD");
//                }
//                else if (run.PinToolSizeAttached == "SM")
//                {
//                    m_spel.Call("AttachSM");
//                }
//                else if (run.PinToolSizeAttached == "96")
//                {
//                    m_spel.Call("Attach96");
//                }
//            }
//            run.pinToolAttached = true;
//        }

//        public void DetachPinTool(stringSpel m_spel)
//        {
//            GetSpelErrorMessage(m_spel);

//            if (run.pinToolAttached) // put on pin tool if not Detached
//            {
//                if (run.PinToolSizeAttached == "LG" && !run.pin96)
//                {
//                    m_spel.Call("DetachLG");
//                }
//                else if (run.PinToolSizeAttached == "MD")
//                {
//                    m_spel.Call("DetachMD");
//                }
//                else if (run.PinToolSizeAttached == "SM")
//                {
//                    m_spel.Call("DetachSM");
//                }
//                else if (run.PinToolSizeAttached == "96")
//                {
//                    m_spel.Call("Detach96");
//                }
//            }
//            run.pinToolAttached = false;
//        }
//        public void RunPinTransfer(DestinationPlate destinationPlate, Spel m_spel) // m_spel calls wait for task to complete before returning control to main app
//        {
//            GetSpelErrorMessage(m_spel);

//            if (run.StageDestContains == null || run.StageDestContains == null)
//            {
//                throw new InvalidOperationException("Plates Missing");
//            } // check for plates on stage
//            else
//            {
//                if (run.PinToolSizeAttached == "LG")
//                {
//                    m_spel.Call("TransferLG");

//                }
//                else if (run.PinToolSizeAttached == "MD")
//                {
//                    if (run.pinLowVolume == true)
//                    {
//                        m_spel.Call("TransferMDLV"); //for low volume plate e.g. Corning 3820 transfers
//                    }
//                    else
//                    {
//                        m_spel.Call("TransferMD");
//                    }

//                }
//                else if (run.PinToolSizeAttached == "SM")
//                {
//                    if (run.pinLowVolume == true)
//                    {
//                        m_spel.Call("TransferSMLV"); //for low volume plate e.g. Corning 3820 transfers
//                    }
//                    else
//                    {
//                        m_spel.Call("TransferSM");
//                    }
//                }

//                else if (destinationPlate.Quadrant == "A1")
//                {
//                    m_spel.Call("Transfer96_A1");
//                }

//                else if (destinationPlate.Quadrant == "A2")
//                {
//                    m_spel.Call("Transfer96_A2");
//                }
//                else if (destinationPlate.Quadrant == "B1")
//                {
//                    m_spel.Call("Transfer96_B1");
//                }
//                else if (destinationPlate.Quadrant == "B2")
//                {
//                    m_spel.Call("Transfer96_B2");
//                }

//                destinationPlate.PlateIsPinned = true; // track pinned status on dest plates

//                run.pinToolClean = false;
//            }
//        }
//        public void InitializeSpelAsync(Spel m_spel)
//        {
//            m_spel.Initialize();


//            m_spel.Project = m_spel_ProjectFilePath;// \ is escape character
//            m_spel.Reset();
//            m_spel.RebuildProject();

//            m_spel.EventReceived += new RCAPINet.Spel.EventReceivedEventHandler(m_spel_EventReceived);
//            m_spel.AsyncMode = true; // When the AsyncMode property is true and you execute an asynchronous method,
//            // the method will be started and control will return immediately back to
//            // the.NET application for further processing.
//            m_spel.EnableEvent(SpelEvents.AllTasksStopped, false); // gets rid of return prompt that m_spel return after executing a task
//            //m_spel.DisableMsgDispatch = true;
//        }

//        public void GetSpelErrorMessage(Spel m_spel)
//        {
//            string msg;

//            if (m_spel.ErrorOn)
//            {
//                msg = m_spel.GetErrorMessage(m_spel.ErrorCode);
//                MessageBox.Show(msg);

//            }
//        }
//    }
//}
