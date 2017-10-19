using System;
using System.Globalization;
using Ventana.Core.Base.Activities;
using Ventana.Core.Logging;
using Ventana.Core.Utilities;

namespace Ventana.Core.Activities.Executables
{
    /// <summary>
    /// Implements an IF condition using a submachine to realize scope and execution of that scope.
    /// Example preconditions:
    /// (addressableDevice) => (addressableDevice.DigitalInputs[3] == DigitalState.On)
    /// (dataContext) => (dataContext.Variable1 is float && ((float)dataContext.Variable1) == 3.4)
    /// </summary>
    /// <typeparam name="TPreconditionParam">This is either an IDataComposer (for I/O access) or a dynamic DataContext (for variable access)</typeparam>
    public class DoIfConditionActivity<TPreconditionParam> : ExecuteMachineActivity<TPreconditionParam> where TPreconditionParam : class
    {
        public DoIfConditionActivity(string name, TPreconditionParam preconditionParam, Func<TPreconditionParam, bool> precondition, DynamicConfiguration machineConfig)
            : base(name, preconditionParam, precondition, machineConfig)
        {
        }

        public DoIfConditionActivity(string name, TPreconditionParam preconditionParam, Func<TPreconditionParam, bool> precondition, DynamicConfiguration machineConfig, Func<DynamicConfiguration, IActivityMachine> create)
            : base(name, preconditionParam, precondition, machineConfig, create)
        {
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        protected override void ExecuteMachine()
        {
            if (CreateMachine == null) 
                throw new ArgumentNullException(string.Format(CultureInfo.InvariantCulture,"CreateMachine function is not defined for {0}", Name));

            if (IsQuitting())
            {
                LogService.Log(LogType, LogMessageType.Debug, GetType().Name,
                    string.Format(CultureInfo.InvariantCulture, "'{0}' received quitting notification from {1} root ActivityMachine.", Name, RootName));
                base.HandleMachineFinished(this, null);
                return;
            }

            // This new machine will inherit any dispatcher that it finds in the given MachineConfig.
            Machine = CreateMachine(MachineConfig as DynamicConfiguration);

            if (Machine == null)
                throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, "{0} failed to create a nested ActivityMachine for {1}", RootName, Name));

            OnSubmachineCreated();
            Machine.IsSynchronous = false;
            // This is not a synchronous call
            Machine.Execute();
        }
    }
}
