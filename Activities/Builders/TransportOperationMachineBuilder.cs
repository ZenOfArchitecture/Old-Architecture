using System;
using Ventana.Core.Activities.Executables;
using Ventana.Core.Activities.Machines;
using Ventana.Core.Activities.Parts;
using Ventana.Core.Activities.SpecializedTriggers;
using Ventana.Core.Base;
using Ventana.Core.Base.Activities;
using Ventana.Core.Base.BusinessObjects;
using Ventana.Core.Base.BusinessObjects.Generic;
using Ventana.Core.Base.Common;
using Ventana.Core.Base.Transportation;
using Ventana.Core.Logging;
using Ventana.Core.Utilities.ExtensionMethods;

namespace Ventana.Core.Activities.Builders
{
    public class TransportOperationMachineBuilder : ActivityMachineBuilder
    {
        /// <summary>
        /// Check an IStation to see if it is prepared to begin the tray pick-up operation.
        /// Stopped is included to account for a tray recovery hand-off.
        /// Idle is included to account for a tray processor that never started processing.
        /// </summary>
        private static readonly Func<IStation, bool> IsStationPickupReady = s =>
            (s.State.IsOneOf(StationState.Done, StationState.Idle, StationState.Stopped));

        /// <summary>
        /// Check an IStation to see if it is prepared to begin the tray drop-off operation.
        /// Stopped is included to account for a tray recovery hand-off.
        /// </summary>
        private static readonly Func<IStation, bool> IsStationDropoffReady = s =>
            (s.State.IsOneOf(StationState.Idle, StationState.Stopped));

        /// <summary>
        /// Check an IStation to see if it is prepared for hand-off.
        /// </summary>
        private static readonly Func<IStation, bool> IsStationHandoffPrepared = s =>
            (!(s is ITrayHandler) && s.State == StationState.Done) || (s is INeedRefactoring)
            //TODO: change this to use HandoffState of ITrayHandler once station states are rewritten.
            || (s is ITrayHandler && s.State == StationState.HandoffPrepared);

        /// <summary>
        /// Check an IStation to see if it is any active (not stopping or disabling) state other than HandoffPrepared.
        /// Stopped is not included because the Station may go Stopped->HandoffPrepared->Stopped for tray recovery.
        /// Movement should be okay once the IStation is Stopped anyway, but not while Stopping.
        /// </summary>
        private static readonly Func<IStation, bool> IsStationNotHandoffPrepared = s => 
            s.State.IsNoneOf(StationState.HandoffPrepared, StationState.Stopping, StationState.Disabling);

        /// <summary>
        /// Check an ITransport to see if it is currently below the pick-up height of an ITrayHandler.
        /// </summary>
        private static readonly Func<ITransportDock, Func<ITransport, bool>> IsTransportBelowPickupHeight = dock => 
            ((ITransport t) => t.IsInElevator && (t.ZPosition < dock.PickupZPosition || t.IsAtLocationWithinTolerance(Axis.Z, dock.PickupZPosition)));

        /// <summary>
        /// Check an ITransport to see if it is currently above the drop-off height of an ITrayHandler.
        /// </summary>
        private static readonly Func<ITransportDock, Func<ITransport, bool>> IsTransportAboveDropoffHeight = dock => 
            ((ITransport t) => t.IsInElevator && (t.ZPosition > dock.DropoffZPosition || t.IsAtLocationWithinTolerance(Axis.Z, dock.DropoffZPosition)));

        /// <summary>
        /// Check an ITransport to see if it is parked at the drop-off height of an ITrayHandler.
        /// </summary>
        private static readonly Func<ITransportDock, Func<ITransport, bool>> IsTransportParkedAtDropoffHeight = dock =>
            ((ITransport t) => t.IsInElevator && !t.IsMoving && t.IsAtLocationWithinTolerance(Axis.Z, dock.DropoffZPosition));

        /// <summary>
        /// Check an ITransport to see if it is parked at the pick-up height of an ITrayHandler.
        /// </summary>
        private static readonly Func<ITransportDock, Func<ITransport, bool>> IsTransportParkedAtPickupHeight = dock =>
            ((ITransport t) => t.IsInElevator && !t.IsMoving && t.IsAtLocationWithinTolerance(Axis.Z, dock.PickupZPosition));

        /// <summary>
        /// Check an ITransport to see if it is parked in the elevator.
        /// </summary>
        private static readonly Func<ITransport, bool> IsTransportParkedInElevator =
            transport => transport.IsInElevator && !transport.IsMoving;

        private static readonly Func<ITransport, ITrayDetector, bool> IsTrayLost = (transport, detector) =>
            (!transport.IsTrayDetected && !detector.IsTrayDetected);

        private static readonly Func<ITransport, bool> IsTransportParkedWithTray = (transport) =>
            (transport.IsTrayDetected && transport.State == StationState.Idle);

        private static readonly Func<ITransport, bool> IsTransportParkedWithoutTray = (transport) =>
            (!transport.IsTrayDetected && transport.State == StationState.Idle);

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        public override void Build(IActivityMachine machine)
        {
            var transportMachine = machine as TransportOperationMachine;

            if (transportMachine != null)
            {
                transportMachine.Instrument = machine.Configuration.Data.Instrument as IInstrument;
                transportMachine.Transport = machine.Configuration.Data.Transport as ITransport;
                transportMachine.Operation = machine.Configuration.Data.TransportOperation;
                transportMachine.Station = machine.Configuration.Data.Station as ITrayDetector;
                transportMachine.ExpirationTimespan = machine.Configuration.Data.ExpirationTimespan;
                // Assigning the tray at build time ensures that the stations are locked while also
                // letting the tray be used for runtime triggers.
                transportMachine.Tray = transportMachine.Station.Tray;

                var statefulSource = transportMachine.Station as IStatefulModel<StationState>;

                // Hold a reference to the transport's tray, if the station didn't have a tray.
                if (transportMachine.Tray == null)
                {
                    transportMachine.Tray = transportMachine.Transport.Tray;
                }

                // Setup the triggers that each machine will use.
                if (transportMachine.Tray != null)
                {
                    transportMachine.UseRuntimeTrigger(new PropertyChangedTrigger("Tray State Changed", transportMachine.Tray, transportMachine.Tray.PropertyToString(() => transportMachine.Tray.State)));
                }
                if (statefulSource != null)
                {
                    transportMachine.UseRuntimeTrigger(new PropertyChangedTrigger("Station State Changed", statefulSource, statefulSource.PropertyToString(() => statefulSource.State)));
                }
                var stationTrayDetectTrigger = new PropertyChangedTrigger("Station IsTrayDetected Changed", statefulSource, 
                    transportMachine.Station.PropertyToString(() => transportMachine.Station.IsTrayDetected));
                transportMachine.UseRuntimeTrigger(stationTrayDetectTrigger);

                var station = transportMachine.Station as IStation;
                if (station != null)
                {
                    // This will trigger if either Error or Severity property notifies of a change.
                    transportMachine.UseRuntimeTrigger(new PropertyChangedTrigger("Station Error Severity Changed", station,
                        station.PropertyToString(() => station.Error) + "." + station.PropertyToString(() => station.Error.Severity)));
                }

                var transportTrayDetectTrigger = new PropertyChangedTrigger("Transportation IsTrayDetected Changed", transportMachine.Transport,
                    transportMachine.Transport.PropertyToString(() => transportMachine.Transport.IsTrayDetected));
                transportMachine.UseRuntimeTrigger(new PropertyChangedTrigger("Instrument State Changed", transportMachine.Instrument,
                    transportMachine.Instrument.PropertyToString(() => transportMachine.Instrument.State)));
                transportMachine.UseRuntimeTrigger(new PropertyChangedTrigger("Transportation State Changed", transportMachine.Transport,
                    transportMachine.Transport.PropertyToString(() => transportMachine.Transport.State)));
                transportMachine.UseRuntimeTrigger(transportTrayDetectTrigger);
                var transportStation = transportMachine.Transport as IStation;
                transportMachine.UseRuntimeTrigger(new PropertyChangedTrigger("Transportation HasErred Changed", transportStation,
                    transportStation.PropertyToString(() => transportStation.HasErred)));
                transportMachine.UseRuntimeTrigger(new PropertyChangedTrigger("Transportation XPosition Changed", transportMachine.Transport,
                    transportMachine.Transport.PropertyToString(() => transportMachine.Transport.XPosition)));
                transportMachine.UseRuntimeTrigger(new PropertyChangedTrigger("Transportation ZPosition Changed", transportMachine.Transport,
                    transportMachine.Transport.PropertyToString(() => transportMachine.Transport.ZPosition)));

                // Tell the machine to quit acting if the tray is lost.
                transportMachine.SetQuitOnTrayState(TrayState.Lost);
                // Tell the machine to quit if Transport station has erred.
                transportMachine.SetQuitOnTransportHasErred();
                // Tell the machine to quit if the source/dest (depending on move type) acquires a critical error.
                transportMachine.SetQuitOnCriticalStationError();
                // Tell the machine to quit if the source/dest (depending on move type) goes to disabling or disabled state.
                transportMachine.SetQuitOnStationState(StationState.Disabling);
                transportMachine.SetQuitOnStationState(StationState.Disabled);
                // Tell machine to finish if transport is stopped.
                transportMachine.SetFinishOnTransportState(StationState.Stopped);

                switch (transportMachine.Operation)
                {
                    case TransportOperation.BeginPickup:
                        AssembleMachineForBeginTrayPickup(transportMachine);
                        break;

                    case TransportOperation.CompletePickup:
                        AssembleMachineForCompleteTrayPickup(transportMachine, machine.Configuration.Data.SourceLockOwner);
                        break;

                    case TransportOperation.BeginDropoff:
                        AssembleMachineForBeginTrayDropoff(transportMachine);
                        break;

                    case TransportOperation.CompleteDropoff:
                        AssembleMachineForCompleteTrayDropoff(transportMachine);
                        break;

                    default:
                        LogService.Log(LogType.System, LogMessageType.Warning, GetType().Name, "Attempted to build a TransportActivityMachine for an unknown purpose.");
                        break;
                }
            }
            else
            {
                LogService.Log(LogType.System, LogMessageType.Error, GetType().Name, "Attempted to build a TransportActivityMachine using a null reference.");
            }
        }

        private void AssembleMachineForBeginTrayPickup(TransportOperationMachine machine)
        {
            // In this case, Station is the source of the tray.
            var station = machine.Station as IStation;
            var dockStation = machine.Station as ITransportDock;
            if (station != null && dockStation != null)
            {
                // Wait for the source station to detect a tray, responding only to the IsTrayDetected trigger.
                // The source station should have the tray.  Slide id does not have a tray present sensor, so it should fake it.
                machine.AddQuitOrContinueCondition(
                    new DynamicConstraint<ITrayDetector>("Tray Not Detected At Source?", machine.Station, (td) => !td.IsTrayDetected),
                    new DynamicConstraint<ITrayDetector>("Tray Detected At Source?", machine.Station, (td) => td.IsTrayDetected));

                // We can get an early start on handoff preparation if not picking up from a stainer,
                // or if the transport is already below the stainer.
                var prepareForHandoffEarly = station.Type != StationType.Stainer 
                                             || IsTransportBelowPickupHeight(dockStation)(machine.Transport);
                if (prepareForHandoffEarly)
                {
                    // Pause until the source station is ready for hand-off preparation.
                    machine.SetPauseUntilConditionOfStation("IsStationPickupReady?", IsStationPickupReady);

                    // Prepare the source station for handoff.  Quit this machine if the PrepareForHandoff fails.
                    machine.SetActivityTrayHandlerPrepareForHandoff();
                }

                // Move the transport to the tray source station for a tray pick-up. Quit if the move fails.
                machine.SetConditionalActivityTransportMoveToPickupHeight(IsTransportParkedInElevator,
                    machine.Configuration.Data.MoveCancellationToken);

                // Pause until the transport is below the source station's pick-up location.
                machine.SetPauseUntilConditionOfTransport("IsTransportBelowPickupHeight?", IsTransportBelowPickupHeight(dockStation));

                // If we didn't already prepare for handoff before the elevator move, do it now.
                if (!prepareForHandoffEarly)
                {
                    // Pause until the source station is ready.
                    machine.SetPauseUntilConditionOfStation("IsStationPickupReady?", IsStationPickupReady);

                    // Prepare the source station for handoff.  Quit this machine if the PrepareForHandoff fails.
                    machine.SetActivityTrayHandlerPrepareForHandoff();
                }

                // Pause until source station is handoff-prepared for pickup.
                machine.SetPauseUntilConditionOfStation("IsStationHandoffPrepared?", IsStationHandoffPrepared);
                // Wait for transport to be stopped at the pick-up height.
                machine.SetPauseUntilConditionOfTransport("IsTransportStoppedAtLocation?", IsTransportParkedAtPickupHeight(dockStation));

                // Ensure source station is safe to enter to pick-up tray
                machine.SetActivityTrayHandlerSafeToEnter();

                // From this point on, TrayMover should stop movements if this machine encounters an error.
                machine.AddActivity(new DynamicActivity("Set machine HaltOnError", () => machine.HaltOnError = true));

                // Tell transport to get the tray. Quit if the move fails.
                machine.SetActivityTransportPerformPickup();

                // If picking up from a stainer, make sure transport is clear of stainer mount before allowing complete handoff.
                if (station.Type == StationType.Stainer)
                {
                //TODO: ASK TEST/DEV TEAMS IF STAINER UPPER&LOWER LOADOFFSETS ARE EVER DIFFERENT THAN UPPER&LOWER OFFSETS.
                //TODO: IF NOT, CAN ELIMINATE THIS AND REMOVE THE LOADOFFSETS FROM THE CONFIGURATION.
                //TODO: IF STILL NEEDED, ELIMINATE SPECIAL CASE HERE BY MAKING STAINERS OVERRIDE UPPEROFFSET & LOWEROFFSET WHICH INCORPORATES ANY EXTRA OFFSET INTO THE ONE MOVE.
                    // Ensure the transport made it to the desired Z position.
                    machine.SetPauseUntilConditionOfTransport("IsTransportAboveDropoffHeight?", IsTransportAboveDropoffHeight(dockStation));
                }
            }
            else
            {
                throw new Exception("Begin tray pick-up failed to build: no source station.");
            }
        }

        private void AssembleMachineForCompleteTrayPickup(TransportOperationMachine machine, object sourceLockOwner)
        {
            // In this case, Station is the source of the tray.
            var station = machine.Station as IStation;
            var dockStation = machine.Station as ITransportDock;
            if (station != null && dockStation != null)
            {
                // This machine doesn't know if Transport is safe yet or not.
                machine.AddActivity(new DynamicActivity("Set machine HaltOnError", () => machine.HaltOnError = true));

                // Wait for transportation to stop moving.
                machine.AddContinueCondition(new DynamicConstraint<ITransport>("Transport Detects Tray?", machine.Transport,
                        (td) => IsTransportParkedWithTray(machine.Transport)));

                if (station.Type != StationType.Stainer)
                {
                    // This machine knows it is above the station and in elevator now, so no need for transport error.
                    machine.AddActivity(new DynamicActivity("Set machine HaltOnError", () => machine.HaltOnError = false));
                }

                // Wait for the source station IsTrayDetected trigger.
                // Slide id does not have a tray present sensor, so it should always be faking it.
                machine.AddContinueCondition(
                    new DynamicConstraint<ITrayDetector>("Tray Not Detected At Source?", machine.Station, (td) => !td.IsTrayDetected));

                // Complete the handoff.  Quit this machine if the CompleteHandoff fails.
                machine.SetActivityTrayHandlerCompleteHandoff();

                // If we just picked up from a stainer and the destination is below it, wait for CompleteHandoff to finish.
                if (station.Type == StationType.Stainer)
                {
                    var destinationZ = machine.Configuration.Data.DestinationZLocation;
                    // If the destination is below the source stainer,
                    // wait until the stainer's handoff is completed.  Otherwise, transport can move on safely.
                    if (dockStation.DockZ > destinationZ)
                    {
                        // Pause until source station has completed its handoff.
                        machine.SetPauseUntilConditionOfStation("IsStationNotHandoffPrepared", IsStationNotHandoffPrepared);
                    }
                }

                // Assuming the station is no longer HandoffPrepared, TrayMover does not need to halt movements anymore.
                machine.AddActivity(new DynamicActivity("Set machine HaltOnError", () => machine.HaltOnError = true));

                // Reassign the tray from source to transport.
                machine.SetActivityReassignTray(machine.Station, machine.Transport);

                // Special activity needed only for this machine to release the lock on the source station early.
                machine.SetActivityReleaseDockLock(sourceLockOwner);
            }
            else
            {
                throw new Exception("Complete tray pick-up failed to build: no source station.");
            }
        }

        public void AssembleMachineForBeginTrayDropoff(TransportOperationMachine machine)
        {
            var station = machine.Station as IStation;
            var dockStation = machine.Station as ITransportDock;
            if (station != null && dockStation != null && IsStationDropoffReady(station))
            {
                // We can get an early start on handoff preparation if not dropping off to a stainer,
                // or if the transport is already above the stainer.
                var prepareForHandoffEarly = station.Type != StationType.Stainer
                                             || IsTransportAboveDropoffHeight(dockStation)(machine.Transport);
                if (prepareForHandoffEarly)
                {
                    // Pause until the source station is ready to start hand-off prep.
                    machine.SetPauseUntilConditionOfStation("IsStationDropoffReady", IsStationDropoffReady);

                    // Prepare the source station for handoff.  Quit this machine if the PrepareForHandoff fails.
                    machine.SetActivityTrayHandlerPrepareForHandoff();
                }

                // Move the transport to the tray destination station for a tray drop-off. Quit if the move fails.
                machine.SetConditionalActivityTransportMoveToDropoffHeight(IsTransportParkedInElevator,
                    machine.Configuration.Data.MoveCancellationToken);

                // Pause until the transport is above the destination station's drop-off location.
                // If it is already above, this will continue immediately.
                machine.SetPauseUntilConditionOfTransport("IsTransportAboveDropoffHeight", IsTransportAboveDropoffHeight(dockStation));

                // If we didn't already prepare for handoff before the Z move, do it now.
                if (!prepareForHandoffEarly)
                {
                    // Pause until the source station is ready to start hand-off prep.
                    machine.SetPauseUntilConditionOfStation("IsStationDropoffReady?", IsStationDropoffReady);

                    // Prepare the source station for handoff.  Quit this machine if the PrepareForHandoff fails.
                    machine.SetActivityTrayHandlerPrepareForHandoff();
                }

                // Pause until the destination station is handoff-prepared for drop-off.
                machine.SetPauseUntilConditionOfStation("IsStationHandoffPrepared?", IsStationHandoffPrepared);
                // Wait for transport to be stopped at location.
                machine.SetPauseUntilConditionOfTransport("IsTransportStoppedAtLocation?", IsTransportParkedAtDropoffHeight(dockStation));
                
                // Ensure destination station is safe to enter to drop-off tray
                machine.SetActivityTrayHandlerSafeToEnter();

                // Don't allow the machine to be stopped once the drop-off has started.
                machine.AddActivity(new DynamicActivity("Set CanStop=false", () => machine.CanStop = false));

                // Possibly finish before doing the drop-off if the operation was cancelled, regardless of tray aborted or not.
                // CompleteTrayDropoff operation will do recovery if we finish here.
                machine.SetFinishOrContinueForMoveStopped();

                // From this point on, TrayMover should stop movements if this machine encounters an error.
                machine.AddActivity(new DynamicActivity("Set machine HaltOnError", () => machine.HaltOnError = true));

                // Tell transport to put the tray. Quit if the move fails. 
                // This will not act until transport is at the drop-off height.
                machine.SetActivityTransportPerformDropoff();

                // If dropping off to a stainer, make sure transport is clear of the stainer mount before allowing complete handoff.
                if (station.Type == StationType.Stainer)
                {
                //TODO: ASK TEST/DEV TEAMS IF STAINER UPPER&LOWER LOADOFFSETS ARE EVER DIFFERENT THAN UPPER&LOWER OFFSETS.
                //TODO: IF NOT, CAN ELIMINATE THIS AND REMOVE THE LOADOFFSETS FROM THE CONFIGURATION.
                //TODO: IF STILL NEEDED, ELIMINATE SPECIAL CASE HERE BY MAKING STAINERS OVERRIDE UPPEROFFSET & LOWEROFFSET WHICH INCORPORATES ANY EXTRA OFFSET INTO THE ONE MOVE.
                    // Ensure the transport made it to below the stainer mount.  If it already is below the stainer, just move on.
                    machine.SetPauseUntilConditionOfTransport("IsTransportBelowPickupHeight?", IsTransportBelowPickupHeight(dockStation));
                }
            }
            else
            {
                throw new Exception("Begin tray drop-off failed to build: no destination station, or station is not ready.");
            }
        }

        public void AssembleMachineForCompleteTrayDropoff(TransportOperationMachine machine)
        {
            var station = machine.Station as IStation;
            if (station != null)
            {
                // This machine doesn't know if Transport is safe or not.
                machine.AddActivity(new DynamicActivity("Set machine HaltOnError", () => machine.HaltOnError = true));

                // If the prior machine was stopped, skip tray reassignment.
                if (!machine.IsStopped)
                {
                    //TODO: change this to go by interfaces once SlideId is refactored.
                    if (station.Type != StationType.SlideId)
                    {
                        // Wait for transportation to stop moving 
                        machine.AddContinueCondition(new DynamicConstraint<ITransport>("Transport Missing Tray?", machine.Transport,
                            (td) => IsTransportParkedWithoutTray(machine.Transport)));
                    }

                    // Wait for the destination station IsTrayDetected trigger.
                    // Slide id does not have a tray present sensor, so it should always be faking it.
                    machine.AddContinueCondition(new DynamicConstraint<ITrayDetector>("Tray Detected At Destination?", machine.Station, 
                        (td) => td.IsTrayDetected));

                    // Reassign the tray from transport to destination. 
                    // Needed to reassign the Tray before performing complete handoff to ensure the tray cooling is performed.
                    machine.SetActivityReassignTray(machine.Transport, machine.Station);
                }

                if (station.Type != StationType.Stainer)
                {
                    // This machine knows it is below the station and in elevator now, so no need for transport error.
                    machine.AddActivity(new DynamicActivity("Set machine HaltOnError", () => machine.HaltOnError = false));
                }

                // Complete the handoff.  Quit this machine if the CompleteHandoff fails.
                machine.SetActivityTrayHandlerCompleteHandoff();

                // Pause until destination station has completed its handoff, which indicates the overall tray move is done.
                machine.SetPauseUntilConditionOfStation("IsStationNotHandoffPrepared?", IsStationNotHandoffPrepared);

                // If neither the destination nor the transport detect a tray when the machine exits, set tray to Lost state.
                machine.UseFinalExitBehavior(new ActOnResultActivity<bool>("Set Tray Lost",
                    () => IsTrayLost(machine.Transport, machine.Station) && machine.Tray != null, true,
                    () => machine.Tray.OnDetectionLost()));
            }
            else
            {
                throw new Exception("Complete tray drop-off failed to build: no destination station.");
            }
        }
    }
}
