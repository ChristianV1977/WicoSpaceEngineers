﻿using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System;
using VRage.Collections;
using VRage.Game.Components;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Game;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        void doModuleConstructor()
        {
            // called from main constructor.

        }

        #region maininit

        string sInitResults = "";
        string sArgResults = "";

        int currentInit = 0;

        string doInit()
        {

            // initialization of each module goes here:

            // when all initialization is done, set init to true.

            Log("Init:" + currentInit.ToString());
            double progress = currentInit * 100 / 3;
            string sProgress = progressBar(progress);
            StatusLog(moduleName + sProgress, textPanelReport);

            Echo("Init:" + currentInit.ToString());
            if (currentInit == 0)
            {
                //StatusLog("clear",textLongStatus,true);
                StatusLog(DateTime.Now.ToString() + " " + OurName + ":" + moduleName + ":INIT", textLongStatus, true);
                if (modeCommands != null)
                {
                    if (!modeCommands.ContainsKey("launchprep")) modeCommands.Add("launchprep", MODE_LAUNCHPREP);
                    if (!modeCommands.ContainsKey("orbitallaunch")) modeCommands.Add("orbitallaunch", MODE_ORBITALLAUNCH);
                    // if(!modeCommands.ContainsKey("orbitaldescent")) modeCommands.Add("orbitaldescent", MODE_DESCENT);
                }
                else Echo("NULL modeCommands!");
                gridsInit();
                initLogging();
                sInitResults += initSerializeCommon();
                Deserialize();
            }
            else if (currentInit == 1)
            {
                sInitResults += BlockInit();
                anchorPosition = gpsCenter;
                currentPosition = anchorPosition.GetPosition();
                sInitResults += connectorsInit();
                sInitResults += thrustersInit(gpsCenter);
                sInitResults += camerasensorsInit(gpsCenter);

                /*
                }
                else if(currentInit==2)
                {
                */
                sInitResults += gearsInit();
                sInitResults += tanksInit();

                sInitResults += NAVInit();
                sInitResults += gyrosetup();
                sInitResults += doorsInit();
                sInitResults += landingsInit(gpsCenter);

                Deserialize();


                // autoConfig();
                bWantFast = false;
                sInitResults += modeOnInit(); // handle mode initializting from load/recompile..
                init = true;
            }

            currentInit++;
            if (init) currentInit = 0;

            Log(sInitResults);

            return sInitResults;

        }

        IMyTextPanel gpsPanel = null;

        string BlockInit()
        {
            string sInitResults = "";
            Echo("#localgrids=" + localGrids.Count);
            List<IMyTerminalBlock> centerSearch = new List<IMyTerminalBlock>();
            GridTerminalSystem.SearchBlocksOfName(sGPSCenter, centerSearch, (x1 => x1.CubeGrid == Me.CubeGrid));
            // GridTerminalSystem.SearchBlocksOfName(sGPSCenter, centerSearch);
            if (centerSearch.Count == 0)
            {
                centerSearch = GetBlocksContains<IMyRemoteControl>("[NAV]");
                if (centerSearch.Count == 0)
                {

                    GridTerminalSystem.GetBlocksOfType<IMyRemoteControl>(centerSearch, localGridFilter);

                    if (centerSearch.Count == 0)
                    {
                        // didn't find an RC.  try 'cockpits'
                        GridTerminalSystem.GetBlocksOfType<IMyCockpit>(centerSearch, localGridFilter);
                        //                GridTerminalSystem.GetBlocksOfType<IMyShipController>(centerSearch, localGridFilter);
                        int i = 0;
                        for (; i < centerSearch.Count; i++)
                        {
                            Echo("Checking Controller:" + centerSearch[i].CustomName);
                            if (centerSearch[i] is IMyCryoChamber)
                                continue;
                            break;
                        }
                        if (i > centerSearch.Count || i == 0)
                        {
                            sInitResults += "!!NO valid Controller";
                            Echo("No Controller found");
                        }
                        else
                        {
                            sInitResults += "S";
                            Echo("Using good ship Controller: " + centerSearch[i].CustomName);
                        }

                    }
                    else
                    {
                        sInitResults += "R";
                        Echo("Using first remote control: " + centerSearch[0].CustomName);
                        //                        gpsCenter = centerSearch[0];
                    }
                }
            }
            else
            {
                sInitResults += "N";
                Echo("Using Named: " + centerSearch[0].CustomName);
            }
            //            if (centerSearch.Count > 0)
            gpsCenter = centerSearch[0];
            //          else gpsCenter = null;

            List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
            blocks = GetBlocksContains<IMyTextPanel>("[GPS]");
            if (blocks.Count > 0)
                gpsPanel = blocks[0] as IMyTextPanel;

            return sInitResults;
        }
        string modeOnInit()
        {

            return ">";
        }

        #endregion


    }
}