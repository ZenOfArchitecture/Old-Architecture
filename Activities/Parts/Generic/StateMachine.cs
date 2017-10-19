using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Ventana.Core.Base.Activities;
using Ventana.Core.Base.BusinessObjects.Generic;
using Ventana.Core.Logging;

namespace Ventana.Core.Activities.Parts.Generic
{
    public class StateMachine<TState> : StateMachine where TState : struct
    {
        public StateMachine(string name) : this(name, null)
        {
        }

        public StateMachine(string name, IStatefulModel<TState> statefulModel) : base(name)
        {
            StatefulModel = statefulModel;
            InitializeStates();
            InitializeNodes();
        }

        /// <summary>
        /// Raised when a state node is first entered, before its behaviors occurr.
        /// </summary>
        public new event EventHandler<StateChangedEventArgs<TState>> StateEntered;

        /// <summary>
        /// Raised when a state node is entered, after its behaviors occurr.
        /// </summary>
        public new event EventHandler<StateChangedEventArgs<TState>> StateChanged;
        
        public TState CurrentState
        {
            get { return GetNodeAsState(CurrentNode); }
        }

        public StateNode this[TState desiredState]
        {
            get
            {
                var node = FindState(desiredState);
                if (node == null)
                {
                    throw new StateNotFoundException(Name, desiredState.ToString());
                }
                return node;
            }
        }

        protected IStatefulModel<TState> StatefulModel { get; set; }

        /// <summary>
        /// Add a transition from the given supplier to the given consumer.
        /// </summary>
        /// <param name="supplierState"></param>
        /// <param name="consumerState"></param>
        /// <param name="triggers">A required set of triggers</param>
        /// <returns>the new transition</returns>
        public UmlTransition AddTransition(TState supplierState, TState consumerState, params IUmlTrigger[] triggers)
        {
            return AddTransition(supplierState, consumerState, null, null, triggers);
        }

        /// <summary>
        /// Add a transition from the given supplier to the given consumer.
        /// </summary>
        /// <param name="supplierState"></param>
        /// <param name="consumerState"></param>
        /// <param name="guard">an optional guard for the transition</param>
        /// <param name="triggers">A required set of triggers</param>
        /// <returns>the new transition</returns>
        public UmlTransition AddTransition(TState supplierState, TState consumerState, IUmlConstraint guard, params IUmlTrigger[] triggers)
        {
            return AddTransition(supplierState, consumerState, guard, null, triggers);
        }

        /// <summary>
        /// Add a transition from the given supplier to the given consumer.
        /// </summary>
        /// <param name="supplierState"></param>
        /// <param name="consumerState"></param>
        /// <param name="guard">an optional guard for the transition</param>
        /// <param name="effect">an optional effect of the transition</param>
        /// <param name="triggers">A required set of triggers</param>
        /// <returns>the new transition</returns>
        public virtual UmlTransition AddTransition(TState supplierState, TState consumerState, IUmlConstraint guard, Action effect, params IUmlTrigger[] triggers)
        {
            if (triggers == null || !triggers.Any())
            {
                throw new MissingTriggerException(Name, supplierState.ToString());
            }
            var supplier = FindState(supplierState);
            if (supplier == null)
            {
                throw new StateNotFoundException(Name, supplierState.ToString());
            }
            var consumer = FindState(consumerState);
            if (consumer == null)
            {
                throw new StateNotFoundException(Name, consumerState.ToString());
            }
            var transition = supplier.TransitionTo(consumer, guard, effect, triggers);
            transition.Fired += HandleTransitionFired;
            return transition;
        }

        /// <summary>
        /// Add a transition from the initial pseudo-state to the given consumer.
        /// </summary>
        /// <param name="consumerState"></param>
        public void AddInitialTransition(TState consumerState)
        {
            AddInitialTransition(consumerState, (IUmlConstraint)null, null);
        }

        /// <summary>
        /// Add a transition from the initial pseudo-state to the given consumer. Accepts triggers.
        /// </summary>
        /// <param name="consumerState"></param>
        /// <param name="triggers"></param>
        public void AddInitialTransition(TState consumerState, params IUmlTrigger[] triggers)
        {
            AddInitialTransition(consumerState, null, triggers);
        }

        /// <summary>
        /// Add a transition from the initial pseudo-state to the given consumer.  Triggers and guards are both
        /// optional for initial transitions.
        /// </summary>
        /// <param name="consumerState"></param>
        /// <param name="guard">an optional guard for the transition</param>
        /// <param name="triggers">An optional set of triggers</param>
        public void AddInitialTransition(TState consumerState, IUmlConstraint guard, params IUmlTrigger[] triggers)
        {
            var consumer = FindState(consumerState);
            if (consumer == null)
            {
                throw new StateNotFoundException(Name, consumerState.ToString());
            }
            var transition = InitialNode.TransitionTo(consumer, guard, null, triggers);
            transition.Fired += HandleTransitionFired;
        }

        /// <summary>
        /// Add a transition from the given supplier to the final pseudo-state.  Neither guard nor triggers are allowed.
        /// </summary>
        /// <param name="supplierState"></param>
        /// <param name="triggers">A required set of triggers</param>
        public void AddFinalTransition(TState supplierState, params IUmlTrigger[] triggers)
        {
            AddFinalTransition(supplierState, null, triggers);
        }

        /// <summary>
        /// Add a transition from the given supplier to the final pseudo-state.  Neither guard nor triggers are allowed.
        /// </summary>
        /// <param name="supplierState"></param>
        /// <param name="guard">an optional guard for the transition</param>
        /// <param name="triggers">A required set of triggers</param>
        public void AddFinalTransition(TState supplierState, IUmlConstraint guard, params IUmlTrigger[] triggers)
        {
            //TODO: remove trigger requirement on final transitions?
            if (triggers == null || !triggers.Any())
            {
                throw new MissingTriggerException(Name, supplierState.ToString());
            }
            var supplier = FindState(supplierState);
            if (supplier == null)
            {
                throw new StateNotFoundException(Name, supplierState.ToString());
            }
            var transition = supplier.TransitionTo(FinalNode, guard, null, triggers);
            transition.Fired += HandleTransitionFired;
        }

        /// <summary>
        /// Shortcut the operation of the machine by entering a specific state node.
        /// Hint: this is for unit testing.
        /// </summary>
        /// <param name="transitionToState"></param>
        /// <param name="fakeOrigin"></param>
        /// <param name="fakeArgs"></param>
        internal void TriggerFakeTransition(TState transitionToState, object fakeOrigin, TransitionEventArgs fakeArgs)
        {
            TriggerFakeTransition(FindState(transitionToState), fakeOrigin, fakeArgs);
        }

        public static TState GetStringAsState(string state)
        {
            return (TState)Enum.Parse(typeof(TState), state);
        }

        protected static TState GetNodeAsState(UmlNode node)
        {
            return GetStringAsState(node.Name);
        }

        protected StateNode FindState(TState state)
        {
            return _nodes.FirstOrDefault(n => n.Name == state.ToString()) as StateNode;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        protected void AddState(string nodeName)
        {
            if (_isAssembled)
            {
                throw new StateMachineException("States can only be added before assembly is done.");
            }
            TState stateEnum;
            if (Enum.TryParse(nodeName, out stateEnum))
            {
                AddNode(new StateNode(nodeName, Name));
            }
            else
            {
                LogService.Log(LogType.System, LogMessageType.Error, GetType().Name,
                    string.Format(CultureInfo.InvariantCulture,"Failed to add a state to state machine for {0}:  {1} did not parse into a {2}.", Name, nodeName, typeof(TState).Name));
            }
        }

        /// <summary>
        /// To be called when a state node has been entered.  
        /// The new state has already been assigned to the machine's model.
        /// </summary>
        /// <param name="args"></param>
        protected void OnStateEntered(StateChangedEventArgs<TState> args)
        {
            if (StateEntered != null)
            {
                StateEntered(this, args);
            }
        }

        /// <summary>
        /// To be called when all node entry (Enter,Do) behaviors have completed.
        /// </summary>
        /// <param name="args"></param>
        protected void OnStateChanged(StateChangedEventArgs<TState> args)
        {
            if (StateChanged != null)
            {
                StateChanged(this, args);
            }
        }

        #region Handling Events

        protected override void HandleNodeEntryBehaviorsFinished(object sender, NodeEnteredEventArgs args)
        {
            // Raise the state change event now that the behaviors are done.
            OnStateChanged(GetStateChangedEventArgs(sender, args));
        }

        /// <summary>
        /// Handles node entry event, which happens before the ENTRY Behavior is executed.
        /// This implementation sets the machine's model's state to the new value given by the entered node.
        /// This inherits thread-safety from the critical sections established around TraverseConnector calls.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        protected override void HandleNodeEntered(object sender, NodeEnteredEventArgs args)
        {
            var changeArgs = GetStateChangedEventArgs(sender, args);

            // Update the StatefulModel, if there is one.
            if (StatefulModel != null)
            {
                StatefulModel.State = changeArgs.NewState;
            }

            // Raise an event for the state entry.
            OnStateEntered(changeArgs);
        }
        #endregion

        private StateChangedEventArgs<TState> GetStateChangedEventArgs(object sender, NodeEnteredEventArgs args)
        {
            var newState = GetNodeAsState(sender as StateNode);
            var oldState = GetStringAsState(RequiredPseudoStateValue);
            var changeArgs = new StateChangedEventArgs<TState>() { NewState = newState, OldState = oldState };
            var transition = args.EnteredFrom as UmlTransition;
            if (transition != null && transition.Supplier is StateNode)
            {
                changeArgs.OldState = GetNodeAsState(transition.Supplier as StateNode);
            }
            return changeArgs;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        private void InitializeStates()
        {
            var stateNames = Enum.GetNames(typeof(TState));
            if (!stateNames.Any(name => name.Equals(RequiredPseudoStateValue)))
            {
                throw new StateMachineException("StateMachine requires a state enumeration that contains the value " + RequiredPseudoStateValue);
            }
            foreach (var stateName in stateNames)
            {
                if (_nodes.Any(n => n.Name == stateName.ToString()))
                {
                    LogService.Log(LogType.System, LogMessageType.Error, GetType().Name,
                        string.Format(CultureInfo.InvariantCulture, "A state named '{0}' already exists in state machine for {1}.", stateName, Name));
                    continue;
                }
                AddState(stateName);
            }
        }

        private void InitializeNodes()
        {
            InitialNode = new StateNode(RequiredPseudoStateValue, Name);
            FinalNode = new UmlNode(RequiredPseudoStateValue, Name);
            //TODO: is this correct? copied from activityMachine
            // Only add the Initial node, not the Final node.
            AddNode(InitialNode);
        }
    }
}
