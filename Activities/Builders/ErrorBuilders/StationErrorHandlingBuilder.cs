using System;
using System.Globalization;
using Ventana.Core.Activities.Executables;
using Ventana.Core.Activities.Machines;
using Ventana.Core.Activities.Parts;
using Ventana.Core.Activities.SpecializedTriggers;
using Ventana.Core.Base;
using Ventana.Core.Base.Activities;
using Ventana.Core.Base.BusinessObjects;
using Ventana.Core.Base.ExceptionHandling;
using Ventana.Core.Base.Processors;
using Ventana.Core.ExceptionHandling;
using Ventana.Core.Logging;
using Ventana.Core.Utilities.ExtensionMethods;

namespace Ventana.Core.Activities.Builders
{
    public class StationErrorHandlingBuilder : ErrorHandlingBuilderBase
    {
        private const bool AllowStationEnableAfterError = false;
        private static readonly Func<IStation, bool> IsStationStoppedOrDisabled = s =>
            s.State.IsOneOf(StationState.Stopped, StationState.Disabled);

        private static readonly Func<IStation, bool> IsStationNotStoppedOrDisabled = s =>
            s.State.IsNoneOf(StationState.Stopped, StationState.Disabled);

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        public override void Build(IActivityMachine machine)
        {
            var ex = machine.Configuration.Data.Exception as AtlasException;
            var stationMachine = machine as ErrorHandlingActivityMachine;
            
            if (ex != null && stationMachine != null)
            {
                var station = machine.Configuration.Data.Station as IStation;
                if (station != null)
                {
                    //// Don't assemble machines for PortalBays or GarageBays.
                    //if (station.Type != StationType.PortalBay && station.Type != StationType.GarageBay)
                    //{
                    //    return;
                    //}

                    stationMachine.SetStationAndTrayReferences(station);
                }

                //TODO: change this time.
                stationMachine.ExpirationTimespan = TimeSpan.FromMinutes(45);
                stationMachine.Instrument = machine.Configuration.Data.Instrument as IInstrument;
                stationMachine.InstrumentManager = machine.Configuration.Data.InstrumentManager as IInstrumentManager;
                stationMachine.Engine = machine.Configuration.Data.Engine as IExecutableEngine;
                
                if (stationMachine.TrayProcessor != null)
                {
                    stationMachine.UseRuntimeTrigger(new PropertyChangedTrigger("StationStateChanged", stationMachine.TrayProcessor,
                        stationMachine.TrayProcessor.PropertyToString(() => stationMachine.TrayProcessor.State)));
                }

                //Raise error dialog
                SetNotificationActivities(stationMachine, ex);

                if (!(ex.Station is ITrayProcessor))
                {
                    LogService.Log(LogType.System, LogMessageType.Error, GetType().Name,
                        string.Format(CultureInfo.InvariantCulture, "Cannot perform {0} {1} exception handling without a station.", ex.Severity, ex.Code));
                    return;
                }

                AssembleMachineForTrayProcessor(ex, stationMachine);
            }
        }

        /// <summary>
        /// allow the station to finish processing, then stop and disable the station.
        /// </summary>
        /// <param name="activityMachine"></param>
        /// <param name="instrument"></param>
        /// <param name="e"></param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(System.String,System.String)")]
        public void AssembleMachineForStandardError(ErrorHandlingActivityMachine activityMachine, IInstrument instrument, AtlasException e)
        {
            var trayProcessor = e.Station as ITrayProcessor;

            AssembleMachineForQuitOnErrorEscalation(activityMachine, e);

            SetDegradedModeActivity(activityMachine, e);

            activityMachine.UseFinalExitBehavior(new DynamicActivity("Release locks", () => trayProcessor.ReleaseLock(activityMachine)));

            // Lock was obtained by machine CTOR, so it's safe to assemble the machine based on current station state.
            // Only assemble a machine for an error if the station is running.
            activityMachine.AddFinishOrContinueCondition(
                new DynamicConstraint<IStation>("StationStoppedOrDisabled", e.Station, IsStationStoppedOrDisabled),
                new DynamicConstraint<IStation>("StationNotStoppedOrDisabled", e.Station, IsStationNotStoppedOrDisabled));

            // Try to set the station's error and quit if it already had one set.
            activityMachine.AddActivity(new ActOnResultActivity<bool>("SetError", () => e.Station.SetError(e), false, () => activityMachine.Quit("SetError returned false")));
            
            // if tray present notify of a process anomaly occurred. 
            if (activityMachine.Tray != null)
            {
                activityMachine.UseRuntimeTrigger(new PropertyChangedTrigger("TrayStateChanged", activityMachine.Tray, "State"));

                // Tell the machine to quit acting if the tray is lost.
                activityMachine.SetQuitOnTrayState(TrayState.Lost);

                activityMachine.AddActivity(new DynamicActivity(string.Format(CultureInfo.InvariantCulture, "Notify Tray ProcessAnomaly {0}"
                    , trayProcessor.Name), () => activityMachine.Tray.OnProcessingAnomalyOccurred(e)));

                // Pause here until the station is done with the current process and the tray has been retrieved.
                activityMachine.AddContinueCondition(new DynamicConstraint<IStation>(trayProcessor.Name + " tray processing finished?", trayProcessor as IStation
                    , s => s.State == StationState.Idle && !(s as ITrayDetector).IsTrayDetected));
            }

            // Now get a resource lock for the error handling machine.
            activityMachine.AddActivity(new DynamicActivity(string.Format(CultureInfo.InvariantCulture, "Wait For Lock on {0}", trayProcessor.Name), () => trayProcessor.WaitForLock(activityMachine)));

            activityMachine.AddQuitOrContinueCondition(new DynamicConstraint<ITrayProcessor>("StationState == StationState.Disabled", trayProcessor, s => s.State == StationState.Disabled), new DynamicConstraint<ITrayProcessor>("StationState != StationState.Disabled", trayProcessor, s => s.State != StationState.Disabled));
            // Disable the station once processing is done and allow user to re-enable.
            activityMachine.SetActivityDisable(AllowStationEnableAfterError);

            // Make this machine wait until the station reports that it is indeed disabled by disable().
            activityMachine.SetPauseUntilStationStatus(StationState.Disabled);

            activityMachine.AddActivity(new DynamicActivity(string.Format(CultureInfo.InvariantCulture, "Release Lock on {0}", trayProcessor.Name), () => trayProcessor.ReleaseLock(activityMachine)));
        }

        /// <summary>
        /// If a tray in in the module, abort it, stop and disable station
        /// </summary>
        /// <param name="activityMachine"></param>
        /// <param name="instrument"></param>
        /// <param name="e"></param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(System.String,System.String)")]
        public void AssembleMachineForSevereError(ErrorHandlingActivityMachine activityMachine, IInstrument instrument, AtlasException e)
        {
            var trayProcessor = e.Station as ITrayProcessor;

            AssembleMachineForQuitOnErrorEscalation(activityMachine, e);

            SetDegradedModeActivity(activityMachine, e);
            activityMachine.UseFinalExitBehavior(new DynamicActivity("Release locks", () => trayProcessor.ReleaseLock(activityMachine)));

            // Only assemble a machine for an error if the station is running.
            activityMachine.AddFinishOrContinueCondition(
                new DynamicConstraint<IStation>("StationStoppedOrDisabled", e.Station, IsStationStoppedOrDisabled),
                new DynamicConstraint<IStation>("StationNotStoppedOrDisabled", e.Station, IsStationNotStoppedOrDisabled));

            // Try to set the station's error and quit if it already had one set.
            activityMachine.AddActivity(new ActOnResultActivity<bool>("SetError", () => e.Station.SetError(e), false, () => activityMachine.Quit("SetError returned false")));

            AssembleMachineForAbortTray(activityMachine, instrument, e);
            // Now get a resource lock for the error handling machine.
            activityMachine.AddActivity(new DynamicActivity(string.Format(CultureInfo.InvariantCulture, "Wait For Lock on {0}", trayProcessor.Name), 
                () => trayProcessor.WaitForLock(activityMachine)));
            
            activityMachine.AddQuitOrContinueCondition(new DynamicConstraint<ITrayProcessor>("StationState == StationState.Disabled", 
                trayProcessor, s => s.State == StationState.Disabled), new DynamicConstraint<ITrayProcessor>("StationState != StationState.Disabled", 
                    trayProcessor, s => s.State != StationState.Disabled));
            // Disable the station when it is finished processing.
            activityMachine.SetActivityDisable(AllowStationEnableAfterError);

            activityMachine.AddActivity(new DynamicActivity(string.Format(CultureInfo.InvariantCulture, "Release Lock on {0}", trayProcessor.Name), 
                () => trayProcessor.ReleaseLock(activityMachine)));

        }

        /// <summary>
        /// stop station immediately and set the tray to lost
        /// </summary>
        /// <param name="activityMachine"></param>
        /// <param name="instrument"></param>
        /// <param name="e"></param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(System.String,System.String)")]
        public void AssembleMachineForCriticalError(ErrorHandlingActivityMachine activityMachine, IInstrument instrument, AtlasException e)
        {
            var trayProcessor = e.Station as ITrayProcessor;

            SetDegradedModeActivity(activityMachine, e);

            // Redundant release for assurance
            activityMachine.UseFinalExitBehavior(new DynamicActivity("Release locks", () => trayProcessor.ReleaseLock(activityMachine)));

            // Only assemble a machine for an error if the station is running.
            activityMachine.AddFinishOrContinueCondition(
                new DynamicConstraint<IStation>("StationStoppedOrDisabled", e.Station, IsStationStoppedOrDisabled),
                new DynamicConstraint<IStation>("StationNotStoppedOrDisabled", e.Station, IsStationNotStoppedOrDisabled));


            // Now get a resource lock for the error handling machine.
            activityMachine.AddActivity(new DynamicActivity(string.Format(CultureInfo.InvariantCulture, "Wait For Lock on {0}", trayProcessor.Name), 
                () => trayProcessor.WaitForLock(activityMachine)));


            // Try to set the station's error and quit if it already had one set.
            activityMachine.AddActivity(new ActOnResultActivity<bool>("SetError", () => e.Station.SetError(e), false, () => activityMachine.Quit("SetError returned false")));

            // Set the tray to error, which tells scheduler to leave it in the module.
            if (activityMachine.Tray != null)
            {
                activityMachine.AddActivity(new DynamicActivity(string.Format(CultureInfo.InvariantCulture, "Notify Tray ProcessError {0}", trayProcessor.Name), 
                    () => activityMachine.Tray.OnProcessingErrorOccurred(e)));
                activityMachine.AddActivity(new DynamicActivity(activityMachine.TrayHandler.Name + ".AbortFlowForTray", 
                    () => activityMachine.InstrumentManager.AbortFlowForTray(activityMachine.Tray)));
            }

            // Make the first activity to stop the station.
            activityMachine.SetActivityStop();

            //If the station is disabled because the stop process failed, then we want to quit.
            activityMachine.AddQuitOrContinueCondition(new DynamicConstraint<ITrayProcessor>("StationState == StationState.Disabled", 
                trayProcessor, s => s.State == StationState.Disabled), new DynamicConstraint<ITrayProcessor>("StationState == StationState.Stopped", 
                    trayProcessor, s => s.State == StationState.Stopped));

            // Disable the station when it is finished processing and do NOT permit it to re-enable.
            activityMachine.AddActivity(new DynamicActivity(trayProcessor.Name + ".Disable", () => ((ISupportsDisabling)trayProcessor).Disable(AllowStationEnableAfterError)));

            activityMachine.AddActivity(new DynamicActivity(string.Format(CultureInfo.InvariantCulture, "Release Lock on {0}", trayProcessor.Name), 
                () => trayProcessor.ReleaseLock(activityMachine)));

        }

        public void AssembleMachineForFatalError(ErrorHandlingActivityMachine activityMachine, IInstrument instrument, AtlasException e)
        {
            // No resource locking required for fatal

            //stop commands running on stations and shut down instrument.
            SetStopInstrumentActivity(activityMachine, e);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        protected override void SetSeverityActivities(DynamicActivityMachine machine, AtlasException e)
        {
            switch (e.Severity)
            {
                case ExceptionSeverityCategory.Fatal:
                    AssembleMachineForFatalError(machine as ErrorHandlingActivityMachine, machine.Configuration.Data.Instrument as IInstrument, e);
                    break;
                case ExceptionSeverityCategory.Critical:
                    AssembleMachineForCriticalError(machine as ErrorHandlingActivityMachine, machine.Configuration.Data.Instrument as IInstrument, e);
                    break;
                case ExceptionSeverityCategory.Severe:
                    AssembleMachineForSevereError(machine as ErrorHandlingActivityMachine, machine.Configuration.Data.Instrument as IInstrument, e);
                    break;
                case ExceptionSeverityCategory.Standard:
                    AssembleMachineForStandardError(machine as ErrorHandlingActivityMachine, machine.Configuration.Data.Instrument as IInstrument, e);
                    break;
            }
        }

        private void AssembleMachineForQuitOnErrorEscalation(DynamicActivityMachine machine, AtlasException e)
        {
            // This will trigger if either Error or Severity property notifies of a change.
            machine.UseRuntimeTrigger(new PropertyChangedTrigger("Station Error Severity Changed", e.Station,
                e.Station.PropertyToString(() => e.Station.Error) + "." + e.Station.PropertyToString(() => e.Station.Error.Severity)));
            // Quit at any time if the station's error escalates in severity.
            machine.UseAdditionalQuitCondition(new DynamicConstraint<IStation>("Station Error Severity Increased?", e.Station,
                (s) => s.Error != null && s.Error.Severity.IsGreaterThan(e.Severity)));
        }
    }
}
