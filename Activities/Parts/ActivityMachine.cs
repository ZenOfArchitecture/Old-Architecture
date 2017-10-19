using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Ventana.Core.Activities.Executables;
using Ventana.Core.Base;
using Ventana.Core.Base.Activities;
using Ventana.Core.Base.Common;
using Ventana.Core.Base.Configuration;
using Ventana.Core.Logging;
using Ventana.Core.Utilities;
using Exception = System.Exception;

namespace Ventana.Core.Activities.Parts
{
    /// <summary>
    /// Abstract ActivityMachine class.
    /// </summary>
    public abstract class ActivityMachine : AsyncTimedExecutable, IActivityMachine
    {
        protected readonly ManualResetEvent _finishedEvent = new ManualResetEvent(false);
        private readonly object _connectorTraversalLock = new Object();

        /// <summary>
        /// A lock object to protect execution of this activity machine.
        /// </summary>
        protected readonly object _executionLock = new object();
        /// <summary>
        /// A lock object to protect the transition between two nodes of this activity machine.
        /// </summary>
        protected readonly object _transitionLock = new object();

        protected volatile ExecutableState _executableState;
        protected volatile CompletionCause _completionCause;
        protected SimpleDispatcher _dispatcher;
        protected bool _isEditable = false;
        protected bool _isAssembled = false;
        protected bool _hasLocalDispatcher = false;
        private UmlNode _currentNode;

        /// <summary>
        /// CTOR that creates an ActivityMachine that will halt and have
        /// a CompletionCause of Faulted upon an exception.
        /// </summary>
        /// <param name="name"></param>
        protected ActivityMachine(string name)
            : base(name)
        {
            ExecutableState = ExecutableState.NotStarted;
            CompletionCause = CompletionCause.Pending;
            ExecuteTriggers = new List<IUmlTrigger>();
            HaltOnFault = true;
        }

        ~ActivityMachine()
        {
            Dispose(false);
        }

        /// <summary>
        /// 
        /// </summary>
        private event EventHandler _executeRequested;

        /// <summary>
        /// Raised when this machine is interrupted prematurely but without an error.
        /// </summary>
        public event EventHandler<TimeStampedEventArgs> Interrupted;

        /// <summary>
        /// The Quitting event is raised after the CompletionCause changes from Pending to one of the other causes.
        /// </summary>
        public event EventHandler<TimeStampedEventArgs> Quitting;

        /// <summary>
        /// The CurrentNodeChanged event is raised when the current node value has changed.  This happens after a
        /// previous node is exited and a new node is entered.
        /// </summary>
        public event EventHandler<ActivityMachineEventArgs> CurrentNodeChanged;

        /// <summary>
        /// Raised when this machine has paused execution.
        /// </summary>
        public event EventHandler<PausedEventArgs> Paused;

        /// <summary>
        /// Raised when this machine has resumed execution after being paused.
        /// </summary>
        public event EventHandler Resumed;

        public bool IsFaulted
        {
            get { return ExecutableState == ExecutableState.Finished && CompletionCause == CompletionCause.Faulted; }
        }

        public bool IsExpired
        {
            get { return ExecutableState == ExecutableState.Finished && CompletionCause == CompletionCause.Expired; }
        }

        public bool IsInterrupted
        {
            get { return ExecutableState == ExecutableState.Finished && CompletionCause == CompletionCause.Interrupted; }
        }

        public bool IsFinished
        {
            get { return ExecutableState == ExecutableState.Finished && CompletionCause == CompletionCause.Finished; }
        }

        /// <summary>
        /// Raised when a special Execute trigger signals that this machine needs to be executed.
        /// This even only allows one subscriber.
        /// </summary>
        public event EventHandler ExecuteRequested
        {
            add
            {
                // only allow one subscriber.
                if (ExecutableState == ExecutableState.NotStarted && _executeRequested == null)
                {
                    ActivateExecutionTriggers();
                    _executeRequested += value;
                }
            }
            remove
            {
                if (_executeRequested != null)
                {
                    DeactivateExecutionTriggers();
                    _executeRequested -= value;
                }
            }
        }

        public IConfiguration Configuration { get; set; }

        /// <summary>
        /// Get or set the name to be given to a local dispatcher's thread.
        /// </summary>
        public string ThreadName { get; set; }

        /// <summary>
        /// Gets or sets the builder that this machine will use at the beginning of its execution.
        /// </summary>
        public IActivityMachineBuilder Builder { get; set; }

        /// <summary>
        /// Gets or set a value indicating whether or not this IActivityMachine blocks the caller of Execute.
        /// The default behavior is asynchronous (not blocking).
        /// </summary>
        public bool IsSynchronous { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether or not this IActivityMachine's currently
        /// executing activity can be interrupted.
        /// </summary>
        public bool IsInterruptable { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether or not this machine quits when a fault is encountered.
        /// </summary>
        public bool HaltOnFault { get; set; }
        
        /// <summary>
        /// Gets the exception that caused the machine to fault.
        /// </summary>
        public Exception Fault { get; protected set; }

        /// <summary>
        /// Gets a value indicating whether this machine is currently executing its activities.
        /// </summary>
        public ExecutableState ExecutableState
        {
            get { return _executableState; }
            protected set { _executableState = value; }
        }

        /// <summary>
        /// Gets or sets a value indicating the nature of this machine's completion.
        /// When toggling to complete, the Quitting event is raised.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        public CompletionCause CompletionCause
        {
            get { return _completionCause; }
            set
            {
                bool toggleFromPending = _completionCause == CompletionCause.Pending && value != CompletionCause.Pending;
                bool toggleToPending = value == CompletionCause.Pending && _completionCause != CompletionCause.Pending;
                // Only allow toggle between complete and pending.
                if (toggleFromPending || toggleToPending)
                {
                    _completionCause = value;
                    if (toggleFromPending)
                    {
                        LogService.Log(LogType, LogMessageType.Debug, Name,
                                       "Quitting with " + value + " completion cause.");
                        if (Quitting != null)
                        {
                            Quitting(this, new TimeStampedEventArgs());
                        }
                    }
                }
            }
        }

        public UmlNode CurrentNode
        {
            get { return _currentNode; }
            protected set
            {
                _currentNode = value;
                OnCurrentNodeChanged();
            }
        }

        public UmlNode InitialNode { get; protected set; }
        public UmlNode FinalNode { get; protected set; }
        protected List<IUmlTrigger> ExecuteTriggers { get; set; }

        /// <summary>
        /// Gets or optionally sets a dispatcher to use for all behaviors.
        /// </summary>
        protected SimpleDispatcher Dispatcher
        {
            get
            {
                if (_dispatcher == null)
                {
                    CreateLocalDispatcher();
                }
                return _dispatcher;
            }
            set { _dispatcher = value; }
        }

        private void CreateLocalDispatcher()
        {
            _dispatcher = new SimpleDispatcher(string.IsNullOrEmpty(ThreadName) ? Name : ThreadName);
            _hasLocalDispatcher = true;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        public void Quit(string reason = null)
        {
            if (!string.IsNullOrEmpty(reason))
            {
                LogService.Log(LogType, LogMessageType.Debug, Name, "Quit called: " + reason);
            }

            QuitInternal(ExecutableState == ExecutableState.NotStarted, null);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        public void EmergencyQuit(string reason = null)
        {
            if (!string.IsNullOrEmpty(reason))
            {
                LogService.Log(LogType, LogMessageType.Debug, Name, "EmergencyQuit called: " + reason);
            }

            QuitInternal(true, null);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        public virtual void Pause()
        {
            lock (_executionLock)
            {
                if (ExecutableState != ExecutableState.Running)
                {
                    return;
                }
                LogService.Log(LogType, LogMessageType.Debug, Name, "Pausing execution");
                ExecutableState = ExecutableState.Paused;

                // Disable transitions out of the current node while paused. This machine
                // cannot exit the node until the connectors are enabled again.
                CurrentNode.DisableConnectors();
                // Let overrides pass in their own args, but none needed here.
                OnPaused(null);
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        public virtual void Resume()
        {
            lock (_executionLock)
            {
                if (ExecutableState != ExecutableState.Paused)
                {
                    return;
                }
                LogService.Log(LogType, LogMessageType.Debug, Name, "Resuming execution");
                ExecutableState = ExecutableState.Running;
                // Make sure to notify before actually kicking off execution because subscribers will depend on knowing before 
                // any more activities execute.
                OnResumed();

                // Enable transitions out of the current node while paused.
                CurrentNode.EnableConnectors();
                // Optimistically try to transition.
                RunToCompletion();
            }
        }

        public void WaitUntilFinished()
        {
            bool wasSignaled = false;
            // Added a loop to make this method more reliable.  Now is doesn't hang on the wait
            // when the ManualResetEvent is disposed before Set notifies all.
            while (!_finishedEvent.SafeWaitHandle.IsClosed && !wasSignaled)
            {
                wasSignaled = _finishedEvent.WaitOne(1000);
            }
        }

        public bool Wait(int millisecondsTimeout)
        {
            if (_finishedEvent.SafeWaitHandle.IsClosed)
                return true;

            return _finishedEvent.WaitOne(millisecondsTimeout);
        }

        public void UseExecuteTrigger(IUmlTrigger trigger)
        {
            ExecuteTriggers.Add(trigger);
        }

        /// <summary>
        /// Make this ActivityMachine editable.
        /// </summary>
        public abstract void BeginEditing();

        /// <summary>
        /// Make this ActivityMachine un-editable.
        /// </summary>
        public abstract void StopEditing();

        public abstract IActivityMachine Copy();

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        protected void QuitInternal(bool doTeardown, Exception e)
        {
            _isEditable = false;
            lock (_executionLock)
            {
                CompletionCause = (e == null) ? CompletionCause.Interrupted : CompletionCause.Faulted;
                Fault = e;

                LogService.Log(LogType, LogMessageType.Debug, GetType().Name,
                   string.Format(CultureInfo.InvariantCulture, "{0}.QuitInternal with ExecutableState={1} and CompletionCause={2} and doTeardown={3}.", Name, ExecutableState, CompletionCause, doTeardown));

                // Only invoke this directly if Quit was called before machine officially started.  Otherwise, internally
                // trigger the machine to let it quit naturally via a finish transition to the final node.
                if (doTeardown)
                {
                    CompleteExecution("Internal Quit", e);
                }
                else if (ExecutableState == ExecutableState.Paused)
                {
                    Resume();
                }
                else
                {
                    RunToCompletion();
                }
            }
        }

        /// <summary>
        /// Stop the expiration timer and clear the dispatcher.
        /// The machine should only deactivate once, so call this from critical sections that can make such a promise.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        protected virtual void Deactivate()
        {
            LogService.Log(LogType, LogMessageType.Debug, GetType().Name, "'" + Name + "' is deactivating.");

            StopTiming();

            if (CurrentNode != FinalNode)
            {
                // Cancel any possible remaining executables this machine may have generated, but only
                // stop the work thread if the dispatcher is local to this machine.
                _dispatcher.CancelRemaining(_hasLocalDispatcher);
            }
        }

        /// <summary>
        /// This must be called in order for the machine to complete its execution.
        /// If the machine has not already finished and there was a completion cause specified, this will
        /// set the execution state to Finished, call DeactivateAndShutdown, and then raise the indicated event.
        /// </summary>
        /// <param name="explanation">an option explanation for the completion</param>
        /// <param name="causeOfFault">An optional exception for faulted completion</param>
        /// <returns>True if completion is done here, false if it was already done or no completion cause was indicated.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        protected virtual bool CompleteExecution(string explanation, Exception causeOfFault = null)
        {
            lock (_executionLock)
            {
                // Only handle completion once, and only when the cause is known (not pending).
                if (ExecutableState == ExecutableState.Finished || CompletionCause == CompletionCause.Pending)
                {
                    return false;
                }

                // Deactivate must happen before setting ExecutableState and before raising the event for the completion cause.  
                // This will remove all the remaining work from this machine's dispatcher and may stop the dispatcher thread.
                Deactivate();

                // This is the only place that the executable state is set to finished, regardless of the completion cause.
                ExecutableState = ExecutableState.Finished;
            }

            switch (CompletionCause)
            {
                case CompletionCause.Finished:
                    OnExecutableFinished();
                    break;
                case CompletionCause.Expired:
                    OnExecutableExpired();
                    break;
                case CompletionCause.Faulted:
                    OnExecutableFaulted(causeOfFault);
                    break;
                case CompletionCause.Interrupted:
                    OnMachineInterrupted();
                    break;
            }

            if (!string.IsNullOrEmpty(explanation))
            {
                LogService.Log(LogType, LogMessageType.Debug, GetType().Name,
                    string.Format(CultureInfo.InvariantCulture, "{0} reason for deactivating:  {1}", Name, explanation));
            }

            if (!_finishedEvent.SafeWaitHandle.IsClosed)
            {
                _finishedEvent.Set();
            }

            Teardown();

            return true;
        }

        /// <summary>
        /// Use a dispatcher to call this method.
        /// </summary>
        /// <param name="connector"></param>
        /// <param name="args">Optional event args related to the connector traversal</param>
        /// <returns>true if the connector was traversed.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        protected bool TraverseConnector(IUmlConnector connector, TransitionEventArgs args = null)
        {
            // If this machine is paused, don't allow transitions.
            lock (_executionLock)
            {
                if (ExecutableState == ExecutableState.Paused && CompletionCause == CompletionCause.Pending)
                {
                    return false;
                }
            }

            bool transitioned = false;
            lock (_connectorTraversalLock)
            {
                if (CurrentNode == connector.Supplier && connector.CanTraverse
                    && connector.Consumer != null && connector.Consumer.CanEnter
                    && connector.Supplier != null && connector.Supplier.TryExit())
                {
                    // Hook up to the connector's traversed event so that the CurrentNode value will be set before the node is entered.
                    // This allows a node's ENTER and DO behaviors to rely on the new current state.
                    connector.Traversed += HandleConnectorTraversed;

                    transitioned = connector.Traverse(args);
                    if (!transitioned)
                    {
                        LogService.Log(LogType, LogMessageType.Error, Name,
                            string.Format(CultureInfo.InvariantCulture, "Exited '{0}' but failed to transition into '{1}' node.  Machine is in a limbo state.", 
                                connector.Supplier.Name, connector.Consumer.Name));
                    }

                    // Traversal attempt is finished.  Whether it succeeded or failed, the event handling is no longer needed.
                    connector.Traversed -= HandleConnectorTraversed;
                }
            }
            if (transitioned)
            {
                // Try to keep this activation running.
                RunToCompletion();
            }
            return transitioned;
        }

        /// <summary>
        /// The method used to determine if a machine should transition to final node.
        /// It indicates quitting if the machine's CompletionCause is other than 'Pending',
        /// or if it does not have a continue transition (meaning it is the last node);
        /// </summary>
        /// <param name="machine">the machine to check</param>
        /// <returns>True if the machine is finished or if the current node only has a quit transition.</returns>
        protected static bool CheckMachineCanFinish(ActivityMachine machine)
        {
            if (machine == null)
                return false;

            lock (machine._executionLock)
            {
                if (machine.CompletionCause != CompletionCause.Pending)
                {
                    return true;
                }
                
                var continueTransition = FindContinueTransition(machine.CurrentNode);
                // If somehow there is no continue transition, then the only way to finish is this nodes finish transition.
                return continueTransition == null;
            }
        }

        /// <summary>
        /// The expression used to determine if a machine should should continue.
        /// Only continue if the machine is running and is not finished.
        /// </summary>
        protected static bool CheckMachineCanNotFinish(ActivityMachine machine)
        {
            if (machine == null)
                return false;

            lock (machine._executionLock)
            {
                return machine.CompletionCause == CompletionCause.Pending;
            }
        }

        /// <summary>
        /// Find this node's finish transition.
        /// Encapsulates a UML spec violation where transition/trigger order matters.
        /// </summary>
        /// <returns></returns>
        protected static UmlTransition FindFinishTransition(UmlNode node)
        {
            if (node != null && node.Connectors.Count > 0)
            {
                // The Finishe transition is always index 0 in ActivityMachine.
                return node.Connectors[(int)ActivityTransition.Finish] as UmlTransition;
            }
            return null;
        }

        /// <summary>
        /// Find this node's quit transition, if it has one. Only ConditionalNodes have these.
        /// Encapsulates a UML spec violation where transition/trigger order matters.
        /// </summary>
        /// <returns></returns>
        protected static UmlTransition FindQuitTransition(UmlNode node)
        {
            // Only ConditionalNode will have a quit transition.
            if (node is ConditionalNode)
            {
                return (node as ConditionalNode).QuitTransition;
            }
            return null;
        }

        /// <summary>
        /// Find this node's continue transition.
        /// Encapsulates a UML spec violation where transition/trigger order matters.
        /// </summary>
        /// <returns></returns>
        protected static UmlTransition FindContinueTransition(UmlNode node)
        {
            if (node is ConditionalNode)
            {
                return (node as ConditionalNode).ContinueTransition;
            }
            // Only ConditionalNodes have quit transitions, so if node is not conditional then
            // it's continue transition must be the 2nd one.
            if (node != null && node.Connectors.Count > 1)
            {
                return node.Connectors[(int)ActivityTransition.Continue] as UmlTransition;
            }
            return null;
        }

        protected List<IUmlConnector> FindAllConnectorsTo(IUmlNode targetNode)
        {
            var quitConnectors = new List<IUmlConnector>();
            FindAllConnectorsTo(targetNode, InitialNode, quitConnectors, new List<IUmlConnector>());
            return quitConnectors;
        }

        protected List<IUmlConnector> FindAllConnectors()
        {
            var allConnectors = new List<IUmlConnector>();
            FindAllConnectors(InitialNode, allConnectors);
            return allConnectors;
        }

        protected virtual void ActivateExecutionTriggers()
        {
            //TODO: add copy of trigger to ?
        }

        protected virtual void DeactivateExecutionTriggers()
        {
            //TODO:
        }

        /// <summary>
        /// Executes the given executable using a Dispatcher.
        /// This is NOT a duplicate implementation of BehavioralNode.ExecuteExecutable, 
        /// it acts slightly differently.
        /// </summary>
        /// <param name="executable">IExecutable</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        protected void ExecuteExecutable(IExecutable executable)
        {
            if (!Dispatcher.IsDisposed)
            {
                Dispatcher.Run(executable);
            }
            else
            {
                LogService.Log(LogType.System, LogMessageType.Debug, GetType().Name,
                    string.Format(CultureInfo.InvariantCulture, "Cannot execute '{0}'. Dispatcher for ActivityMachine '{1}' was already disposed.", executable.Name, Name));
            }
        }

        /// <summary>
        /// Use a dispatcher to call this.
        /// </summary>
        protected void EnterInitialNode()
        {
            if (!_isAssembled)
            {
                return;
            }
            InitialNode.EnterFrom(this);
            CurrentNode = InitialNode;
            // Try to keep this activation running.
            RunToCompletion();
        }

        /// <summary>
        /// Run to completion ensures that the ActivityMachine is in a stable state before it gives
        /// up control to external events.  This implementation differs from the semantics of a 
        /// state machine in that a run to completion may include traversal over many sequential connectors, 
        /// and running through any number of actions.  
        /// A connector is considered acceptable even if it does have triggers.  This means that
        /// the guard condition must be designed to handle conditions present during this call.
        /// </summary>
        protected abstract void RunToCompletion();

        /// <summary>
        /// Recursively finds all connectors to the target node from the current node.
        /// </summary>
        /// <param name="targetNode">connector consumer</param>
        /// <param name="currentNode">connector supplier</param>
        /// <param name="found">list of found connectors</param>
        /// <param name="covered">collection of connectors that were already covered in this recursive search.</param>
        private void FindAllConnectorsTo(IUmlNode targetNode, IUmlNode currentNode, List<IUmlConnector> found, List<IUmlConnector> covered)
        {
            var current = currentNode as UmlNode;
            if (current != null)
            {
                foreach (var connector in current.Connectors)
                {
                    if (connector != null && !covered.Contains(connector))
                    {
                        covered.Add(connector);
                        if (connector.Consumer == targetNode)
                        {
                            found.Add(connector);
                        }
                        else
                        {
                            FindAllConnectorsTo(targetNode, connector.Consumer, found, covered);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Recursively finds all connectors to the next node from the current node.
        /// </summary>
        /// <param name="currentNode">starting node</param>
        /// <param name="found">list of found connectors</param>
        private void FindAllConnectors(IUmlNode currentNode, List<IUmlConnector> found)
        {
            var current = currentNode as UmlNode;
            if (current != null)
            {
                foreach (var connector in current.Connectors)
                {
                    if (connector != null && !found.Contains(connector))
                    {
                        found.Add(connector);
                        FindAllConnectors(connector.Consumer, found);
                    }
                }
            }
        }

        /// <summary>
        /// Teardown is not meant to be a public dispose method.  ActivityMachine and it's derived
        /// classes are meant to clean-up after themselves.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        protected void Teardown()
        {
            // Only dispose the dispatcher if it was created here.
            if (_hasLocalDispatcher)
            {
                // We are here because the FinalNode's ExitBehavior is running on the Dispatcher.
                // Attempting to dispose of the Dispatcher from the final executable using the Dispatcher's
                // own thread will cause the Dispatcher to kill (abort) it's thread.
                // That thread abort can be avoided by giving the Dispatcher cleanup to a different thread,
                // which allows the currently executing ExitBehavior to finish cleanly first.
                Task.Factory.StartNew(() =>
                {
                    LogService.Log(LogType, LogMessageType.Debug, GetType().Name, "Waiting for local dispatcher '" + Name + "' to be empty.");
                    _dispatcher.WaitUntilDone();
                    LogService.Log(LogType, LogMessageType.Debug, GetType().Name, "'" + Name + "' is disposing.");

                    // We are now completely done with the dispatcher.
                    _dispatcher.Dispose();
                    _dispatcher = null;
                });
            }

            try
            {
                _finishedEvent.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // reset event was disposed
            }

            //Dispose the UML nodes
            DisposeNodes(InitialNode);
            if (FinalNode != null)
            {
                // THIS MAY BE A PROBLEM if SimpleDispatcher is made to dispose IExecutables after executing them.
                FinalNode.Dispose();
            }

            Dispose();
        }

        /// <summary>
        /// Disposes evertying but the final node
        /// </summary>
        /// <param name="currentNode"></param>
        private void DisposeNodes(IDisposable currentNode)
        {
            if (currentNode == null)
                return;

            if (currentNode == FinalNode)
                return;

            var current = currentNode as UmlNode;
            if (current != null)
            {
                foreach (var connector in current.Connectors)
                {
                    DisposeNodes(connector.Consumer);
                }
                currentNode.Dispose();
            }
        }

        #region Raising Events
        /// <summary>
        /// Raise the Interrupted event when the machine was externally interrupted.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        protected void OnMachineInterrupted()
        {
            if (Interrupted != null)
            {
                try
                {
                    LogService.Log(LogType, LogMessageType.Debug, GetType().Name, Name + " interrupted.");
                    Interrupted(this, new TimeStampedEventArgs());
                }
                catch (Exception e)
                {
                    LogService.Log(LogType, LogMessageType.Error, GetType().Name,
                                    e.GetType().Name + " while raising Interrupted event. (" + Name + "): " + e.Message, e);
                }
            }
        }

        /// <summary>
        /// Raise the Paused event when the machine is told to pause execution.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        protected virtual void OnPaused(PausedEventArgs args)
        {
            if (Paused != null)
            {
                try
                {
                    Paused(this, args);
                }
                catch (Exception e)
                {
                    LogService.Log(LogType, LogMessageType.Error, GetType().Name,
                                    e.GetType().Name + " while raising Paused event. (" + Name + "): " + e.Message, e);
                }
            }
        }

        /// <summary>
        /// Raise the ExecutionResumed event when the machine is told to resume execution.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        protected void OnResumed()
        {
            if (Resumed != null)
            {
                try
                {
                    Resumed(this, null);
                }
                catch (Exception e)
                {
                    LogService.Log(LogType, LogMessageType.Error, GetType().Name,
                                    e.GetType().Name + " while raising ExecutionResumed event. (" + Name + "): " + e.Message, e);
                }
            }
        }

        /// <summary>
        /// Raise the ExecuteRequested event.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        protected void OnExecuteRequested(object sender, EventArgs args)
        {
            // If a direct execute call already happened, then don't let this run again.
            if (ExecutableState == ExecutableState.NotStarted)
            {
                if (_executeRequested != null)
                {
                    try
                    {
                        LogService.Log(LogType, LogMessageType.Debug, GetType().Name, Name + " execute requested.");
                        _executeRequested(this, null);
                    }
                    catch (Exception e)
                    {
                        LogService.Log(LogType, LogMessageType.Error, GetType().Name,
                                        e.GetType().Name + " while raising ExecuteRequested event. (" + Name + "): " + e.Message, e);
                    }
                }
            }
        }

        private void OnCurrentNodeChanged()
        {
            if (CurrentNodeChanged != null)
            {
                try
                {
                    CurrentNodeChanged(this, new ActivityMachineEventArgs() { ExecutableState = ExecutableState });
                }
                catch (Exception e)
                {
                    LogService.Log(LogType, LogMessageType.Error, GetType().Name,
                                    e.GetType().Name + " while raising CurrentNodeChanged event. (" + Name + "): " + e.Message, e);
                }
            }
        }
        #endregion

        #region Handling Events

        protected void HandleNodeExited(object sender, EventArgs args)
        {
            //TODO: anything?
        }

        protected virtual void HandleNodeEntered(object sender, EventArgs args)
        {
            //TODO: anything?
        }

        protected void HandleNodeFaulted(object sender, FaultedEventArgs args)
        {
            if (HaltOnFault)
            {
                // Don't raise the event here, just set the completion cause.
                lock (_executionLock)
                {
                    Fault = args.Cause;
                    CompletionCause = CompletionCause.Faulted;
                }
            }
        }

        protected void HandleNodeTimedOut(object sender, EventArgs args)
        {
            // Don't raise the event here, just set the completion cause.
            lock (_executionLock)
            {
                CompletionCause = CompletionCause.Expired;
            }
        }

        protected override void HandleExpirationTimerElapsed(object sender, ElapsedEventArgs e)
        {
            StopTiming();

            // Don't raise the event here, just set the completion cause.
            lock (_executionLock)
            {
                CompletionCause = CompletionCause.Expired;
            }
        }

        /// <summary>
        /// Handler for a connector Traversed event.  This allows the CurrentNode property to be set before
        /// the new current node's ENTER and DO behaviors occur.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="eventArgs"></param>
        private void HandleConnectorTraversed(object sender, EventArgs eventArgs)
        {
            var connector = sender as IUmlConnector;
            // Set the new current state to the node that the connector entered.
            CurrentNode = connector.Consumer as StateNode;
        }
        #endregion
    }
}
