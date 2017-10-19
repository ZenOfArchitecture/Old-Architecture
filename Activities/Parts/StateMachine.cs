using System;
using System.Collections.Generic;
using System.Linq;
using Ventana.Core.Base.Activities;

namespace Ventana.Core.Activities.Parts
{
    public abstract class StateMachine : IDisposable
    {
        protected const string RequiredPseudoStateValue = "Unknown";
        protected bool _isEditable = false;
        protected bool _isAssembled = false;
        protected List<IUmlNode> _nodes = new List<IUmlNode>();
        private readonly object _traverseLock = new object();

        protected StateMachine(string name)
        {
            Name = name;
        }

        /// <summary>
        /// Raised when a state node is first entered, before its behaviors occurr.
        /// </summary>
        public virtual event EventHandler<StateChangedEventArgs> StateEntered;

// This code triggers a warning that the StateChanged event is "never used".
// Wrapped this declaration in pragma warning disable/restore directives to avoid the warning so we can start treating warnings as errors.
// TODO: Remove this warning condition and the corresponding warning disable/restore directives
#pragma warning disable 67
        /// <summary>
        /// Raised when a state node is entered, after its behaviors occurr.
        /// </summary>
        public virtual event EventHandler<StateChangedEventArgs> StateChanged;
#pragma warning restore 67

        public string Name { get; set; }

        public StateNode CurrentNode { get; protected set; }
        public StateNode InitialNode { get; protected set; }
        public UmlNode FinalNode { get; protected set; }

        /// <summary>
        /// Put this machine in edit mode, which allows you to assemble the structure.
        /// </summary>
        public void BeginEditing()
        {
            if (!_isAssembled)
            {
                _isEditable = true;
            }
        }

        /// <summary>
        /// Take this machine out of edit mode, mark it as fully assembled and then
        /// enter the initial state.
        /// </summary>
        public void StopEditing()
        {
            if (_isAssembled)
            {
                return;
            }

            _isEditable = false;
            _isAssembled = true;

            // Enter the initial pseudo-state and possibly beyond.
            EnterInitialNode();
        }

        protected virtual void AddNode(UmlNode node)
        {
            _nodes.Add(node);
            node.Entered += HandleNodeEntered;
            if (node is BehavioralNode)
            {
                (node as BehavioralNode).EntryBehaviorsFinished += HandleNodeEntryBehaviorsFinished;
            }
        }

        /// <summary>
        /// Enter the Initial state and mark it as the current state.  Also, try
        /// to run to completion from the InitialNode.
        /// </summary>
        protected void EnterInitialNode()
        {
            if (!_isAssembled)
            {
                throw new IncompleteMachineException(Name, "EnterInitialNode");
            }
            lock (_traverseLock)
            {
                InitialNode.EnterFrom(this);
                CurrentNode = InitialNode;
                // Try to move on to next real state (not pseudo-state).
                InitialRunToCompletion();
            }
        }

        /// <summary>
        /// Shortcut the operation of the machine by entering a specific node
        /// with an artificial transition.
        /// Hint: this is for unit testing.
        /// </summary>
        /// <param name="transitionToState"></param>
        /// <param name="fakeOrigin"></param>
        /// <param name="fakeArgs"></param>
        protected void TriggerFakeTransition(StateNode transitionToState, object fakeOrigin, TransitionEventArgs fakeArgs)
        {
            if (!_isAssembled)
            {
                throw new IncompleteMachineException(Name, "EnterNode " + transitionToState.Name);
            }
            lock (_traverseLock)
            {
                if (CurrentNode != null)
                {
                    if (!CurrentNode.TryExit())
                    {
                        throw new StateMachineException("Attempt to fake a transition failed: Could not exit " + CurrentNode.Name);
                    }
                }
                // Set current node first
                CurrentNode = transitionToState;
                // then enter new state like transition would
                transitionToState.EnterFrom(fakeOrigin, fakeArgs);
            }
        }

        /// <summary>
        /// Traverse the specified connector, if possible.
        /// This is NOT thread-safe, you must call it in a critical section.
        /// </summary>
        /// <param name="connector"></param>
        /// <param name="args">Optional event args related to the connector traversal</param>
        /// <returns>true if the connector was traversed.</returns>
        protected bool TraverseConnector(IUmlConnector connector, TransitionEventArgs args = null)
        {
            bool traversed = false;

            if (CurrentNode == connector.Supplier && connector.CanTraverse &&
                (connector.Consumer.CanEnter || connector.Consumer == connector.Supplier))
            {
                // Broke this condition out separately because the order of TryExit is important.
                if (connector.Supplier.TryExit()
                    // If Consumer is Supplier, just make sure again that Consumer can enter now that it has been exited.
                    && (connector.Consumer != connector.Supplier || connector.Consumer.CanEnter))
                {
                    // Hook up to the connector's traversed event so that the CurrentNode value will be set before the node is entered.
                    // This allows a node's ENTER and DO behaviors to rely on the new current state.
                    connector.Traversed += HandleConnectorTraversed;

                    // Successful traversal will first set the current state in this machine, 
                    // then set the new state on this machine's IStatefulModel. This order is important.
                    traversed = connector.Traverse(args);
                    
                    // Traversal attempt is finished.  Whether it succeeded or failed, the event handling is no longer needed.
                    connector.Traversed -= HandleConnectorTraversed;
                }
            }
            return traversed;
        }

        protected void InitialRunToCompletion()
        {
            // Get transitions acceptable for initial traversal (only those with no triggers).
            var transitions = from connector in CurrentNode.Connectors
                              let transition = connector as UmlTransition
                              where transition.Triggers.Count == 0
                              select transition;

            lock (_traverseLock)
            {
                // Try to move on to next node using the order of the transitions collection.
                foreach (var t in transitions)
                {
                    if (TraverseConnector(t))
                    {
                        break;
                    }
                }
            }
        }

        internal List<IUmlConnector> FindAllConnectorsTo(IUmlNode targetNode)
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

        protected void HandleTransitionFired(object sender, TransitionEventArgs args)
        {
            var transition = sender as UmlTransition;

            lock (_traverseLock)
            {
                TraverseConnector(transition, args);
            }
        }

        protected void OnStateEntered(StateChangedEventArgs args)
        {
            if (StateEntered != null)
            {
                StateEntered(this, args);
            }
        }

        protected abstract void HandleNodeEntered(object sender, NodeEnteredEventArgs args);

        protected abstract void HandleNodeEntryBehaviorsFinished(object sender, NodeEnteredEventArgs args);

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
                    if (!covered.Contains(connector))
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
        /// Recursively finds all connectors to the target node from the current node.
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
                    if (!found.Contains(connector))
                    {
                        found.Add(connector);
                        FindAllConnectors(connector.Consumer, found);
                    }
                }
            }
        }

        /// <summary>
        /// Handler for a connector Traversed event.  This allows the CurrentNode property to be set before
        /// this StateMachine's IStatefulModel is updated with its new state, and before the ENTER and DO behaviors occur.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="eventArgs"></param>
        private void HandleConnectorTraversed(object sender, EventArgs eventArgs)
        {
            var connector = sender as IUmlConnector;
            // Set the new current state to the node that the connector entered.
            CurrentNode = connector.Consumer as StateNode;
        }

        public void Dispose()
        {
            foreach (var node in _nodes)
            {
                node.Dispose();
            }
            _nodes.Clear();
        }
    }
}
