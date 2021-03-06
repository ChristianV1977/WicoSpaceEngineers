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

        // assumes 'back' connector.
        // needs to support:
        //  other sides for connectors
        // merge block
        // rotor head
        // rotor base.
        // combined connector and merge block (ex, tradeship)

        // opening hangar doors after connection
        // close hangar doors before de-connect


        #region docking

        /*
        state
        0 master inint
        100 init antenna power
        101 wait for slow speed
          choose base.  Send request.
        102 wait for reply from base
        105  timeout while wiating for a reply from a base; try to move closer to base ->101
        106 collision calc from 105
        107 collision avoid from 106 ->101

        109 No known bases.  wait for reply

        //antSend("WICO:CON?:" + base.baseID, +":"+ "mini"+ ":"+ gpsCenter.CubeGrid.CustomName+":"+SaveFile.EntityId.ToString()+":"+Vector3DToString(gpsCenter.GetPosition() +

        Use other connector position and vector for docking
        110	Move to 'wait' location (or current location) ?request 'wait' location? ->115 or ->120

        111 calc colision avoid moving to 'mom'
        112 do travel to avoid collision moving to mom

        
        115 do travel to 'mom' location (range 4500) ->120

        120	request available docking connector

        130	settime-out
        131 wait for available
        140	when available, calculate approach locations
        150  Start:	Move through locations
        'Back' Connector:
        160 move to launch1
        161 collision detected.
        162 do travel for collision avoid ->160 Coll2->163
        163 secondary collision if asteroid ->164 else ->161
        164 init escape from inside asteroid/ship
        165 check for escape route when found ->162


        169 Delay for motion before 170
        170 align to docking alignment
        171 align to dock
        172 align to docking alignment
        173 'reverse' to dock, aiming connector at target connector
                supports 'back' connector
                TODO: support 'forward' connector
                TODO: support 'down' connector (kneeling required for wheeled vehicles?)

        Always:	Lock connector iMode->MODE_DOCKED
            */

        DockableConnector targetConnector = new DockableConnector();
        IMyTerminalBlock dockingConnector;

        int iPushCount = 0;

        Vector3D vAvoid;

        Vector3D vDockAlign;
        bool bDoDockAlign = false;

        int iTargetBase = -1;

        List<IMyTerminalBlock> thrustDockBackwardList = new List<IMyTerminalBlock>();
        List<IMyTerminalBlock> thrustDockForwardList = new List<IMyTerminalBlock>();
        List<IMyTerminalBlock> thrustDockLeftList = new List<IMyTerminalBlock>();
        List<IMyTerminalBlock> thrustDockRightList = new List<IMyTerminalBlock>();
        List<IMyTerminalBlock> thrustDockUpList = new List<IMyTerminalBlock>();
        List<IMyTerminalBlock> thrustDockDownList = new List<IMyTerminalBlock>();

        void doModeDocking()
        {
            StatusLog("clear", textPanelReport);
            StatusLog(moduleName + ":DOCKING!", textPanelReport);
            StatusLog(moduleName + ":Docking: current_state=" + current_state, textPanelReport);
            StatusLog(moduleName + ":Docking: current_state=" + current_state, textLongStatus, true);
            bWantFast = true;
            Echo("DOCKING: state=" + current_state);

            IMySensorBlock sb;

            if (dockingConnector == null) current_state = 0;

//            sInitResults += "DOCKING: state=" + current_state+"\n";

            if (current_state == 0)
            {
//                sInitResults = "DOCKING: state=" + current_state+"\n";
                if (AnyConnectorIsConnected())
                {
                    setMode(MODE_DOCKED);
                    return;
                }
                dockingConnector = getDockingConnector();
                if (dockingConnector == null)// || getAvailableRemoteConnector(out targetConnector))
                {
                    Echo("No local connector for docking");
                    StatusLog(moduleName + ":No local Docking Connector Availalbe!", textLongStatus, true);
                    // we could check for merge blocks.. or landing gears..
                    setMode(MODE_ATTENTION);
                    bWantFast = false;
                    return;
                }
                else
                {
                    ResetMotion();
                    turnDrillsOff();

                    thrustersInit(dockingConnector, ref thrustDockForwardList, ref  thrustDockBackwardList,
                        ref thrustDockDownList, ref thrustDockUpList,
                        ref thrustDockLeftList, ref thrustDockRightList);
                    current_state = 100;
                }
                iTargetBase = -1;
            }
            Vector3D vPos = dockingConnector.GetPosition();
            if (!AnyConnectorIsConnected() && AnyConnectorIsLocked())
            {
                ConnectAnyConnectors();
                ResetMotion();
                setMode(MODE_DOCKED);
                powerDownThrusters(thrustAllList, thrustAll, true);
                return;
            }
            if (current_state == 100)
            {
                antennaMaxPower();
                if (sensorsList.Count > 0)
                {
                    sb = sensorsList[0];
                    //			setSensorShip(sb, 1, 1, 1, 1, 50, 1);
                }
                current_state = 101;
            }
            else if (current_state == 101)
            { // wait for slow
                if (velocityShip < 10)
                {
                    if (iTargetBase < 0) iTargetBase = findBestBase();
                    //                    sInitResults += "101: Base=" + iTargetBase;
                   dtStartShip = DateTime.Now;
                   if (iTargetBase >= 0)
                    {
                        antSend("WICO:CON?:" + baseIdOf(iTargetBase).ToString() + ":" + "mini" + ":" + gpsCenter.CubeGrid.CustomName + ":" + SaveFile.EntityId.ToString() + ":" + Vector3DToString(gpsCenter.GetPosition()));
                        current_state = 102;
                    }
                    else // No available base
                    {
                        // try to get a base to respond
                        checkBases(true);
                        current_state = 109;
//                        setMode(MODE_ATTENTION);
                    }
                }
                else
                    ResetMotion();
            }
            else if (current_state == 102)
            { // wait for reply from base
                bWantFast = false;
                DateTime dtMaxWait = dtStartShip.AddSeconds(5.0f);
                DateTime dtNow = DateTime.Now;
                if (DateTime.Compare(dtNow, dtMaxWait) > 0)
                {
                    current_state = 105;
                    return;
                }
                if (sReceivedMessage != "")
                {
                    Echo("Received Message=\n" + sReceivedMessage);
                    string[] aMessage = sReceivedMessage.Trim().Split(':');
                    Echo(aMessage.Length + ": Length");
                    for (int i = 0; i < aMessage.Length; i++)
                        Echo(i + ":" + aMessage[i]);
                    if (aMessage.Length > 1)
                    {
                        if (aMessage[0] != "WICO")
                        {
                            Echo("not wico system message");
                            return;
                        }
                        if (aMessage.Length > 2)
                        {
                            if (aMessage[1] == "CONA")
                            {
                                Echo("Approach answer!");
                                //antSend("WICO:CONA:" + droneId +":" + SaveFile.EntityId.ToString(), +":"+Vector3DToString(vApproachPosition))

                                long id = 0;
                                long.TryParse(aMessage[2], out id);
                                if (id == SaveFile.EntityId)
                                {
                                    // it's a message for us.
                                    sReceivedMessage = ""; // we processed it.
                                    long.TryParse(aMessage[3], out id);
                                    double x, y, z;
                                    int iOff = 4;
                                    x = Convert.ToDouble(aMessage[iOff++]);
                                    y = Convert.ToDouble(aMessage[iOff++]);
                                    z = Convert.ToDouble(aMessage[iOff++]);
                                    Vector3D vPosition = new Vector3D(x, y, z);

                                    vHome = vPosition;
                                    bValidHome = true;
                                    //                                        StatusLog("clear", gpsPanel);
                                    //                                        debugGPSOutput("Home", vHome);

                                    current_state = 110;
                                }
                            }
                            // TODO: need to process CONF
                        }
                    }
                }
                else
                { // uses timeout from above
                    Echo("Awaiting reply message");
                }
            }
            else if (current_state == 105)
            { // timeout waiting for reply from base..
                // move closer to the chosen base's last known position.
                if (iTargetBase < 0)
                {
                    setMode(MODE_ATTENTION);
                    return;
                }
                doTravelMovement(basePositionOf(iTargetBase), 4000, 101, 106);
            }
            else if (current_state == 106)
            {
                calcCollisionAvoid(basePositionOf(iTargetBase));
                current_state = 107;
            }
            else if (current_state == 107)
            {
                doTravelMovement(vAvoid, 5.0f, 101, 106);
            }
            else if (current_state == 109)
            {
                // no known bases. requested response. wait for a while to see if we get one
                bWantFast = false;
                DateTime dtMaxWait = dtStartShip.AddSeconds(2.0f);
                DateTime dtNow = DateTime.Now;
                if (DateTime.Compare(dtNow, dtMaxWait) > 0)
                {
                    setMode(MODE_ATTENTION);
                    return;
                }
                if (findBestBase() >= 0)
                    current_state = 101;
            }

            else if (current_state == 110)
            { //110	Move to 'approach' location (or current location) ?request 'wait' location?
                if (bValidHome)
                {
                    double distancesqHome = Vector3D.DistanceSquared(vHome, gpsCenter.GetPosition());
                    if (distancesqHome > 25000) // max SG antenna range
                    {
                        current_state = 115;
                    }
                    else current_state = 120;
                }
                else current_state = 120;
            }
            else if (current_state == 111)
            { // collision detected
                calcCollisionAvoid(vHome);
                current_state = 112;
            }
            else if (current_state == 112)
            { // avoid collision
                doTravelMovement(vAvoid, 5.0f, 110, 111);
            }
            else if (current_state == 115)
            { // get closer to approach location
                doTravelMovement(vHome, 5.0f, 120, 111);
            }
            else if (current_state == 120)
            {//120	request available docking connector
                if (velocityShip < 5)
                {
                    antSend("WICO:COND?:" + baseIdOf(iTargetBase) + ":" + "mini" + ":" + gpsCenter.CubeGrid.CustomName + ":" + SaveFile.EntityId.ToString() + ":" + Vector3DToString(gpsCenter.GetPosition()));
                    {
                        dtStartShip = DateTime.Now;
                        current_state = 131;
                    }
                }
                else ResetMotion();
            }
            else if (current_state == 130)
            {
                dtStartShip = DateTime.Now;
                current_state = 131;
            }
            else if (current_state == 131)
            { //131	wait for available connector
                bWantFast = false;
                DateTime dtMaxWait = dtStartShip.AddSeconds(5.0f);
                DateTime dtNow = DateTime.Now;
                if (DateTime.Compare(dtNow, dtMaxWait) > 0)
                {
                    current_state = 0;
                    return;
                }
                if (getAvailableRemoteConnector(out targetConnector))
                {
                    current_state = 140;
                }
                else
                {
                    if (sReceivedMessage != "")
                    {
                        Echo("Received Message=\n" + sReceivedMessage);
                        string[] aMessage = sReceivedMessage.Trim().Split(':');
                        Echo(aMessage.Length + ": Length");
                        for (int i = 0; i < aMessage.Length; i++)
                            Echo(i + ":" + aMessage[i]);
                        if (aMessage.Length > 1)
                        {
                            if (aMessage[0] != "WICO")
                            {
                                Echo("not wico system message");
                                return;
                            }
                            if (aMessage.Length > 2)
                            {
                                //                                if (aMessage[1] == "DOCK" || aMessage[1] == "ADOCK")
                                if (aMessage[1] == "COND" || aMessage[1] == "ACOND")
                                {
                                    Echo("Docking answer!");
                                    // FORMAT:	antSend("WICO:DOCK:" + aMessage[3] + ":" + connector.EntityId + ":" + connector.CustomName + ":" + Vector3DToString(vPosition) + ":" + Vector3DToString(vVec));
                                    //	antSend("WICO:ADOCK:" + incomingID + ":" + connector.EntityId + ":" + connector.CustomName 	+ ":" + Vector3DToString(vPosition) + ":" + Vector3DToString(vVec)+":" + Vector3DToString(vAlign));

                                    long id = 0;
                                    long.TryParse(aMessage[2], out id);
                                    if (id == SaveFile.EntityId)
                                    {
                                        // it's a message for us.
                                        sReceivedMessage = ""; // we processed it.
                                        long.TryParse(aMessage[3], out id);
                                        string sName = aMessage[4];
                                        double x, y, z;
                                        int iOff = 5;
                                        x = Convert.ToDouble(aMessage[iOff++]);
                                        y = Convert.ToDouble(aMessage[iOff++]);
                                        z = Convert.ToDouble(aMessage[iOff++]);
                                        Vector3D vPosition = new Vector3D(x, y, z);

                                        x = Convert.ToDouble(aMessage[iOff++]);
                                        y = Convert.ToDouble(aMessage[iOff++]);
                                        z = Convert.ToDouble(aMessage[iOff++]);
                                        Vector3D vVec = new Vector3D(x, y, z);

                                        if (aMessage[1] == "ACOND")
                                        {
                                            x = Convert.ToDouble(aMessage[iOff++]);
                                            y = Convert.ToDouble(aMessage[iOff++]);
                                            z = Convert.ToDouble(aMessage[iOff++]);
                                            vDockAlign = new Vector3D(x, y, z);
                                            bDoDockAlign = true;
                                        }
                                        vDock = vPosition;
                                        vLaunch1 = vDock + vVec * 10; // should use shipdim..
                                        vHome = vDock + vVec * 30;
                                        bValidDock = true;
                                        bValidLaunch1 = true;
                                        bValidHome = true;
                                        StatusLog("clear", gpsPanel);
                                        debugGPSOutput("dock", vDock);
                                        debugGPSOutput("launch1", vLaunch1);
                                        debugGPSOutput("Home", vHome);

                                        current_state = 150;

                                    }
                                }
                                // TODO handle CONF
                            }
                        }

                    }
                    else
                    { // uses timeout from above
                        Echo("Awaiting reply message");
                    }
                }
            }
            else if (current_state == 140)
            { //140	when available, calculate approach locations

                vDock = targetConnector.vPosition;
                vLaunch1 = vDock + targetConnector.vVector * 10; // should use shipdim..
                vHome = vDock + targetConnector.vVector * 30;
                bValidDock = true;
                bValidLaunch1 = true;
                bValidHome = true;
                current_state = 150;
                StatusLog("clear", gpsPanel);
                debugGPSOutput("dock", vDock);
                debugGPSOutput("launch1", vLaunch1);
                debugGPSOutput("Home", vHome);
            }
            else if (current_state == 150)
            { //150  Start:	Move through locations
                current_state = 160;
                iPushCount = 0;
            }
            else if (current_state == 160)
            { //	160 move to home
                Echo("Moving to Home");
                //		if(iPushCount<60) iPushCount++;
                //		else
                doTravelMovement(vHome, 3.0f, 169, 161);
            }
            else if (current_state == 161)
            { //161 Collision detected
                calcCollisionAvoid(vHome);
                current_state = 162;
                iPushCount = 0;
            }
            else if (current_state == 162)
            {
                //		if(iPushCount<60) iPushCount++;
                //		else
                doTravelMovement(vAvoid, 5.0f, 160, 163);
                //		doTravelMovement(vAvoid, 5.0f, 160, 165);
            }
            else if (current_state == 163)
            {       // secondary collision

                if (lastDetectedInfo.Type == MyDetectedEntityType.Asteroid)
                {
                    current_state = 164;// setMode(MODE_ATTENTION);
                }
                else current_state = 161;
            }
            else if (current_state == 164)
            {
                initEscapeScan();
                dtStartShip = DateTime.Now;
                current_state = 165;
            }
            else if (current_state == 165)
            {
                DateTime dtMaxWait = dtStartShip.AddSeconds(5.0f);
                DateTime dtNow = DateTime.Now;
                if (DateTime.Compare(dtNow, dtMaxWait) > 0)
                {
                    setMode(MODE_ATTENTION);
                    return;
                }
                if (scanEscape())
                {
                    current_state = 162;
                }
            }
            else if (current_state == 169)
            {
                ResetMotion();
                Echo("Waiting for ship to stop");
                iPushCount = 0;
                if (velocityShip < 0.1f)
                {
                    current_state = 170;
                }
                else
                {
                    bWantFast = false;
                }
            }
            else if (current_state == 170 || current_state == 172)
            { //170 172 'reverse' to dock, aiming connector at dock location
              // align to docking alignment if needed
                turnEjectorsOff();
                if (!bDoDockAlign)
                {
                    current_state = 173;
                    return;
                }
                Echo("Aligning to dock");
                bool bAimed = false;
                minAngleRad = 0.03f;

                // may need to change direction if non- 'back' connector
                bAimed = GyroMain("up", vDockAlign, gpsCenter);
                if (bAimed) current_state++; // 170->171 172->173
            }
            else if (current_state == 171)
            { //171 align to dock
                Vector3D vTargetLocation = vDock;
                Vector3D vVec = vTargetLocation - dockingConnector.GetPosition();

                if (!bDoDockAlign)
                    current_state = 172;

                //		Vector3D vTargetLocation = gpsCenter.GetPosition() +vDockAlign;
                //		Vector3D vVec = vTargetLocation - gpsCenter.GetPosition();
                Echo("Aligning to dock");
                bool bAimed = false;
                minAngleRad = 0.03f;
                bAimed = GyroMain("forward", vVec, dockingConnector);
                if (bAimed) current_state = 172;

            }
            else if (current_state == 173)
            { //173 'reverse' to dock, aiming connector at dock location
                // needs a time-out for when misaligned or base connector moves.
                Echo("bDoDockAlign=" + bDoDockAlign);
                StatusLog(moduleName + ":Docking: Reversing to dock! Velocity=" + velocityShip.ToString("0.00"), textPanelReport);
                Echo("Reversing to Dock");
                CTRL_COEFF = 0.75;
                minAngleRad = 0.01f;

                Vector3D vTargetLocation = vDock;
                Vector3D vVec = vTargetLocation - dockingConnector.GetPosition();
                double distance = vVec.Length();
                Echo("distance=" + niceDoubleMeters(distance));
                Echo("velocity=" + velocityShip.ToString("0.00"));

                if (distance > 10)
                    minAngleRad = 0.03f;
                else
                    minAngleRad = 0.05f;

                debugGPSOutput("DockLocation", vTargetLocation);

                bool bAimed = false;
                /*
                        if ((craft_operation & CRAFT_MODE_SLED) > 0)
                        {
                            double yawangle = CalculateYaw(vTargetLocation, dockingConnector);
                            DoRotate(yawangle, "Yaw");
                            if (Math.Abs(yawangle) < .05) bAimed = true;
                        }
                        else
                */
                bAimed = GyroMain("forward", vVec, dockingConnector);

                if (bAimed)
                {
                    // we are aimed at location
                    Echo("Aimed");
                    if (distance > 15)
                    {
                        Echo(">15");
                        if (velocityShip < .5)
                        {
                            iPushCount++;
                            powerUpThrusters(thrustDockForwardList, 25+ iPushCount);
                        }
                        else if (velocityShip < 5)
                            powerUpThrusters(thrustDockForwardList, 1);
                        else
                            powerDownThrusters(thrustAllList);
                    }
                    else
                    {
                        Echo("<=15");
                        if (velocityShip < .5)
                        {
                            iPushCount++;
                            powerUpThrusters(thrustDockForwardList, 25 + iPushCount);
                        }
                        else if (velocityShip < 1.4)
                        {
                            powerUpThrusters(thrustDockForwardList, 1);
                            if (iPushCount > 0) iPushCount--;
                        }
                        else
                            powerDownThrusters(thrustAllList);

                    }
                }
                else
                {
                    Echo("Aiming");
                    powerDownThrusters(thrustAllList);
                }
            }
        }
        #endregion

        bool bScanLeft = true;
        bool bScanRight = true;
        bool bScanUp = true;
        bool bScanDown = true;
        bool bScanBackward = true;
        bool bScanForward = true;

        MyDetectedEntityInfo leftDetectedInfo = new MyDetectedEntityInfo();
        MyDetectedEntityInfo rightDetectedInfo = new MyDetectedEntityInfo();
        MyDetectedEntityInfo upDetectedInfo = new MyDetectedEntityInfo();
        MyDetectedEntityInfo downDetectedInfo = new MyDetectedEntityInfo();
        MyDetectedEntityInfo backwardDetectedInfo = new MyDetectedEntityInfo();
        MyDetectedEntityInfo forwardDetectedInfo = new MyDetectedEntityInfo();

        void initEscapeScan()
        {
            bScanLeft = true;
            bScanRight = true;
            bScanUp = true;
            bScanDown = true;
            bScanBackward = false;// don't rescan where we just came from..
                                  //	bScanBackward = true;
            bScanForward = true;
            leftDetectedInfo = new MyDetectedEntityInfo();
            rightDetectedInfo = new MyDetectedEntityInfo();
            upDetectedInfo = new MyDetectedEntityInfo();
            downDetectedInfo = new MyDetectedEntityInfo();
            backwardDetectedInfo = new MyDetectedEntityInfo();
            forwardDetectedInfo = new MyDetectedEntityInfo();

            // don't assume all drones have all cameras..
            if (cameraLeftList.Count < 1) bScanLeft = false;
            if (cameraRightList.Count < 1) bScanRight = false;
            if (cameraUpList.Count < 1) bScanUp = false;
            if (cameraDownList.Count < 1) bScanDown = false;
            if (cameraForwardList.Count < 1) bScanForward = false;
            if (cameraBackwardList.Count < 1) bScanBackward = false;

        }

        bool scanEscape()
        {
            MatrixD worldtb = gpsCenter.WorldMatrix;
            Vector3D vVec = worldtb.Forward;

            if (bScanLeft)
            {
                if (doCameraScan(cameraLeftList, 200))
                {
                    bScanLeft = false;
                    leftDetectedInfo = lastDetectedInfo;
                    if (lastDetectedInfo.IsEmpty())
                    {
                        vVec = worldtb.Left;
                        vVec.Normalize();
                        vAvoid = gpsCenter.GetPosition() + vVec * 200;
                        return true;
                    }
                }
            }
            if (bScanRight)
            {
                if (doCameraScan(cameraRightList, 200))
                {
                    bScanRight = false;
                    rightDetectedInfo = lastDetectedInfo;
                    if (lastDetectedInfo.IsEmpty())
                    {
                        vVec = worldtb.Right;
                        vVec.Normalize();
                        vAvoid = gpsCenter.GetPosition() + vVec * 200;
                        return true;
                    }
                }
            }
            if (bScanUp)
            {
                if (doCameraScan(cameraUpList, 200))
                {
                    upDetectedInfo = lastDetectedInfo;
                    bScanUp = false;
                    if (lastDetectedInfo.IsEmpty())
                    {
                        vVec = worldtb.Up;
                        vVec.Normalize();
                        vAvoid = gpsCenter.GetPosition() + vVec * 200;
                        return true;
                    }
                }
            }
            if (bScanDown)
            {
                if (doCameraScan(cameraDownList, 200))
                {
                    downDetectedInfo = lastDetectedInfo;
                    bScanDown = false;
                    if (lastDetectedInfo.IsEmpty())
                    {
                        vVec = worldtb.Down;
                        vVec.Normalize();
                        vAvoid = gpsCenter.GetPosition() + vVec * 200;
                        return true;
                    }
                }
            }
            if (bScanBackward)
            {
                if (doCameraScan(cameraBackwardList, 200))
                {
                    backwardDetectedInfo = lastDetectedInfo;
                    bScanBackward = false;
                    if (lastDetectedInfo.IsEmpty())
                    {
                        vVec = worldtb.Backward;
                        vVec.Normalize();
                        vAvoid = gpsCenter.GetPosition() + vVec * 200;
                        return true;
                    }
                }
            }
            if (bScanForward)
            {
                if (doCameraScan(cameraForwardList, 200))
                {
                    bScanForward = false;
                    if (lastDetectedInfo.IsEmpty())
                    {
                        vVec = worldtb.Forward;
                        vVec.Normalize();
                        vAvoid = gpsCenter.GetPosition() + vVec * 200;
                        return true;
                    }
                }
            }
            if (bScanForward || bScanBackward || bScanUp || bScanDown || bScanLeft || bScanRight)
                return false; // still more scans to go

            // nothing was 'clear'.  find longest vector.
            MyDetectedEntityInfo furthest = backwardDetectedInfo;
            Vector3D currentpos = gpsCenter.GetPosition();
            vVec = worldtb.Backward;
            if (furthest.HitPosition == null || leftDetectedInfo.HitPosition != null && Vector3D.DistanceSquared(currentpos, (Vector3D)furthest.HitPosition) < Vector3D.DistanceSquared(currentpos, (Vector3D)leftDetectedInfo.HitPosition))
            {
                vVec = worldtb.Left;
                furthest = leftDetectedInfo;
            }
            if (furthest.HitPosition == null || rightDetectedInfo.HitPosition != null && Vector3D.DistanceSquared(currentpos, (Vector3D)furthest.HitPosition) < Vector3D.DistanceSquared(currentpos, (Vector3D)rightDetectedInfo.HitPosition))
            {
                vVec = worldtb.Right;
                furthest = rightDetectedInfo;
            }
            if (furthest.HitPosition == null || upDetectedInfo.HitPosition != null && Vector3D.DistanceSquared(currentpos, (Vector3D)furthest.HitPosition) < Vector3D.DistanceSquared(currentpos, (Vector3D)upDetectedInfo.HitPosition))
            {
                vVec = worldtb.Up;
                furthest = upDetectedInfo;
            }
            if (furthest.HitPosition == null || downDetectedInfo.HitPosition != null && Vector3D.DistanceSquared(currentpos, (Vector3D)furthest.HitPosition) < Vector3D.DistanceSquared(currentpos, (Vector3D)downDetectedInfo.HitPosition))
            {
                vVec = worldtb.Down;
                furthest = downDetectedInfo;
            }
            if (furthest.HitPosition == null || forwardDetectedInfo.HitPosition != null && Vector3D.DistanceSquared(currentpos, (Vector3D)furthest.HitPosition) < Vector3D.DistanceSquared(currentpos, (Vector3D)forwardDetectedInfo.HitPosition))
            {
                vVec = worldtb.Forward;
                furthest = forwardDetectedInfo;
            }
            if (furthest.HitPosition == null) return false;

            double distance = Vector3D.Distance(currentpos, (Vector3D)furthest.HitPosition);
            if (distance > 15)
            {
                vVec.Normalize();
                vAvoid = gpsCenter.GetPosition() + vVec * distance / 2;
                return true;
            }

            return false;
        }

        void calcCollisionAvoid(Vector3D vTargetLocation)
        {
            Echo("Collsion Detected");
            Vector3D vHit;
            if (lastDetectedInfo.HitPosition.HasValue)
                vHit = (Vector3D)lastDetectedInfo.HitPosition;
            else
                vHit = gpsCenter.GetPosition();

            Vector3D vCenter = lastDetectedInfo.Position;
            //	Vector3D vTargetLocation = vHome;
            //vTargetLocation;
            debugGPSOutput("TargetLocation", vTargetLocation);
            debugGPSOutput("HitPosition", vHit);
            debugGPSOutput("CCenter", vCenter);

            Vector3D vVec = (vCenter - vHit);
            vVec.Normalize();

            Vector3D vMinBound = lastDetectedInfo.BoundingBox.Min;
            debugGPSOutput("vMinBound", vMinBound);
            Vector3D vMaxBound = lastDetectedInfo.BoundingBox.Max;
            debugGPSOutput("vMaxBound", vMaxBound);

            double radius = (vCenter - vMinBound).Length();
            Echo("Radius=" + radius.ToString("0.00"));

            vAvoid = vCenter - vVec * (radius + shipDim.WidthInMeters() * 5);
            //	 Vector3D gpsCenter.GetPosition() - vAvoid;

            double distancesq = Vector3D.DistanceSquared(vAvoid, gpsCenter.GetPosition());
            if (distancesq < 30)
            {
                // we are already close to avoid.  try a different method.
                var cross = Vector3D.Cross(vTargetLocation, vCenter);
                debugGPSOutput("cross", cross);
            }
            debugGPSOutput("vAvoid", vAvoid);
        }
    }
}