using System;
using System.Globalization;
using Ventana.Core.Base.Activities;
using Ventana.Core.Base.Configuration;
using Ventana.Core.Logging;
using Ventana.Core.Utilities;

namespace Ventana.Core.Activities.Executables
{
    /// <summary>
    /// Implements a While Loop using submachines to realize scope and execution of that scope.
    /// Example preconditions:
    /// (device) => (device.DigitalInputs[3] == DigitalState.On)
    /// (context) => (context.Variable1 is float && ((float)context.Variable1) == 3.4)
    /// </summary>
    /// <typeparam name="TPreconditionParam">This is either an IDataComposer (for I/O access) or a dynamic DataContext (for variable access)</typeparam>
    public class DoWhileConditionActivity<TPreconditionParam> : ExecuteMachineActivity<TPreconditionParam> where TPreconditionParam : class
    {
        private int _iteration = 0;

        /// <summary>
        /// Ctor using the default CreateMachine delegate.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="preconditionParam"></param>
        /// <param name="precondition"></param>
        /// <param name="machineConfig"></param>
        public DoWhileConditionActivity(string name, TPreconditionParam preconditionParam, Func<TPreconditionParam, bool> precondition, DynamicConfiguration machineConfig)
            : base(name, preconditionParam, precondition, machineConfig)
        {
        }

        public DoWhileConditionActivity(string name, TPreconditionParam preconditionParam, Func<TPreconditionParam, bool> precondition, DynamicConfiguration machineConfig, Func<DynamicConfiguration, IActivityMachine> create)
            : base(name, preconditionParam, precondition, machineConfig, create)
        {
        }

        /// <summary>
        /// Execute the first iteration here.  Further iteration is done with a callback.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        protected override void ExecuteMachine()
        {
            if (CreateMachine == null) 
                throw new ArgumentNullException(string.Format(CultureInfo.InvariantCulture, "CreateMachine function is not defined for {0}", Name));
  
            if (IsQuitting())
            {
                LogService.Log(LogType, LogMessageType.Debug, GetType().Name,
                    string.Format(CultureInfo.InvariantCulture, "'{0}' received quitting notification from {1} root ActivityMachine.", Name, RootName));
                base.HandleMachineFinished(this, null);
                return;
            }

            MachineConfig.Data.Name = Name + "-Iteration" + _iteration;
            // This new machine will inherit any dispatcher that it finds in the given MachineConfig.
            Machine = CreateMachine(MachineConfig as DynamicConfiguration);
                
            if (Machine == null) 
                throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, "{0} failed to create a nested ActivityMachine for {1}", RootName, Name));

            OnSubmachineCreated();
            Machine.IsSynchronous = false;

            Machine.Execute();

            _iteration++;
        }

        /// <summary>
        /// DoWhile has a special implementation that reevaluates the machine execution precondition
        /// after each machine finishes completely.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        protected override void HandleMachineFinished(object sender, EventArgs args)
        {
            // Check the precondition again to see if another iteration can run.
            CanExecute = ExecutePrecondition(PreconditionParam);

            if (CanExecute && !IsQuitting())
            {
                // Unsubscribe from machine
                SubscribeToMachine(Machine, false);
                // Execute another loop iteration.
                ExecuteMachine();
            }
            else
            {
                // If no more iterations can execute, clean up and signal with Done event.
                base.HandleMachineFinished(sender, args);
            }
        }
    }
}
