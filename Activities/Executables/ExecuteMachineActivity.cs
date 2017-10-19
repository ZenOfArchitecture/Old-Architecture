using System;
using System.Threading;
using Ventana.Core.Activities.Builders;
using Ventana.Core.Activities.Machines;
using Ventana.Core.Activities.Parts;
using Ventana.Core.Base;
using Ventana.Core.Base.Activities;
using Ventana.Core.Base.Configuration;
using Ventana.Core.Base.Utilities;
using Ventana.Core.Utilities;
using Ventana.Core.Utilities.ExtensionMethods;

namespace Ventana.Core.Activities.Executables
{
    public abstract class ExecuteMachineActivity<TPreconditionParam> : DynamicActivity, ISubmachineBehavior where TPreconditionParam : class
    {
        protected readonly AutoResetEvent ExecutionWaitEvent = new AutoResetEvent(false);
        private IActivityMachine _machine;

        /// <summary>
        /// Ctor using the default CreateMachine delegate.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="preconditionParam"></param>
        /// <param name="precondition"></param>
        /// <param name="machineConfig"></param>
        protected ExecuteMachineActivity(string name, TPreconditionParam preconditionParam, 
                                         Func<TPreconditionParam, bool> precondition, DynamicConfiguration machineConfig)
            : this(name, preconditionParam, precondition, machineConfig, ActivityMachineFactory.Create)
        {            
        }

        protected ExecuteMachineActivity(string name, TPreconditionParam preconditionParam,
                                         Func<TPreconditionParam, bool> precondition, DynamicConfiguration machineConfig,
                                         Func<DynamicConfiguration, IActivityMachine> create) : base(name)
        {
            PreconditionParam = preconditionParam;
            ExecutePrecondition = precondition;
            MachineConfig = machineConfig;
            CreateMachine = create;
            var root = ExtractRootMachine(machineConfig);
            RootName = root == null ? "unknown" : root.Name;
        }

        /// <summary>
        /// Raised when all the work of the submachine(s) of this host is finished.
        /// </summary>
        public event EventHandler<SubmachineEventArgs> SubmachineDone;

        /// <summary>
        /// Raised when a submachine of this host is paused.
        /// </summary>
        public event EventHandler<SubmachineEventArgs> SubmachinePaused;

        /// <summary>
        /// Raised when a submachine of this host is resumed.
        /// </summary>
        public event EventHandler<SubmachineEventArgs> SubmachineResumed;

        /// <summary>
        /// Raised when a submachine is created in this host.
        /// </summary>
        public event EventHandler<SubmachineEventArgs> SubmachineCreated;
        
        /// <summary>
        /// 
        /// </summary>
        public event EventHandler<PausedEventArgs> SubmachinePausableNodeEntered;

        protected Func<DynamicConfiguration, IActivityMachine> CreateMachine { get; set; }
        public IConfiguration MachineConfig { get; private set; }
        protected string RootName { get; set; }
        public bool IsFinished { get; protected set; }
        public bool IsFaulted { get; protected set; }
        public bool IsExpired { get; protected set; }
        public bool IsInterrupted { get; protected set; }
        public bool IsPaused { get; protected set; }

        /// <summary>
        /// Sets an ActivityMachine instance and handles event subscriptions.
        /// </summary>
        public IActivityMachine Machine
        {
            get { return _machine; }
            internal set
            {
                if (value != _machine)
                {
                    SubscribeToMachine(_machine, false);
                    _machine = value;
                    SubscribeToMachine(_machine, true);
                }
            }
        }

        /// <summary>
        /// Gets the condition to be checked before the machine can execute.
        /// </summary>
        protected Func<TPreconditionParam, bool> ExecutePrecondition { get; set; }

        public bool CanExecute { get; protected set; }

        /// <summary>
        /// Gets or sets the target object for the parameter of the condition that guards execution of the submachine. 
        /// </summary>
        protected TPreconditionParam PreconditionParam { get; set; }

        public static ActivityMachine ExtractRootMachine(DynamicConfiguration configuration)
        {
            ActivityMachine rootMachine = null;
            if (configuration.HasDataKey(Key.RootMachine))
            {
                rootMachine = configuration.Data.RootMachine as ActivityMachine;
            }
            else if (configuration.HasDataContextKey(Key.RootMachine))
            {
                rootMachine = configuration.Data.DataContext.RootMachine as ActivityMachine;
            }
            return rootMachine;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (ExecutionWaitEvent != null)
                {
                    try
                    {
                        ExecutionWaitEvent.Set();
                        ExecutionWaitEvent.Dispose();
                    }
                    catch (ObjectDisposedException)
                    {
                        // ignore. WaitHandle may have been disposed
                    }
                }

                // unsubscribe from machine
                SubscribeToMachine(Machine, false);

                if (Machine is IDisposable)
                {
                    (Machine as IDisposable).Dispose();
                }
                Machine = null;
                MachineConfig = null;

                CanExecute = false;
            }

            // Free any unmanaged objects here. 
            //
            base.Dispose(disposing);
        }

        protected bool IsQuitting()
        {
            var rootMachine = ExtractRootMachine(MachineConfig as DynamicConfiguration);
            return (rootMachine != null && rootMachine.CompletionCause != CompletionCause.Pending);
        }

        protected void WaitForExecutionSignal()
        {
            bool wasSignaled = false;
            while (!IsQuitting() && !wasSignaled)
            {
                wasSignaled = ExecutionWaitEvent.WaitOne(TimeSpan.FromSeconds(5));
            }
        }

        protected abstract void ExecuteMachine();

        protected override void RunAction()
        {
            CanExecute = ExecutePrecondition(PreconditionParam);

            if (CanExecute)
            {
                // This executes one or more new ActivityMachines.
                ExecuteMachine();
            }
            else
            {
                // If the machine shouldn't execute, clean up and signal with Done event.
                HandleMachineFinished(this, null);
            }
        }

        /// <summary>
        /// Inherited from IExecutable
        /// </summary>
        public override void Execute()
        {
            try
            {
                OnExecutableStarted();
                StartTiming();
                RunAction();
                // Let a callback handle completion because this activity may not be
                // done with the execution of one submachine.
            }
            catch (Exception e)
            {
                StopTiming();
                OnExecutableFaulted(e);
            }
        }

        protected void SubscribeToMachine(IActivityMachine machine, bool subscribe)
        {
            if (machine != null)
            {
                var activityMachine = machine as ActivityMachine;
                var commandMachine = machine as CommandExecutingMachine;
                if (subscribe)
                {
                    machine.Finished += HandleMachineFinished;
                    machine.Expired += HandleMachineExpired;
                    machine.Interrupted += HandleMachineInterrupted;
                    machine.Faulted += HandleMachineFaulted;
                    if (activityMachine != null)
                    {
                        activityMachine.Paused += HandleMachinePaused;
                        activityMachine.Resumed += HandleMachineResumed;
                    }
                    if (commandMachine != null)
                    {
                        commandMachine.PausableNodeEntered += HandlePausableNodeEntered;
                    }
                }
                else
                {
                    machine.Finished -= HandleMachineFinished;
                    machine.Expired -= HandleMachineExpired;
                    machine.Interrupted -= HandleMachineInterrupted;
                    machine.Faulted -= HandleMachineFaulted;
                    if (activityMachine != null)
                    {
                        activityMachine.Paused -= HandleMachinePaused;
                        activityMachine.Resumed -= HandleMachineResumed;
                    }
                    if (commandMachine != null)
                    {
                        commandMachine.PausableNodeEntered -= HandlePausableNodeEntered;
                    }
                }
            }
        }

        private void HandlePausableNodeEntered(object sender, PausedEventArgs args)
        {
            OnSubmachinePausableNodeEntered(args);
        }

        protected virtual void HandleMachinePaused(object sender, PausedEventArgs args)
        {
            IsPaused = true;
            StopTiming();

            OnSubmachinePaused(args);
        }

        protected virtual void HandleMachineResumed(object sender, EventArgs args)
        {
            IsPaused = false;
            StartTiming();

            OnSubmachineResumed();
        }

        protected virtual void HandleMachineInterrupted(object sender, EventArgs args)
        {
            // unsubscribe from machine
            SubscribeToMachine(Machine, false);

            IsInterrupted = true;
            StopTiming();
            OnExecutableFaulted(new ActivityMachineException("Nested machine was interrupted."));

            OnSubmachineDone();

            Dispose();
        }

        protected virtual void HandleMachineExpired(object sender, EventArgs args)
        {
            // unsubscribe from machine
            SubscribeToMachine(Machine, false);
            
            IsExpired = true;
            StopTiming();
            OnExecutableExpired();

            OnSubmachineDone();

            Dispose();
        }

        protected virtual void HandleMachineFaulted(object sender, FaultedEventArgs args)
        {
            // unsubscribe from machine
            SubscribeToMachine(Machine, false);
            
            IsFaulted = true;
            StopTiming();
            OnExecutableFaulted(args.Cause);

            OnSubmachineDone();

            Dispose();
        }

        protected virtual void HandleMachineFinished(object sender, EventArgs args)
        {
            // unsubscribe from machine
            SubscribeToMachine(Machine, false);

            IsFinished = true;
            StopTiming();
            OnExecutableFinished();

            OnSubmachineDone();

            Dispose();
        }

        private void OnSubmachineDone()
        {
            if (SubmachineDone != null)
            {
                SubmachineDone(this, new SubmachineEventArgs() { Machine = Machine });
            }
        }

        private void OnSubmachinePaused(PausedEventArgs args)
        {
            if (SubmachinePaused != null)
            {
                SubmachinePaused(this, new SubmachineEventArgs() { Machine = Machine });
            }
        }

        private void OnSubmachineResumed()
        {
            if (SubmachineResumed != null)
            {
                SubmachineResumed(this, new SubmachineEventArgs() { Machine = Machine });
            }
        }

        private void OnSubmachinePausableNodeEntered(PausedEventArgs args)
        {
            if (SubmachinePausableNodeEntered != null)
            {
                SubmachinePausableNodeEntered(this, args);
            }
        }

        protected void OnSubmachineCreated()
        {
            if (SubmachineCreated != null)
            {
                SubmachineCreated(this, new SubmachineEventArgs() { Machine = Machine });
            }
        }
    }
}
