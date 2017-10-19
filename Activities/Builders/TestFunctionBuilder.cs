using System;
using System.Collections.Generic;
using Ventana.Core.Activities.Machines;
using Ventana.Core.Base;
using Ventana.Core.Base.Activities;
using Ventana.Core.Base.BusinessObjects;
using Ventana.Core.Logging;

namespace Ventana.Core.Activities.Builders
{
    public class TestFunctionBuilder : ActivityMachineBuilder
    {
        private const double ScriptExecutingExpirationMins = 3.0;

        public override void Build(IActivityMachine machine)
        {
            //var station = MachineConfiguration.Data.Stations[0];

            //var funkyMachine = activityMachine as TestFunctionActivityMachine;
            //if (funkyMachine != null)
            //{
            //    // Let all machines force quit if the instrument stops.
            //    funkyMachine.SetQuitOnInstrumentState(InstrumentState.Stopped);

            //    switch (MachineConfiguration.TestFunctionType)
            //    {
            //        case TestFunctionType.ScriptExecuter:
            //            AssembleScriptExecutingTestFunction(activityMachine as TestFunctionActivityMachine, MachineConfiguration.Executive, MachineConfiguration.StandardHandler, MachineConfiguration.Instrument, station, MachineConfiguration.ScriptNames);
            //            break;

            //        default:
            //            LogService.Log(LogType.System, LogMessageType.Warning, GetType().Name, "Attempted to build a TestFunctionActivityMachine for unknown function type.");
            //            break;
            //    }
            //}
        }

        public void AssembleScriptExecutingTestFunction(TestFunctionActivityMachine activityMachine, IInstrument instrument, IStation station, List<string> scriptNames)
        {
            //if (station.State == StationState.Idle)
            //{
            //    activityMachine.ExpirationTimespan = TimeSpan.FromMinutes(ScriptExecutingExpirationMins);
            //    // Test function should stop running if module becomes Stopped, Disabled or Error.
            //    activityMachine.SetQuitOnStationStatus(StationState.Error);
            //    activityMachine.SetQuitOnStationStatus(StationState.Disabled);
            //    activityMachine.SetQuitOnStationStatus(StationState.Stopped);

            //    // Ensure instrument is in maintenance state before begining a test function.
            //    activityMachine.SetPauseUntilInstrumentStatus(InstrumentState.TestFunction);

            //    // Ensure station is Idle before beginning any script executing test function.
            //    activityMachine.SetPauseUntilStationStatus(StationState.Idle);

            //    // Run a maintenance script as soon as the station available.  If the executive call fails,
            //    // give the indicated error code to an StandardHandler.
            //    activityMachine.SetActivityPerformMaintenance(scriptNames[0], "99000");

            //    // Ensure that the module did indeed move to maintenance state.
            //    activityMachine.SetPauseUntilStationStatus(StationState.Maintenance);

            //    // Only indicate completion after the maintenance call sets the station back to the beginning state of Idle.
            //    activityMachine.SetPauseUntilStationStatus(StationState.Idle);
            //}
            //else
            //{
            //    // Invalidate the activity machine right away.
            //    activityMachine.Quit();
            //}
        }
    }
}
