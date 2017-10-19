using System;
using Ventana.Core.Utilities;

namespace Ventana.Core.Activities.Executables
{
    /// <summary>
    /// Implements a For Loop using submachines to realize scope and execution of that scope.
    /// Takes a Function used to compare the ForCounter value to Low and High Values for the for loop.
    /// If the For Counter is within range then the submachine is executed.
    /// Actions are used to initiate the ForCounter variable and to increment the ForCounter after each iteration.
    /// </summary>
    /// <typeparam name="TPreconditionParam"></typeparam>
    public class ForLoopActivity<TPreconditionParam> : DoWhileConditionActivity<TPreconditionParam> where TPreconditionParam : class
    {
        private readonly Action<TPreconditionParam> _setForCounterInitialValue;
        private readonly Action<TPreconditionParam> _incrementForCounter;

        /// <summary>
        /// Ctor using the default CreateMachine delegate.
        /// </summary>
        /// <param name="name">Name for Action</param>
        /// <param name="preconditionParam">Parameter used by the Func and Actions</param>
        /// <param name="precondition">Func to determine if the ForLoop is in range</param>
        /// <param name="machineConfig">MachineConfig used to build the submachine</param>
        /// <param name="setCountertoInitial">Action to set intial value of the ForCounter variable</param>
        /// <param name="incrementCounter">Action to increment the ForCounter variable after an interation.</param>
        public ForLoopActivity(string name, TPreconditionParam preconditionParam, Func<TPreconditionParam, bool> precondition, DynamicConfiguration machineConfig, Action<TPreconditionParam> setCountertoInitial, Action<TPreconditionParam> incrementCounter) 
            : base(name, preconditionParam, precondition, machineConfig)
        {
            _setForCounterInitialValue = setCountertoInitial;
            _incrementForCounter = incrementCounter;
        }

        /// <summary>
        /// Specialization that initializes the loop variable.
        /// </summary>
        protected override void RunAction()
        {
            // initate the ForCounter value to Initial value of loop.
            _setForCounterInitialValue(PreconditionParam);
            
            base.RunAction();
        }

        /// <summary>
        /// ForLoop has a special implementation that evaluates the ForLoop counter
        /// to determine if the machine needs to be ran again.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        protected override void HandleMachineFinished(object sender, EventArgs args)
        {
            // increment the ForCounter variable after each iteration.
            _incrementForCounter(PreconditionParam);

            base.HandleMachineFinished(sender, args);
        }
    }
}
