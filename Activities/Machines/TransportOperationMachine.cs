using System;
using System.Globalization;
using Ventana.Core.Activities.Executables;
using Ventana.Core.Activities.Parts;
using Ventana.Core.Base;
using Ventana.Core.Base.BusinessObjects;
using Ventana.Core.Base.BusinessObjects.Generic;
using Ventana.Core.Base.ExceptionHandling;
using Ventana.Core.Base.Transportation;
using Ventana.Core.Configuration;
using Ventana.Core.ExceptionHandling;
using Ventana.Core.Logging;
using Ventana.Core.Utilities;

namespace Ventana.Core.Activities.Machines
{
    public class TransportOperationMachine : DynamicActivityMachine
    {
        private static readonly int DefaultNumRetries;
        private readonly object _stopLock = new object();
        private bool _canStop;

        static TransportOperationMachine()
        {
            // If config entry is missing, DefaultNumRetries will be zero (0). The move will be attempted, but not retried.
            DefaultNumRetries = StationDefinition.GetConfigSection().Transportation.GetIntegerSetting("FailedHandoffRetryAttempts");
        }

        internal TransportOperationMachine(string name, SimpleDispatcher dispatcher) : base(name, dispatcher)
        {
            RequiresResourceLocking = true;
            IsSynchronous = false;    
        }

        public TransportOperation Operation { get; internal set; }

        public ITransport Transport { get; internal set; }

        public IInstrument Instrument { get; internal set; }

        public ITrayDetector Station { get; internal set; }

        public ITray Tray { get; internal set; }

        public bool IsStopped { get; private set; }

        public bool HaltOnError { get; set; }

        public bool CanStop
        {
            get { return _canStop; }
            set
            {
                lock (_stopLock)
                {
                    _canStop = value;
                }
            }
        }

        public void Stop()
        {
            lock (_stopLock)
            {
                if (CanStop)
                {
                    IsStopped = true;
                    // This will only cancel future Transport move steps, not a move in progress.  MCode does
                    // not cleanly handle stopping a move in progress.
                    (Configuration.Data.MoveCancellationToken as MoveCancellationToken).Cancel();
                }
            }
        }

        #region Activities with Typed Conditions for convenience
        public void SetFinishOnStationState(StationState state)
        {
            if (Station is IStatefulModel<StationState>)
            {
                UseAdditionalFinishCondition(new DynamicConstraint<IStatefulModel<StationState>>("StationState==" + state, Station as IStatefulModel<StationState>, s => s.State == state));
            }
        }
        public void SetQuitOnStationState(StationState state)
        {
            if (Station is IStatefulModel<StationState>)
            {
                UseAdditionalQuitCondition(new DynamicConstraint<IStatefulModel<StationState>>("StationState==" + state, Station as IStatefulModel<StationState>, s => s.State == state));
            }
        }

        public void SetQuitOnCriticalStationError()
        {
            if (Station is IStation)
            {
                UseAdditionalQuitCondition(new DynamicConstraint<IStation>("Station Has Critical Error?", Station as IStation, s => s.Error != null && s.Error.Severity.IsGreaterThan(ExceptionSeverityCategory.Severe)));
            }
        }

        public void SetQuitOnTransportHasErred()
        {
            if (Transport is IStation)
            {
                UseAdditionalQuitCondition(new DynamicConstraint<IStation>("Transport HasErred", Transport as IStation, t => t.HasErred));
            }
        }

        public void SetFinishOnTrayState(TrayState state)
        {
            if (Tray != null)
            {
                UseAdditionalFinishCondition(new DynamicConstraint<ITray>("TrayState==" + state, Tray, t => t.State == state));
            }
        }

        public void SetQuitOnTrayState(TrayState state)
        {
            if (Tray != null)
            {
                UseAdditionalQuitCondition(new DynamicConstraint<ITray>("TrayState==" + state, Tray, t => t.State == state));
            }
        }

        public void SetFinishOnTransportState(StationState state)
        {
            UseAdditionalFinishCondition(new DynamicConstraint<ITransport>("TransportState==" + state, Transport, s => s.State == state));
        }

        /// <summary>
        /// Set an ActOnResultActivity that will perform a tray pickup from the station.
        /// This does not allow cancellation of the move operation.
        /// Failure in the activity will cancel the machine.
        /// </summary>
        public void SetActivityTransportPerformPickup()
        {
            AddActivity(new ActOnResultActivity<bool>("ITransport.PickupTray", 
                () => Transport.PickUpTrayFromDetector(Station, DefaultNumRetries, null, true), false,
                () => Cancel("PickupTray", true)));
        }

        /// <summary>
        /// Set an ActOnResultActivity that will perform a tray dropoff to the station.
        /// This does not allow cancellation of the move operation.
        /// Failure in the activity will cancel the machine.
        /// </summary>
        public void SetActivityTransportPerformDropoff()
        {
            AddActivity(new ActOnResultActivity<bool>("ITransport.DropoffTray", 
                () => Transport.DropOffTrayToDetector(Station, DefaultNumRetries, null), false, 
                () => Cancel("DropoffTray", true)));
        }

        /// <summary>
        /// Set a conditional concurrent ActOnResult Activity to move the transport to the station for pick-up, 
        /// or cancel the machine upon failure.
        /// </summary>
        public void SetConditionalActivityTransportMoveToPickupHeight(Func<ITransport, bool> transportCondition, MoveCancellationToken cancelMove)
        {
            var moveActivity = new ActOnResultActivity<bool>("ITransport.PrepareForPickUpFrom:" + Station.Name,
                                    () => Transport.PrepareForPickUpFrom(Station as ITransportDock, cancelMove, true), false,
                                    () => Cancel("MoveToPickUpLocation", !cancelMove.IsCancelled));
            AddConditionalActivity(
                new DynamicConstraint<ITransport>("TransportAtPickupHeightCondition", Transport, transportCondition),
                moveActivity);
        }

        /// <summary>
        /// Set a conditional concurrent ActOnResult Activity to move the transport to the station for drop-off, 
        /// or cancel the machine upon failure.
        /// </summary>
        public void SetConditionalActivityTransportMoveToDropoffHeight(Func<ITransport, bool> transportCondition, MoveCancellationToken cancelMove)
        {
            var moveActivity = new ActOnResultActivity<bool>("ITransport.PrepareForDropOffTo:" + Station.Name,
                                    () => Transport.PrepareForDropOffTo(Station as ITransportDock, cancelMove, true), false,
                                    () => Cancel("MoveToDropOffLocation", !cancelMove.IsCancelled));
            AddConditionalActivity(new DynamicConstraint<ITransport>("TransportAtDropoffHeightCondition", Transport, transportCondition),
                                   moveActivity);
        }

        /// <summary>
        /// Set a QuitActivity that will move the transport to a given z-position.
        /// </summary>
        /// <param name="zPosition"></param>
        public void SetActivityTransportMoveToZposition(double zPosition)
        {
            //TODO: GIVE CANCELLATION TOKEN TO TRANSPORT?
            AddActivity(new ActOnResultActivity<bool>("ITransport.MoveToZPosition:" + zPosition, 
                () => Transport.MoveToElevatorLocation(zPosition, null), false, () => Cancel("MoveToElevator", true)));
        }

        public void SetActivityTrayHandlerPrepareForHandoff()
        {
            if (Station is ITrayHandler)
            {
                AddActivity(new ActOnResultActivity<bool>(Station.Name + ".PrepareForHandoff", 
                    () => (Station as ITrayHandler).PrepareForHandoff(), false, () => Cancel("PrepareForHandoff", true)));
            }
        }

        public void SetActivityTrayHandlerCompleteHandoff()
        {
            if (Station is ITrayHandler)
            {
                AddActivity(new ActOnResultActivity<bool>(Station.Name + ".CompleteHandoff",
                    () => (Station as ITrayHandler).CompleteHandoff(), false, () => Cancel("CompleteHandoff", true)));
            }
        }

        public void SetActivityTrayHandlerSafeToEnter()
        {
            if (Station is ITrayHandler)
            {
                AddActivity(new ActOnResultActivity<bool>(Station.Name+".SafeToEnter", 
                    () => (Station as ITrayHandler).SafeToEnter, false, () => Cancel("SafeToEnter", true)));
            }
        }

        public void SetActivityReleaseDockLock(object lockOwner)
        {
            AddActivity(new DynamicActivity(Station.Name + ".ReleaseLock", 
                () => Station.ReleaseLock(lockOwner)));
        }

        //TODO: move this somewhere better?
        public void SetActivityReassignTray(ITrayDetector src, ITrayDetector dst)
        {
            AddActivity(new DynamicActivity(Name + ".ReassignTray(" + src.Name + "->" + dst.Name + ")",
                () => ReassignTray(src, dst)));
        }

        //TODO: move this somewhere better?
        public void ReassignTray(ITrayDetector src, ITrayDetector dst)
        {
            // Now this station is trayless and toStation owns the tray.
            var tray = src.Tray;
            // set null first since this logs the 'moved from' event.
            src.Tray = null;
            // Now ok to assign tray to destination, which logs the 'moved to' event.
            dst.Tray = tray;
            if (tray != null)
            {
                tray.CurrentStation = dst as IStation;
            }
        }
        #endregion

        public void SetPauseUntilTransportState(StationState state)
        {
            SetPauseUntilConditionOfTransport(string.Format(CultureInfo.InvariantCulture, "Wait for {0} state", state), t => t.State == state);
        }

        public void SetPauseUntilTrayHandlerState(StationState state)
        {
            SetPauseUntilConditionOfStation(string.Format(CultureInfo.InvariantCulture, "Wait for {0} state", state), d => d.State == state);
        }

        public void SetPauseUntilConditionOfTransport(string name, Func<ITransport, bool> condition)
        {
            AddContinueCondition(new DynamicConstraint<ITransport>(name, Transport, condition));
        }

        public void SetPauseUntilConditionOfStation(string name, Func<IStation, bool> condition)
        {
            AddContinueCondition(new DynamicConstraint<IStation>(name, Station as IStation, condition));
        }

        public void SetFinishOrContinueForMoveStopped()
        {
            AddFinishOrContinueCondition(
                new DynamicConstraint<TransportOperationMachine>("IsMachineStopped?", this, (m) => m.IsStopped),
                new DynamicConstraint<TransportOperationMachine>("IsMachineNotStopped?", this, (m) => !m.IsStopped));
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        private void Cancel(string description, bool withFault)
        {
            var cause = withFault ? CompletionCause.Faulted : CompletionCause.Interrupted;
            LogService.Log(LogType.System, LogMessageType.Debug, GetType().Name,
                string.Format(CultureInfo.InvariantCulture, "{0} setting CompletionCause to {1} inside Cancel() during {2}", Name, cause, description));

            // This must set the completion cause rather than trying to throw an exception.  If the caller of Cancel was a
            // different thread, then throwing an exception here would not propagate to the machine.
            CompletionCause = cause;
        }
    }
}
