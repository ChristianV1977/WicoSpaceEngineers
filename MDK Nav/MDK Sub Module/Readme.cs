﻿/* Wico Craft NAV Control sub-module
	* 
	* Handles MODES:
	* MODE_DOCKED
	* MODE_LAUNCH
	* MODE_RELAUNCH
    * MODE_SLEDMOVE
    * MODE_ARRIVEDTARGET
	* 
	* 2.0.4 Upate to new save format
	*  .04A Camera Scans for Obstacles...!!!one
	*  
	*  2.1 Use new blockInint and localgrids
	*  
	*  .1g Add Docked
	*  copy from SLED PATROL
	*  .1h fixed yaw only gyromain
	*  .1i tested in space. Added !NAV to gyro check
	*  .1j add doroll
	*  .1k use (and fix/test) IMyGyroControl
	2.2: Update for 1.72

    2.9 Copy from Sled Dock 2.2

    Needs LOTS of updates.

    3.0 Move code into 3.0
    
    3.0A Start NAV processing: W and O
    3.0B Add D, S, C
    3.0C Add arrivedtarget

    3.0D 110517  search order for text panels

    3.1 Version for PB Updates SE 1.185
    o Added support for GPS-formatted nav locations
        Ex:  W GPS:Wicorel #1:53970.01:128270.31:-123354.92:

    3.1a
    remove blockApplyActions() and make routines for each block type that needs it


    Added Rotors

	* TODO: 
	*/
