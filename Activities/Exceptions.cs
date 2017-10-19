using System;
using System.Globalization;

namespace Ventana.Core.Activities
{
    public class ActivityMachineException : Exception
    {
        public ActivityMachineException(string message)
            : base(message)
        {
        }
    }
    public class StateMachineException : Exception
    {
        public StateMachineException(string message)
            : base(message)
        {
        }
    }

    public class StateNotFoundException : Exception
    {
        public StateNotFoundException(string machineName, string stateName)
            : base(string.Format(CultureInfo.InvariantCulture,"{0} could not find a state named {1}", machineName, stateName))
        {
        }
    }

    public class IncompleteMachineException : Exception
    {
        public IncompleteMachineException(string machineName, string operationName)
            : base(string.Format(CultureInfo.InvariantCulture,"Cannot perform {0}.  Assembly is incomplete for {1} machine.", operationName, machineName))
        {
        }
    }

    public class MissingTriggerException : Exception
    {
        public MissingTriggerException(string machineName, string stateName)
            : base(string.Format(CultureInfo.InvariantCulture,"Transition from state {0} must have at least one trigger in {1} state machine.", stateName, machineName))
        {
        }
    }
}
