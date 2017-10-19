using SimpleMvvmToolkit;
using System.Globalization;
using Ventana.Core.Activities.Machines;
using Ventana.Core.Activities.Parts;
using Ventana.Core.Activities.SpecializedTriggers;
using Ventana.Core.Base;
using Ventana.Core.Base.BusinessObjects;
using Ventana.Core.Base.Communications;
using Ventana.Core.Base.ExceptionHandling;
using Ventana.Core.Base.Processors;
using Ventana.Core.ExceptionHandling;
using Ventana.Core.ExceptionHandling.RecoveryOperations;
using Ventana.Core.Logging;
using Ventana.Core.PubSub;
using Ventana.Core.PubSub.MessageTypes;

namespace Ventana.Core.Activities.Builders
{
    /// <summary>
    /// builder base for all error handling activity machine builders 
    /// so we can support multiple handlers.
    /// </summary>
    public abstract class ErrorHandlingBuilderBase : ActivityMachineBuilder
    {
        private IPubSub _pubSub;
        private IAtlasLogger _atlasLogger;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        public void AssembleMachineForTrayProcessor(AtlasException atlasException, ErrorHandlingActivityMachine errorMachine)
        {
            if (atlasException.ExceptionOperationsGroup.Recovery is Reinitialize)
            {
                if (atlasException.Station is IReinitializable)
                {
                    var reinitializableStation = atlasException.Station as IReinitializable;
                    if (reinitializableStation.AllowsReinitialization)
                    {
                        LogService.Log(LogType.System, LogMessageType.Debug, GetType().Name,
                                        string.Format(CultureInfo.InvariantCulture, "Reinitialization on {0}. AllowReinitialization: {1}",
                                        atlasException.Station.Type, reinitializableStation.AllowsReinitialization));
                        //if this succeeds it will quit the machine
                        AssembleMachineForReinitRecovery(errorMachine, errorMachine.Configuration.Data.Instrument as IInstrument, atlasException);
                    }
                    else
                    {
                        LogService.Log(LogType.System, LogMessageType.Error, GetType().Name, 
                            string.Format(CultureInfo.InvariantCulture, "Reinitialization not possible on {0}. AllowReinitialization: {1}",
                            atlasException.Station.Type, reinitializableStation.AllowsReinitialization), atlasException);
                    }
                }
                else
                {
                    LogService.Log(LogType.System, LogMessageType.Error, GetType().Name, "Station is not IReinitializable. Reinitialization not possible on " + atlasException.Station.Type, atlasException);
                }
            }

            // the activities added by this will be executed only if reinitialize fails or is not performed.
            SetSeverityActivities(errorMachine, atlasException);
        }

        /// <summary>
        /// wires up the severity based builder definitions to the exception severity
        /// </summary>
        /// <param name="machine"></param>
        /// <param name="e"></param>
        protected abstract void SetSeverityActivities(DynamicActivityMachine machine, AtlasException e);

        /// <summary>
        /// Publishes an error notification.
        /// </summary>
        /// <param name="machine"></param>
        /// <param name="e"></param>
        protected void SetNotificationActivities(DynamicActivityMachine machine, AtlasException e)
        {
            if (e.Severity != ExceptionSeverityCategory.Warning &&
                e.Severity != ExceptionSeverityCategory.Information)
            {
                if (e.Severity != ExceptionSeverityCategory.Unknown)
                {
                var instrumentManager = machine.Configuration.Data.InstrumentManager as IInstrumentManager;
                if (instrumentManager != null)
                {
                    machine.AddActivity(new DynamicActivity("InstrumentManager.AddToErrorList",
                        () => instrumentManager.SystemExceptions.Add(e)));
                }

                if (e.Severity == ExceptionSeverityCategory.Fatal)
                {
                    machine.AddActivity(new DynamicActivity(MessageTokens.Instance[Request.CloseAllDialogs],
                        () => MessageBus.Default.Notify("DialogEvent.RequestCloseAll", this,
                            new NotificationEventArgs())));
                }
                //TODO: this should use ShowErrorDialog once dialogs are refactored.  Alerts dialog does not suppress others.
                machine.AddActivity(new DynamicActivity("MessageBus.ShowAlertDialog",
                    () => MessageBus.Default.Notify(MessageTokens.Instance[Request.ShowAlertsDialog], e,
                        new NotificationEventArgs<AtlasException>(e.FullDisplayLocalized, e))));
            }

                _atlasLogger = new AtlasLogger();
                _pubSub = PubSub.PubSub.GetInstance(_atlasLogger);

                var ventanaConnectMessage = new StatusUpdated
                {
                    ErrorCode = e.Code,
                    ErrorMessage = e.Description,
                };

                machine.AddActivity(new DynamicActivity("PubSub.PublishStatus", () => _pubSub.Publish<IStatusUpdated>(ventanaConnectMessage)));
            }

        }

        /// <summary>
        /// If station has a tray then the currently running job for the tray will be aborted.
        /// This adds a trigger to the machine for the IsTrayDetected property and for the tray state.
        /// </summary>
        /// <param name="activityMachine"></param>
        /// <param name="instrument"></param>
        /// <param name="e"></param>
        /// <remarks>Requires externally established trigger for State on the station</remarks>
        protected void AssembleMachineForAbortTray(ErrorHandlingActivityMachine activityMachine, IInstrument instrument, AtlasException e)
        {
            var trayProcessor = e.Station as ITrayProcessor;
            var trayHandler = e.Station as ITrayHandler;
            var trayDetector = e.Station as ITrayDetector;
            var tray = activityMachine.Tray;

            if (tray == null)
            {
                
            }

            if (trayHandler != null && trayDetector != null && tray != null)
            {
                activityMachine.UseRuntimeTrigger(new PropertyChangedTrigger("IsTrayDetected Changed", trayDetector,
                    trayDetector.PropertyToString(() => trayDetector.IsTrayDetected)));

                activityMachine.UseRuntimeTrigger(new PropertyChangedTrigger("TrayStateChanged", tray, tray.PropertyToString(()=> tray.State)));
                // Tell the machine to quit acting if the tray is lost.
                activityMachine.SetQuitOnTrayState(TrayState.Lost);
                activityMachine.AddActivity(
                    new DynamicActivity(string.Format(CultureInfo.InvariantCulture, "Notify Tray Aborted {0}", activityMachine.TrayHandler.Name),
                                        () => activityMachine.Tray.OnAborted(TrayAbortType.Immediate)));
                activityMachine.AddActivity(
                    new DynamicActivity(activityMachine.TrayHandler.Name + ".AbortFlowForTray",
                                        () => activityMachine.InstrumentManager.AbortFlowForTray(tray)));

                // Pause here until the station is done with the abort process and the tray has been retrieved.
                activityMachine.AddContinueCondition(new DynamicConstraint<IStation>(trayProcessor.Name, trayProcessor as IStation,
                    s => s.State == StationState.Idle && !(s as ITrayDetector).IsTrayDetected));
            }
        }

        /// <summary>
        /// Assembles the machine for the Reinitialization operation as part of a Recovery when an exception is raised.
        /// </summary>
        /// <param name="activityMachine"></param>
        /// <param name="instrument"></param>
        /// <param name="e"></param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(System.String,System.String)")]
        public void AssembleMachineForReinitRecovery(ErrorHandlingActivityMachine activityMachine, IInstrument instrument, AtlasException e)
        {
            var station = e.Station;
            var trayDetector = station as ITrayDetector;
            if (trayDetector != null)
            {
                activityMachine.UseRuntimeTrigger(new PropertyChangedTrigger("IsTrayDetected Changed", trayDetector,
                    trayDetector.PropertyToString(() => trayDetector.IsTrayDetected)));
            }

            activityMachine.AddActivity(new DynamicActivity("Log Reinitialize", () => LogService.Log(GetType().Name, "Begin reinitialize activity.")));

            if (station is IReinitializable)
            {
                var reinitializableStation = station as IReinitializable;
                if (reinitializableStation.ReinitAttempted)
                {
                    activityMachine.AddActivity(new DynamicActivity("Log Reinitialize", () => LogService.Log(GetType().Name, "Station already attempted Reinit")));
                    return;
                }                
                activityMachine.AddActivity(new DynamicActivity("Reinit set initial states", () => (e.Station as IReinitializable).Reinit(e)));                
            }
            else
            {
                activityMachine.AddActivity(new DynamicActivity("Log Reinitialize", () => LogService.Log(GetType().Name, "Station is not IReinitializable")));
                return;
            }

            AssembleMachineForAbortTray(activityMachine, instrument, e);

            // Now get a resource lock for the remaining reinit operations.
            activityMachine.AddActivity(new DynamicActivity(string.Format(CultureInfo.InvariantCulture, "Wait For Lock on {0}", trayDetector.Name), () => trayDetector.WaitForLock(activityMachine)));
            
            activityMachine.AddConditionalActivity(new DynamicConstraint<IStation>("Station has no tray and is idle.", e.Station, (s =>
                        (s is IReinitializable && (s as IReinitializable).AllowsReinitialization) &&                                                      //1. Module is IReinitializable
                        ((s is ITrayHandler && !(s as ITrayHandler).IsTrayDetected) || (s is ITrayProcessor && !(s as ITrayProcessor).IsTrayDetected)) && //2. Tray is not present
                        s.State == StationState.Idle))                                                                                                    //3. Module is idle
                , new DynamicActivity("Station.Stop", () => e.Station.Stop()));

            //this delays the start until the station state is stopped
            activityMachine.AddConditionalActivity(new DynamicConstraint<IStation>("Station is stopped.", e.Station,
                (s => s.State == StationState.Stopped))
                , new DynamicActivity("Station.Reinitialize", () => e.Station.Start()));

            activityMachine.AddContinueCondition(new DynamicConstraint<IStation>("Is Processing? (after Start)", e.Station,
               (s => (!(e.Station is ITrayProcessor) || !(e.Station as ITrayProcessor).IsProcessing))));

            activityMachine.AddActivity(new DynamicActivity("Station update Reinit State", () =>
            {
                var reinitializableStation = e.Station as IReinitializable;
                if (reinitializableStation != null)
                {
                    reinitializableStation.ReinitState = e.Station.State == StationState.Idle ? ReinitState.Success : ReinitState.Fail;                    
                }
            }));

            activityMachine.AddFinishOrContinueCondition(
                new DynamicConstraint<IStation>("Station reinitialize succeeded.", e.Station,
                    (s => s.State == StationState.Idle))
                ,
                new DynamicConstraint<IStation>("Station reinitialize failed.", e.Station,
                    (s => s.State != StationState.Idle))
                  );

            activityMachine.AddActivity(new DynamicActivity(string.Format(CultureInfo.InvariantCulture, "Release Lock on {0}", trayDetector.Name), () => trayDetector.ReleaseLock(activityMachine)));
        }

        public void SetDegradedModeActivity(DynamicActivityMachine machine, AtlasException e)
        {
            if (e.ExceptionOperationsGroup.DegradedOperation == null) return;
            
            IInstrument instrument = machine.Configuration.Data.Instrument;
            machine.AddActivity(new DynamicActivity("Instrument.Degrade", () => instrument.Degrade(e.ExceptionOperationsGroup.DegradedOperation)));
        }

        public void SetStopInstrumentActivity(DynamicActivityMachine machine, AtlasException e)
        {
            IInstrumentManager instrumentManager = machine.Configuration.Data.InstrumentManager;
            machine.AddActivity(new DynamicActivity("InstrumentManager.StopSystem", () => instrumentManager.StopSystem()));
        }

        public void SetEmergencyStopInstrumentActivity(DynamicActivityMachine machine, AtlasException e)
        {
            IInstrumentManager instrumentManager = machine.Configuration.Data.InstrumentManager;
            machine.AddActivity(new DynamicActivity("InstrumentManager.StopSystem", () => instrumentManager.StopSystem(true)));
        }
    }
}
