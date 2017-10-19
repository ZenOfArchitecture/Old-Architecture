using System.Collections.Concurrent;
using System.Globalization;
using SimpleMvvmToolkit;
using System;
using System.Collections.Generic;
using System.Linq;
using Ventana.Core.Activities.Builders;
using Ventana.Core.Base.Activities;
using Ventana.Core.Base.Configuration;
using Ventana.Core.Logging;
using Ventana.Core.Utilities;

namespace Ventana.Core.Activities
{
    /// <summary>
    /// Processes IExecutables
    /// </summary>
    public class ActivityEngine : ModelBase<ActivityEngine>, IExecutableEngine
    {
        private readonly object _runLock = new object();
        private readonly object _updateLock = new object();

        /// <summary>
        /// A dictionary of added ActivityMachines that have not completed yet.
        /// </summary>
        private readonly ConcurrentDictionary<Guid, IExecutable> _liveExecutables = new ConcurrentDictionary<Guid, IExecutable>();

        private readonly List<IActivityMachine> _delayedMachines = new List<IActivityMachine>();

        private SimpleDispatcher _dispatcher;
        private int _totalMachines;

        ~ActivityEngine()
        {
            if (_dispatcher != null)
            {
                _dispatcher.Dispose();
            }
        }

        public event EventHandler<ExecutedEventArgs> ExecutableExecuting;

        /// <summary>
        /// Gets a value indicating whether this ActivityEngine is running or not.
        /// </summary>
        public bool IsRunning { get; private set; }

        public int TotalActivityMachinesCount
        {
            get { return _totalMachines; }

            set
            {
                if (_totalMachines == value) return;
                _totalMachines = value;
                NotifyPropertyChanged(s => s.TotalActivityMachinesCount);
            }
        }

        /// <summary>
        /// Gets the total count of TExecutables, which includes those that are running and delayed.
        /// </summary>
        /// <returns>count of all IExecutables (running and delayed) of type TExecutable</returns>
        /// <typeparam name="TExecutable"></typeparam>
        public int GetTotalCountOfType<TExecutable>()
        {
            lock (_updateLock)
            {
                return (_delayedMachines.Count(m => m is TExecutable) + _liveExecutables.Count(e => e is TExecutable));
            }
        }

        /// <summary>
        /// Gets a value indicating whether there are any IActivityMachines of the specified type
        /// waiting to execute.
        /// </summary>
        /// <typeparam name="TMachine">the type of IActivityMachine</typeparam>
        public bool HasDelayedMachinesOfType<TMachine>()
        {
            lock (_updateLock)
            {
                return _delayedMachines.Any(m => m is TMachine);
            }
        }

        /// <summary>
        /// Stops all running IActivityMachines after each one completes its current activity.
        /// This does not yet stop IExecutables of a different variety.
        /// </summary>
        public virtual void Quit()
        {
            QuitAllOfType<IActivityMachine>(false);
        }

        public void QuitAllOfType<TQuit>() where TQuit : IActivityMachine
        {
            QuitAllOfType<TQuit>(false);
        }

        /// <summary>
        /// Stops all running IActivityMachines immediately, if possible.
        /// </summary>
        public virtual void EmergencyQuit()
        {
            QuitAllOfType<IActivityMachine>(true);
        }

        /// <summary>
        /// adds an activity machine to its machines list and listens for 
        /// notification from the machine that it is ready to execute
        /// </summary>
        /// <param name="activityMachine">machine to add</param>
        public void Add(IActivityMachine activityMachine)
        {
            if (activityMachine == null) return;
            lock (_updateLock)
            {
                _delayedMachines.Add(activityMachine);
                GetTotalMachineCount();
                activityMachine.ExecuteRequested += HandleExecuteRequested;
            }
        }

        /// <summary>
        /// adds a new IExecutable to the work list.
        /// </summary>
        /// <param name="executable"></param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        public virtual void Execute(IExecutable executable)
        {
            if (executable == null || !IsRunning)
            {
                return;
            }

            // Don't assume the caller gave an Id yet.
            if (executable.Id == Guid.Empty)
            {
                executable.Id = Guid.NewGuid();
            }
            // Listen for the executable's execution & completion in order to manage a list of running executables.
            executable.Started += HandleExecutableStarted;
            executable.Finished += HandleExecutableFinished;
            executable.Faulted += HandleExecutableFaulted;
            executable.Expired += HandleExecutableFinished;
            if (executable is IActivityMachine)
            {
                (executable as IActivityMachine).Interrupted += HandleExecutableFinished;
            }

            LogService.Log(LogType.System, LogMessageType.Trace, GetType().Name,
                string.Format(CultureInfo.InvariantCulture, "Putting IExecutable '{0}' on the dispatcher for execution.", executable.Name));
            if (ExecutableExecuting != null)
            {
                ExecutableExecuting(this, new ExecutedEventArgs() { Executed = executable });
            }

            //run the executable
            _dispatcher.Run(executable);
        }

        /// <summary>
        /// builds a new activity machine from the configuration passed in
        /// and executes it immediately.
        /// </summary>
        /// <param name="configuration">used to build the machine</param>
        /// <param name="isSynchronous">TBD</param>
        /// <returns></returns>
        public virtual IActivityMachine ExecuteActivityMachine(IConfiguration configuration, bool isSynchronous)
        {
            var activityMachine = CreateMachine(configuration);
            Execute(activityMachine);

            return activityMachine;
        }

        /// <summary>
        /// builds a new activity machine from the configuration passed in 
        /// and adds it to the DelayedActivityMachine list to be executed 
        /// when the machine requests it through event notification.
        /// </summary>
        /// <param name="configuration">used to build the machine</param>
        /// <param name="isSynchronous">TBD</param>
        /// <returns>activity machine that was built</returns>
        public virtual IActivityMachine AddActivityMachine(IConfiguration configuration, bool isSynchronous)
        {
            var activityMachine = CreateMachine(configuration);
            Add(activityMachine);
            return activityMachine;
        }

        /// <summary>
        /// Create a new SimpleDispatcher and start running executables.
        /// </summary>
        public virtual void Start()
        {
            lock (_runLock)
            {
                if (!IsRunning)
                {
                    _dispatcher = new SimpleDispatcher(GetType().Name);
                    IsRunning = true;
                }
            }
        }

        /// <summary>
        /// Dispose an exising SimpleDispatcher and stop running executables.
        /// </summary>
        public virtual void Stop()
        {
            lock (_runLock)
            {
                if (!IsRunning)
                    return;

                _dispatcher.Dispose();
                IsRunning = false;

                lock (_updateLock)
                {
                    while (_liveExecutables.Any())
                    {
                        var executable = _liveExecutables.First().Value;
                        RemoveExecutableFromRunningList(executable);

                        if (executable is IActivityMachine)
                        {
                            (executable as IActivityMachine).EmergencyQuit();
                        }
                    }
                    while (_delayedMachines.Any())
                    {
                        var machine = _delayedMachines.First();
                        RemoveMachineFromDelayedList(machine);
                        machine.Quit();
                    }
                }
            }
        }

        public virtual bool IsExecutableRunning(Guid executableId)
        {
            lock (_updateLock)
            {
                return _liveExecutables.ContainsKey(executableId);
            }
        }

        /// <summary>
        /// Stops all running and queued IActivityMachines of QType.
        /// They can be stopped immediately or after the current activity is done.
        /// </summary>
        /// <typeparam name="TQuit">The type of IActivityMachine to quit.</typeparam>
        /// <param name="immediately">indicate whether to quit all immediately, or when convenient.</param>
        private void QuitAllOfType<TQuit>(bool immediately) where TQuit : IActivityMachine
        {
            lock (_updateLock)
            {
                // Quiting a machine can cause the same thread to enter ADD/REMOVE and modify the local collections here.
                // Use ToList() on the enumerator to avoid that.
                var machines = _liveExecutables.Values.OfType<TQuit>().ToList();
                // Quit machines that are currently running.
                foreach (var item in machines)
                {
                    if (immediately)
                    {
                        item.EmergencyQuit();
                    }
                    else
                    {
                        item.Quit();
                    }
                }
                // Quiting a machine can cause the same thread to enter ADD/REMOVE and modify the local collections here.
                // Use ToList() on the enumerator to avoid that.
                var delayedMachines = _delayedMachines.OfType<TQuit>().ToList();
                // Quit machines that are waiting to be run.
                foreach (var item in delayedMachines)
                {
                    if (immediately)
                    {
                        item.EmergencyQuit();
                    }
                    else
                    {
                        item.Quit();
                    }
                }
            }
        }

        private IActivityMachine CreateMachine(IConfiguration configuration)
        {
            if (!IsRunning)
            {
                return null;
            }

            Guid machineId = Guid.NewGuid();

            IActivityMachine activityMachine = ActivityMachineFactory.Create(configuration as DynamicConfiguration);
            if (activityMachine != null)
            {
                activityMachine.Id = machineId;
            }

            return activityMachine;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        protected virtual void HandleExecutableFaulted(object sender, FaultedEventArgs args)
        {
            var exe = sender as IExecutable;
            if (exe == null) return;
            LogService.Log(LogType.System, LogMessageType.Error, GetType().Name,
                string.Format(CultureInfo.InvariantCulture, "'{0}' quit with {1}: {2}", exe.Name, 
                    args.Cause == null ? "unknown exception." : args.Cause.GetType().Name, 
                    args.Cause == null ? string.Empty : args.Cause.Message), args.Cause);
            CleanupAfterExecutable(exe);
        }

        private void HandleExecutableStarted(object sender, EventArgs e)
        {
            var executable = sender as IExecutable;

            lock (_updateLock)
            {
                AddExecutableToRunningList(executable);
                RemoveMachineFromDelayedList(executable as IActivityMachine);
                GetTotalMachineCount();
            }
        }

        private void HandleExecutableFinished(object sender, EventArgs e)
        {
            CleanupAfterExecutable(sender as IExecutable);
        }

        /// <summary>
        /// calls execute on the activity machine that has indicated its conditions have been satisfied
        /// </summary>
        /// <param name="sender">activity machine to execute</param>
        /// <param name="args">event arguments</param>
        private void HandleExecuteRequested(object sender, EventArgs args)
        {
            var machine = sender as IActivityMachine;
            if (machine == null) return;

            // Don't move the machine off the delayed list until its Started event fires.
            Execute(machine);
        }

        private void CleanupAfterExecutable(IExecutable executable)
        {
            lock (_updateLock)
            {
                // Just in case, try to remove from delayed list.
                RemoveMachineFromDelayedList(executable as IActivityMachine);
                // definitely remove from running list.
                RemoveExecutableFromRunningList(executable);

                if (executable is IDisposable)
                {
                    (executable as IDisposable).Dispose();
                }

                GetTotalMachineCount();
            }
        }

        /// <summary>
        /// Caller should lock using _updateLock first.
        /// </summary>
        /// <param name="machine">IActivityMachine to remove from delayed list.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        private void RemoveMachineFromDelayedList(IActivityMachine machine)
        {
            if (!_delayedMachines.Contains(machine)) return;

            LogService.Log(LogType.System, LogMessageType.Debug, GetType().Name,
                string.Format(CultureInfo.InvariantCulture, "Removing IActivityMachine '{0}' from the delayed IExecutable list.", machine.Name));
            machine.ExecuteRequested -= HandleExecuteRequested;

            _delayedMachines.Remove(machine);
        }

        /// <summary>
        /// Caller should lock using _updateLock first.
        /// </summary>
        /// <param name="executable">IExecutable to add to running list.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        private void AddExecutableToRunningList(IExecutable executable)
        {
            if (executable == null || _liveExecutables.ContainsKey(executable.Id)) return;

            LogService.Log(LogType.System, LogMessageType.Debug, GetType().Name,
                string.Format(CultureInfo.InvariantCulture, "Adding IExecutable '{0}' to the running list.", executable.Name));
            // Stop listening to the machine's started event.
            executable.Started -= HandleExecutableStarted;

            // Hold a reference until the machine is completed.
            _liveExecutables.TryAdd(executable.Id, executable);
        }

        /// <summary>
        /// Caller should lock using _updateLock first.
        /// </summary>
        /// <param name="executable">IExecutable to remove from running list.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        private void RemoveExecutableFromRunningList(IExecutable executable)
        {
            if (executable == null || !_liveExecutables.ContainsKey(executable.Id)) return;

            LogService.Log(LogType.System, LogMessageType.Debug, GetType().Name,
                string.Format(CultureInfo.InvariantCulture, "Removing IExecutable '{0}' from the running list.", executable.Name));
            // Stop listening to the finished machine.
            executable.Finished -= HandleExecutableFinished;
            executable.Expired -= HandleExecutableFinished;
            executable.Faulted -= HandleExecutableFaulted;
            // No need to unhook started event because it already happened in HandleExecutableStarted.
            if (executable is IActivityMachine)
            {
                (executable as IActivityMachine).Interrupted -= HandleExecutableFinished;
            }

            IExecutable removed = null;
            _liveExecutables.TryRemove(executable.Id, out removed);
        }

        /// <summary>
        /// Caller should lock using _updateLock first.
        /// </summary>
        private void GetTotalMachineCount()
        {
            TotalActivityMachinesCount = (_delayedMachines.Count + _liveExecutables.Count);
        }
    }
}
