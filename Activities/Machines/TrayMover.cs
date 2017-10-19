using System.Globalization;
using MicroLibrary;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using Ventana.Core.Activities.Builders;
using Ventana.Core.Activities.Parts;
using Ventana.Core.Base;
using Ventana.Core.Base.Activities;
using Ventana.Core.Base.BusinessObjects;
using Ventana.Core.Base.BusinessObjects.Generic;
using Ventana.Core.Base.Common;
using Ventana.Core.Base.Configuration;
using Ventana.Core.Base.Transportation;
using Ventana.Core.ExceptionHandling;
using Ventana.Core.Logging;
using Ventana.Core.Utilities;
using Ventana.Core.Utilities.ExtensionMethods;

namespace Ventana.Core.Activities.Machines
{
    public class TrayMover : IActivityMachine
    {
        private readonly object _subMachineLock = new object();
        private readonly object _disposeLock = new object();
        private bool _disposed;
        private SimpleDispatcher _dispatcher;
        private IActivityMachineBuilder _beginPickupBuilder;
        private IActivityMachineBuilder _completePickupBuilder;
        private IActivityMachineBuilder _beginDropoffBuilder;
        private IActivityMachineBuilder _completeDropoffBuilder;
        private IInstrument _instrument;
        private bool _isComplete = false;
        protected DynamicConfiguration _beginPickupConfig;
        protected DynamicConfiguration _completePickupConfig;
        protected DynamicConfiguration _beginDropoffConfig;
        protected DynamicConfiguration _completeDropoffConfig;

        /// <summary>
        /// A ResetEvent that will signal when this TrayMover is finished.
        /// </summary>
        private readonly ManualResetEvent _waitForFinishEvent = new ManualResetEvent(true);

        public TrayMover(string srcName, string dstName)
        {
            Name = "TrayMover(" + srcName + "->" + dstName + ")";
            IsSynchronous = false;
            LockResources = true;
            SourceLockOwner = this;
            CompletionCause = CompletionCause.Pending;
            WaitWatcher = new MicroStopwatch();
            _dispatcher = new SimpleDispatcher(Name);
        }

        public event EventHandler<TimeStampedEventArgs> Started;
        public event EventHandler<TimeStampedEventArgs> Finished;
        public event EventHandler<FaultedEventArgs> Faulted;
        public event EventHandler<TimeStampedEventArgs> Interrupted;
        public event EventHandler<TimeStampedEventArgs> Expired;
        public event EventHandler<TransportOperationEventArgs> OperationBeginning;

// This code triggers a warning that the ExecuteRequested event is "never used".
// Wrapped this declaration in pragma warning disable/restore directives to avoid the warning so we can start treating warnings as errors.
// TODO: Remove this warning condition and the corresponding warning disable/restore directives
#pragma warning disable 67
        public event EventHandler ExecuteRequested;
#pragma warning restore 67

        public IConfiguration Configuration { get; set; }

        protected TransportOperation CurrentOperation { get; set; }
        public Stopwatch WaitWatcher { get; private set; }
        public Guid Id { get; set; }
        public string Name { get; set; }
        public bool IsCurrentlyActive { get; private set; }
        public bool IsInterruptable { get; set; }
        public bool IsStopped { get; private set; }
        public bool IsSynchronous { get; set; }
        public bool LockResources { get; set; }
        public object SourceLockOwner { get; set; }
        /// <summary>
        /// Indicates the reason that this TrayMover completed, or if it has not completed yet.
        /// </summary>
        public CompletionCause CompletionCause { get; set; }

        /// <summary>
        /// BEWARE: this is not being set yet.
        /// </summary>
        public ExecutableState ExecutableState { get; private set; }

        public int NumberOfRetries { get; internal set; }
        public IActivityMachineBuilder Builder { get; set; }
        public ITrayDetector Source { get; internal set; }
        public ITrayHandler Destination { get; internal set; }
        public ITransport Transport { get; internal set; }
        public IInstrument Instrument
        {
            get { return _instrument; }
            internal set
            {
                if (_instrument == null && value != null)
                {
                    _instrument = value;
                    _instrument.BusinessLayerPropertyChanged += HandleInstrumentStateChanged;
                }
            }
        }

        protected TransportOperationMachine BeginPickupMachine { get; set; }
        protected TransportOperationMachine CompletePickupMachine { get; set; }
        protected TransportOperationMachine BeginDropoffMachine { get; set; }
        protected TransportOperationMachine CompleteDropoffMachine { get; set; }

        protected IActivityMachineBuilder BeginPickupBuilder
        {
            get 
            { 
                if (_beginPickupBuilder == null)
                {
                    _beginPickupConfig = Configurations.CreateTransportOperationConfig(TransportOperation.BeginPickup,
                                             Source, Transport, Instrument, SourceLockOwner);
                    _beginPickupBuilder = ActivityMachineBuilderLoader.GetActivityMachineBuilder(_beginPickupConfig);
                }
                return _beginPickupBuilder; 
            }
        }

        protected IActivityMachineBuilder CompletePickupBuilder
        {
            get 
            { 
                if (_completePickupBuilder == null)
                {
                    _completePickupConfig = Configurations.CreateTransportOperationConfig(TransportOperation.CompletePickup,
                                                Source, Transport, Instrument, SourceLockOwner, Destination as ITransportDock);
                    _completePickupBuilder = ActivityMachineBuilderLoader.GetActivityMachineBuilder(_completePickupConfig);
                }
                return _completePickupBuilder; 
            }
        }

        protected IActivityMachineBuilder BeginDropoffBuilder
        {
            get 
            { 
                if (_beginDropoffBuilder == null)
                {
                    _beginDropoffConfig = Configurations.CreateTransportOperationConfig(TransportOperation.BeginDropoff,
                                              Destination, Transport, Instrument, SourceLockOwner);
                    _beginDropoffBuilder = ActivityMachineBuilderLoader.GetActivityMachineBuilder(_beginDropoffConfig);
                }
                return _beginDropoffBuilder; 
            }
        }

        protected IActivityMachineBuilder CompleteDropoffBuilder
        {
            get 
            { 
                if (_completeDropoffBuilder == null)
                {
                    _completeDropoffConfig = Configurations.CreateTransportOperationConfig(TransportOperation.CompleteDropoff,
                                                 Destination, Transport, Instrument, SourceLockOwner);
                    _completeDropoffBuilder = ActivityMachineBuilderLoader.GetActivityMachineBuilder(_completeDropoffConfig);
                }
                return _completeDropoffBuilder; 
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        public void Execute()
        {
            if (IsCurrentlyActive || IsStopped)
            {
                Quit();
                return;
            }

            IsCurrentlyActive = true;
            // Do this after setting machine active so that the event is raised if quit is required.
            if (Destination.State.IsOneOf(StationState.Disabling, StationState.Disabled) || CheckForError(Destination))
            {
                Quit();
                return;
            }

            // For diagnostics
            Transport.BusinessLayerPropertyChanged += HandleTransportPropertyChanged;

            // First, need to initialize all the sub-machines starting at begin-pickup.
            var operation = (Source == Transport && Transport.HasTray
                ? TransportOperation.BeginDropoff
                : TransportOperation.BeginPickup);
            var firstMachine = InitializeSubmachine(operation);

            // second, need to obtain required locks.
            if (LockResources && !ObtainResourceLocks())
            {
                Quit();
                return;
            }

            CurrentOperation = operation;
            // Third, notify world that this machine has started.
            OnStarted();

            OnOperationBeginning(operation);

            // Finally, Start execution with the first machine.
            _waitForFinishEvent.Reset();
            firstMachine.Execute();
        }

        private bool CheckForError(object trayHandler)
        {
            var station = trayHandler as IStation;
            return station != null && station.HasErred;
        }

        #region ISharedResource Locking
        public bool ObtainResourceLocks()
        {
            bool result = Transport.ObtainLock(SourceLockOwner);
            if (Source != null)
            {
                result &= Source.ObtainLock(SourceLockOwner);
            }
            result &= Destination.ObtainLock(SourceLockOwner);
            return result;
        }

        public void ReleaseResourceLocks()
        {
            Transport.ReleaseLock(SourceLockOwner);
            if (Source != null)
            {
                Source.ReleaseLock(SourceLockOwner);
            }
            Destination.ReleaseLock(SourceLockOwner);
        }
        #endregion

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        public void Quit(string reason = null)
        {
            if (!string.IsNullOrEmpty(reason))
            {
                LogService.Log(LogType.System, LogMessageType.Debug, GetType().Name, "Quit called: " + reason);
            }
            Quit(false);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        public void EmergencyQuit(string reason = null)
        {
            if (!string.IsNullOrEmpty(reason))
            {
                LogService.Log(LogType.System, LogMessageType.Debug, GetType().Name, "Quit called: " + reason);
            }
            Quit(true);
        }

        public void Pause()
        {
            throw new NotImplementedException();
        }

        public void Resume()
        {
            throw new NotImplementedException();
        }

        public IActivityMachine Copy()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Stops a tray move if it is safe to do so.
        /// It is only safe to stop a TrayMover during Pick-up and during the beginning of
        /// a BeginDropoff operation.  Once the transportation has begun tray drop-off to a station,
        /// the BeginDropoff machine no longer permits itself to be stopped.
        /// If the move was in a place that could be stopped, Transport will have the tray when this 
        /// TrayMover completes.   Otherwise, the destination station will have the tray.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        public void SafeStop()
        {
            lock (_subMachineLock)
            {
                if (_isComplete || IsStopped)
                {
                    return;
                }

                if (CurrentOperation == TransportOperation.BeginPickup || CurrentOperation == TransportOperation.CompletePickup)
                {
                    // In this case, the drop-off machines won't be built or executed.
                    IsStopped = true;
                    // It is not necessary to stop either pick-up machine, since they do not allow stopping internally.
                }
                // Communicate to drop-off sub-machines that this was stopped.  Only BeginDropoff
                // will interrupt an actual move.  CompleteDropoff will recover.
                else if (BeginDropoffMachine != null)
                {
                    BeginDropoffMachine.Stop();

                    // BeginDropoff may have ignored the stop attempt, in which case TrayMover is not stopped.
                    IsStopped = BeginDropoffMachine.IsStopped;
                }

                LogService.Log(LogType.System, LogMessageType.Debug, GetType().Name,
                    string.Format(CultureInfo.InvariantCulture, "Stopping {0} was {1}.", Name, (IsStopped ? "successful" : "not allowed")));
            }
        }

        private void Quit(bool immediately)
        {
            lock (_subMachineLock)
            {
                //TODO:  This method might be able to call TeardownForInterrupt instead of doing its own thing here.
                
                if (IsCurrentlyActive)
                {
                    // this before interrupted event because usually teardown happens before the event but not in this case.
                    IsCurrentlyActive = false;
                    OnInterrupted();
                }

                Teardown(immediately);
                Dispose();
            }
        }

        /// <summary>
        /// Block the caller's thread until this has finished the move.
        /// </summary>
        public void WaitUntilFinished()
        {
            bool wasSignaled = false;
            // Added a loop to make this method more reliable.  Now is doesn't hang on the wait
            // when the ManualResetEvent is disposed before Set notifies all.
            while (!_waitForFinishEvent.SafeWaitHandle.IsClosed && !wasSignaled)
            {
                wasSignaled = _waitForFinishEvent.WaitOne(1000);
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        private void HandleSubMachineFinished(object sender, TimeStampedEventArgs e)
        {
            var doneMachine = sender as TransportOperationMachine;
            TransportOperation nextOperation;
            TransportOperationMachine nextMachine = null;
            // lock here because the submachine references will be accessed.
            lock (_subMachineLock)
            {
                nextMachine = GetNextOperation(doneMachine, out nextOperation);

                if (doneMachine.Operation == TransportOperation.CompleteDropoff)
                {
                    TeardownForFinished();
                }
                else 
                {
                    if (nextMachine != null && Transport.State.IsNoneOf(StationState.Stopped, StationState.Disabled)
                        && !(Transport as IStation).HasErred)
                    {
                        CurrentOperation = nextOperation;
                        OnOperationBeginning(nextOperation);
                        // Last step, execute the next machine.
                        nextMachine.Execute();
                    }
                    else
                    {
                        TeardownForInterruption();
                    }
                }
            }

            if (nextMachine == null && !_isComplete)
            {
                Quit();
                LogService.Log(LogType.System, LogMessageType.Error, GetType().Name,
                    string.Format(CultureInfo.InvariantCulture, "Machine '{0}' failed due to missing {1} sub-machine.", Name, nextOperation.ToString()));
            }
        }

        private TransportOperationMachine GetNextOperation(TransportOperationMachine lastMachine, out TransportOperation nextOperation)
        {
            switch (lastMachine.Operation)
            {
                case TransportOperation.BeginPickup:
                    // Keep operating if mover was stopped for a tray abort because the tray has to be
                    // picked up anyway.
                    if (IsStopped && !lastMachine.Tray.IsAborted) break;
                    InitializeSubmachine(TransportOperation.CompletePickup);
                    nextOperation = TransportOperation.CompletePickup;
                    return CompletePickupMachine;

                case TransportOperation.CompletePickup:
                    // Skip the drop-off if this mover was stopped.
                    if (IsStopped) break;
                    InitializeSubmachine(TransportOperation.BeginDropoff);
                    nextOperation = TransportOperation.BeginDropoff;
                    return BeginDropoffMachine;

                case TransportOperation.BeginDropoff:
                    // Keep operating if mover was stopped for a tray abort because we don't know
                    // how much of the drop-off has run.  CompleteDropoff can recover safely.
                    if (IsStopped && !lastMachine.Tray.IsAborted) break;
                    InitializeSubmachine(TransportOperation.CompleteDropoff);
                    nextOperation = TransportOperation.CompleteDropoff;
                    return CompleteDropoffMachine;
            }
            nextOperation = TransportOperation.BeginPickup;
            return null;
        }

        /// <summary>
        /// Handle when one of this Machine's sub-machines is expired.  This will
        /// only happen by an internal expiration on one of those machines.
        /// </summary>
        /// <param name="sender">object</param>
        /// <param name="e">event args</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        private void HandleSubMachineExpired(object sender, TimeStampedEventArgs e)
        {
            var expiredMachine = sender as TransportOperationMachine;

            lock (_subMachineLock)
            {
                if (_isComplete)
                {
                    return;
                }

                if (IsStopped)
                {
                    // If the move was cancelled, don't retry.
                    TeardownForInterruption();
                }
                else if (NumberOfRetries > 0)
                {
                    var logMessage = string.Format(CultureInfo.InvariantCulture, "Tray move failed at {0} due to expiration. Value of {0}'s IsTrayDetected property: [{1}]",
                        expiredMachine.Station.Name, expiredMachine.Station.IsTrayDetected);
                    LogService.Log(LogType.System, LogMessageType.Error, GetType().Name, logMessage);

                    Retry(expiredMachine);
                }
                else
                {
                    RaiseTransportationException(expiredMachine, "expiration");
                    TeardownForExpiration();
                }
            }
        }

        /// <summary>
        /// Handle when one of this Machine's sub-machines is interrupted.  This will
        /// only happen by an external Quit on one of those machines, since Quit here
        /// disables the Interrupted event handler before stopping submachines.
        /// </summary>
        /// <param name="sender">object</param>
        /// <param name="e">event args</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        private void HandleSubMachineInterrupted(object sender, TimeStampedEventArgs e)
        {
            var interruptedMachine = sender as TransportOperationMachine;

            lock (_subMachineLock)
            {
                if (_isComplete)
                {
                    return;
                }
                
                // If the move was cancelled, don't retry.
                if (!IsStopped && NumberOfRetries > 0)
                {
                    var logMessage = string.Format(CultureInfo.InvariantCulture, "Tray move failed at {0} due to internal interruption. Value of {0}'s IsTrayDetected property: [{1}]",
                        interruptedMachine.Station.Name, interruptedMachine.Station.IsTrayDetected);
                    LogService.Log(LogType.System, LogMessageType.Error, GetType().Name, logMessage);

                    Retry(interruptedMachine);
                }
                else
                {
                    // Only handle as an exception if the Mover was interrupted internally, not for an external Cancel (or quit).
                    if (!IsStopped)
                    {
                        RaiseTransportationException(interruptedMachine, "interruption");
                    }
                    TeardownForInterruption();
                }
            }
        }

        /// <summary>
        /// Handle when one of this Machine's sub-machines is faulted.
        /// </summary>
        /// <param name="sender">object</param>
        /// <param name="e">FaultedEventArgs</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        private void HandleSubMachineFaulted(object sender, FaultedEventArgs e)
        {
            var faultedMachine = sender as TransportOperationMachine;

            lock (_subMachineLock)
            {
                if (_isComplete)
                {
                    return;
                }
                
                if (IsStopped)
                {
                    // If the move was cancelled, don't retry and don't give a fault.
                    TeardownForInterruption();
                }
                else if (NumberOfRetries > 0)
                {
                    var message = string.Format(CultureInfo.InvariantCulture, "Tray '{0}' failed{1} due to fault: {2}", faultedMachine.Name,
                        faultedMachine.Station == null ? string.Empty : " at " + faultedMachine.Station.Name,
                        e.Cause == null ? "unknown cause" : e.Cause.Message);
                    LogService.Log(LogType.System, LogMessageType.Error, GetType().Name, message, e.Cause);

                    Retry(faultedMachine);
                }
                else
                {
                    RaiseTransportationException(faultedMachine, "fault", e.Cause);
                    TeardownForFault(e);
                }
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        private void Retry(TransportOperationMachine startingPoint)
        {
            LogService.Log(LogType.System, LogMessageType.Debug, GetType().Name,
                string.Format(CultureInfo.InvariantCulture, "Retrying Tray move at '{0}'.", startingPoint.Station.Name));
            NumberOfRetries--;
            // Dispose of the old machines.
            DisposeSubmachines();
            // Create new first machine to execute.
            InitializeSubmachine(startingPoint.Operation);
            // Restart execution of whichever machine died, but don't raise the Started event.
            switch (startingPoint.Operation)
            {
                case TransportOperation.BeginPickup:
                    BeginPickupMachine.Execute();
                    break;

                case TransportOperation.CompletePickup:
                    CompletePickupMachine.Execute();
                    break;

                case TransportOperation.BeginDropoff:
                    BeginDropoffMachine.Execute();
                    break;

                case TransportOperation.CompleteDropoff:
                    CompleteDropoffMachine.Execute();
                    break;
            }
        }

        private void Teardown(bool immediately = false)
        {
            IsCurrentlyActive = false;

            DisposeSubmachines(immediately);

            Transport.BusinessLayerPropertyChanged -= HandleTransportPropertyChanged;
            _instrument.BusinessLayerPropertyChanged -= HandleInstrumentStateChanged;

            // Transport can get stuck in running state when a TrayMover is stopped or interrupted.
            if (Transport.State == StationState.Running && !(Transport as IStation).HasErred)
            {
                Transport.State = StationState.Idle;
            }
            // Now safe to release actor lock
            ReleaseResourceLocks();
            try
            {
                // Signal anyone waiting on this TrayMover that it is completely done.
                _waitForFinishEvent.Set();
            }
            catch (ObjectDisposedException)
            {
                //nothing to do. wait handle was disposed
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        private void Dispose()
        {
            lock (_disposeLock)
            {
                if (_disposed)
                {
                    return;
                }
                _disposed = true;
            }
            if (_dispatcher != null)
            {
                _dispatcher.Dispose();
            }
            _dispatcher = null;

            try
            {
                _waitForFinishEvent.Dispose();
            }
            catch (ObjectDisposedException)
            {
                //nothing to do. wait handle was disposed
            }
            LogService.Log(LogType.System, LogMessageType.Debug, GetType().Name,
                string.Format(CultureInfo.InvariantCulture, "{0} is disposed.", Name));
        }

        private void TeardownForFinished()
        {
            _isComplete = true;
            CompletionCause = CompletionCause.Finished;

            Teardown();
            // Signal that it finished.  This MUST happen between Teardown and Dispose.
            OnFinished();
            Dispose();
        }

        private void TeardownForInterruption()
        {
            _isComplete = true;
            CompletionCause = CompletionCause.Interrupted;

            Teardown();
            // Signal that it was interrupted.  This MUST happen between Teardown and Dispose.
            OnInterrupted();
            Dispose();
        }

        private void TeardownForExpiration()
        {
            _isComplete = true;
            CompletionCause = CompletionCause.Expired;

            Teardown();
            // Signal that it expired.  This MUST happen between Teardown and Dispose.
            OnExpired();
            Dispose();
        }

        private void TeardownForFault(FaultedEventArgs e)
        {
            _isComplete = true;
            CompletionCause = CompletionCause.Faulted;

            Teardown();
            // Signal that it faulted.  This MUST happen between Teardown and Dispose.
            OnFaulted(e);
            Dispose();
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        private void OnOperationBeginning(TransportOperation operation)
        {
            LogService.Log(LogType.System, LogMessageType.Debug, GetType().Name,
                string.Format(CultureInfo.InvariantCulture, "Machine '{0}' initiating execution of {1} sub-machine.", Name, operation));
            if (OperationBeginning != null)
            {
                OperationBeginning(this, new TransportOperationEventArgs() { Operation = operation });
            }
        }

        #region Sub-Machine management

        /// <summary>
        /// Initialize one of the four transport operation machine types.  Both Pick-up machines have
        /// CanStop turned off because there are no circumstances where a Pick-up operation can be
        /// stopped, even though the TrayMover as a whole can be stopped during pick-up.  Both
        /// Drop-off machines have CanStop turned on initially so that either drop-off operation can
        /// be stopped.  If this TrayMover has already been stopped, then this will also
        /// stop a newly constructed CompleteDropoff machine before returning it.
        /// </summary>
        /// <param name="startingType"></param>
        /// <returns>TransportOperationMachine</returns>
        protected TransportOperationMachine InitializeSubmachine(TransportOperation startingType)
        {
            TransportOperationMachine initialized = null;
            lock (_subMachineLock)
            {
                switch (startingType)
                {
                    case TransportOperation.BeginPickup:
                        BeginPickupMachine = CreateSubmachine("BeginTrayPickup", BeginPickupBuilder, _beginPickupConfig);
                        initialized = BeginPickupMachine;
                        break;

                    case TransportOperation.CompletePickup:
                        CompletePickupMachine = CreateSubmachine("CompleteTrayPickup", CompletePickupBuilder, _completePickupConfig);
                        initialized = CompletePickupMachine;
                        break;

                    case TransportOperation.BeginDropoff:
                        BeginDropoffMachine = CreateSubmachine("BeginTrayDropoff", BeginDropoffBuilder, _beginDropoffConfig);
                        BeginDropoffMachine.CanStop = true;
                        initialized = BeginDropoffMachine;
                        break;

                    case TransportOperation.CompleteDropoff: 
                        CompleteDropoffMachine = CreateSubmachine("CompleteTrayDropoff", CompleteDropoffBuilder, _completeDropoffConfig);
                        CompleteDropoffMachine.CanStop = true;
                        // CompleteDropoff needs to know if the mover was already stopped
                        if (IsStopped)
                        {
                            CompleteDropoffMachine.Stop();
                        }
                        initialized = CompleteDropoffMachine;
                        break;
                }
            }
            return initialized;
        }

        protected void DisposeSubmachines(bool emergencyQuit = false)
        {
            TransportOperationMachine beginPickup = null;
            TransportOperationMachine endPickup = null;
            TransportOperationMachine beginDropoff = null;
            TransportOperationMachine endDropoff = null;
            // Get local references so that the lock isn't held while operating on each machine.
            lock (_subMachineLock)
            {
                beginPickup = BeginPickupMachine;
                BeginPickupMachine = null;
                endPickup = CompletePickupMachine;
                CompletePickupMachine = null;
                beginDropoff = BeginDropoffMachine;
                BeginDropoffMachine = null;
                endDropoff = CompleteDropoffMachine;
                CompleteDropoffMachine = null;
            }

            DisposeSubmachine(beginPickup, emergencyQuit);
            DisposeSubmachine(endPickup, emergencyQuit);
            DisposeSubmachine(beginDropoff, emergencyQuit);
            DisposeSubmachine(endDropoff, emergencyQuit);
        }

        private void DisposeSubmachine(TransportOperationMachine submachine, bool emergencyQuit)
        {
            if (submachine != null)
            {
                submachine.Finished -= HandleSubMachineFinished;
                submachine.Interrupted -= HandleSubMachineInterrupted;
                submachine.Expired -= HandleSubMachineExpired;
                submachine.Faulted -= HandleSubMachineFaulted;
                if (emergencyQuit)
                {
                    submachine.EmergencyQuit();
                }
                else
                {
                    submachine.Quit();
                }
            }
        }

        private TransportOperationMachine CreateSubmachine(string name, IActivityMachineBuilder builder, DynamicConfiguration config)
        {
            var submachine = new TransportOperationMachine(name, _dispatcher);
            submachine.Builder = builder;
            submachine.Configuration = config;
            submachine.Finished += HandleSubMachineFinished;
            submachine.Interrupted += HandleSubMachineInterrupted;
            submachine.Expired += HandleSubMachineExpired;
            submachine.Faulted += HandleSubMachineFaulted;
            return submachine;
        }
        #endregion

        #region Event Handlers

        private void HandleTransportPropertyChanged(object sender, PropertyChangedEventArgs args)
        {
            var transport = Transport as IStation;
            if (args.PropertyName.Equals(Transport.PropertyToString(() => Transport.State)))
            {
                if (Transport.State == StationState.Idle)
                {
                    WaitWatcher.Restart();
                }
            }
            //else if (transport != null && args.PropertyName.Equals(transport.PropertyToString(() => transport.HasErred)))
            //{
            //    if (transport.HasErred)
            //    {
            //        Quit();
            //    }
            //}
        }

        private void HandleInstrumentStateChanged(object sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName.Equals(_instrument.PropertyToString(() => _instrument.State)))
            {
                if (_instrument.State.IsOneOf(InstrumentState.Stopping, InstrumentState.Terminating))
                {
                    Quit();
                }
            }
        }
        #endregion

        #region Event Raising
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        private void OnInterrupted()
        {
            if (Interrupted != null)
            {
                try
                {
                    LogService.Log(LogType.System, LogMessageType.Debug, GetType().Name,
                        string.Format(CultureInfo.InvariantCulture, "{0} raising Interrupted event.", Name));
                    Interrupted(this, new TimeStampedEventArgs());
                }
                catch (Exception e)
                {
                    LogService.Log(LogType.System, LogMessageType.Error, GetType().Name,
                                   e.GetType().Name + " while raising Interrupted event. (" + Name + ")", e);
                }
            }
        }

        private void RaiseTransportationException(TransportOperationMachine machineInProgress, string description, Exception innerException = null)
        {
            // If transportation already in error state, just escape.
            if ((Transport as IStation).HasErred)
            {
                return;
            }
            AtlasException ex;
            if (!(machineInProgress.Station as IStation).HasErred && machineInProgress.IsExpired)
            {
                ex = new TrayMoveExpirationException(null, Transport, machineInProgress.Station.Name, machineInProgress.Operation.ToString());
            }
            else if (machineInProgress.HaltOnError)
            {
                // This will give transportation an error and a Busy ResourceAvailability.
                ex = new TrayMoveComponentException(null, innerException, Transport, machineInProgress.Station.Name, machineInProgress.Operation.ToString());
            }
            else
            {
                ex = new TrayMoveInternalException(null, innerException, Transport, machineInProgress.Station.Name, machineInProgress.Operation.ToString(), description);
            }
            ex.Handle();
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        private void OnFinished()
        {
            if (Finished != null)
            {
                try
                {
                    LogService.Log(LogType.System, LogMessageType.Debug, GetType().Name,
                        string.Format(CultureInfo.InvariantCulture, "{0} raising Finished event.", Name));
                    Finished(this, new TimeStampedEventArgs());
                }
                catch (Exception e)
                {
                    LogService.Log(LogType.System, LogMessageType.Error, GetType().Name,
                                   e.GetType().Name + " while raising Finished event. (" + Name + ")", e);
                }
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        private void OnStarted()
        {
            if (Started != null)
            {
                try
                {
                    Started(this, new TimeStampedEventArgs());
                }
                catch (Exception e)
                {
                    LogService.Log(LogType.System, LogMessageType.Error, GetType().Name,
                                   e.GetType().Name + " while raising Started event. (" + Name + ")", e);
                }
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        private void OnExpired()
        {
            if (Expired != null)
            {
                try
                {
                    LogService.Log(LogType.System, LogMessageType.Debug, GetType().Name,
                        string.Format(CultureInfo.InvariantCulture, "{0} raising Expired event.", Name));
                    Expired(this, new TimeStampedEventArgs());
                }
                catch (Exception e)
                {
                    LogService.Log(LogType.System, LogMessageType.Error, GetType().Name,
                                   e.GetType().Name + " while raising Expired event. (" + Name + ")", e);
                }
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        private void OnFaulted(FaultedEventArgs args)
        {
            if (Faulted != null)
            {
                try
                {
                    LogService.Log(LogType.System, LogMessageType.Debug, GetType().Name,
                        string.Format(CultureInfo.InvariantCulture, "{0} raising Faulted event.", Name));
                    Faulted(this, args);
                }
                catch (Exception e)
                {
                    LogService.Log(LogType.System, LogMessageType.Error, GetType().Name,
                                   e.GetType().Name + " while raising Faulted event. (" + Name + ")", e);
                }
            }
        }
        #endregion




        /// <summary>
        /// Get or set the name to be given to a local dispatcher's thread.
        /// </summary>
        public string ThreadName { get; set; }
        public bool IsFinished
        {
            get { return ExecutableState == ExecutableState.Finished && CompletionCause == CompletionCause.Finished; }
        }


    }
}
