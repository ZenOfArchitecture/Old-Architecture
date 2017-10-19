using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Ventana.Core.Activities.Parts;
using Ventana.Core.Activities.SpecializedTriggers;
using Ventana.Core.Base;
using Ventana.Core.Base.Activities;
using Ventana.Core.Base.Configuration;
using Ventana.Core.Base.ResourceManagement;
using Ventana.Core.Logging;
using Ventana.Core.Utilities;
using Ventana.Core.Utilities.ExtensionMethods;

namespace Ventana.Core.Activities.Machines
{
    /// <summary>
    /// This class contains UML spec violations.  The order of triggers/transitions is relied
    /// upon for correct behavior.
    /// </summary>
    public class DynamicActivityMachine : ActivityMachine
    {
        /// <summary>
        /// A ResetEvent that will block the machine's execute invoker until it is complete.
        /// </summary>
        private readonly ManualResetEvent _executionSyncEvent = new ManualResetEvent(false);

        protected UmlNode _lastCreatedNode;

        public DynamicActivityMachine(string name)
            : this(name, null)
        {
        }

        protected DynamicActivityMachine(string name, SimpleDispatcher dispatcher)
            : base(name)
        {
            InitializeComponents();
            _hasLocalDispatcher = false;
            Dispatcher = dispatcher;
        }

        public bool RequiresResourceLocking { get; set; }
        public dynamic RuntimeData { get; protected set; }
        private List<IUmlTrigger> RuntimeTriggers { get; set; }
        private IUmlConstraint ExtraFinishConstraint { get; set; }
        private IUmlConstraint ExtraQuitConstraint { get; set; }
        private int ActivityCount { get; set; }
        private int WaitCount { get; set; }
        protected List<ISharedResource> Resources { get; set; }
        public bool IsPaused { get { return ExecutableState == ExecutableState.Paused; } }

        /// <summary>
        /// Gets or sets the finish transition trigger.  This trigger is given to every finish transition.
        /// </summary>
        private UmlTrigger FinishTransitionTrigger { get; set; }
        private DynamicActivity ExecuteBehavior { get; set; }
        private DynamicActivity FinishTransitionEffect { get; set; }
        private DynamicConstraint<DynamicActivityMachine> DefaultFinishConstraint { get; set; }
        private DynamicConstraint<DynamicActivityMachine> DefaultContinueConstraint { get; set; }

        public static SimpleDispatcher ExtractDispatcher(DynamicConfiguration config)
        {
            SimpleDispatcher dispatcher = null;
            if (config.HasDataKey("Dispatcher"))
            {
                dispatcher = config.Data.Dispatcher as SimpleDispatcher;
            }
            return dispatcher;
        }

        public override void Execute()
        {
            if (Builder == null || Configuration == null)
            {
                // Must do teardown directly here. The machine has not started executing itself yet, 
                // so cannot rely on the ExitNode to call this.  This will release any resource locks.
                QuitInternal(true, new ActivityMachineException("Execution failed: Builder or configuration was null."));
                return;
            }

            lock (_executionLock)
            {
                // If an execute trigger already happened, then don't let this run again.
                if (ExecutableState != ExecutableState.NotStarted)
                {
                    // If the machine was already run, no need to do clean-up here, just get out.
                    return;
                }

                // Must initialize initial and final nodes before assembling the rest.
                InitializeNodes();

                // First, lock the actor and assemble the machine's instructions.
                // A Builder may either throw an exception (indicated by false return value),
                // or call Quit (as indicated by ExecutableState being other than NotStarted).
                if (!AssembleMachine() || ExecutableState != ExecutableState.NotStarted)
                {
                    // Just exit, AssembleMachine would already called Complete if necessary in this case.
                    try
                    {
                        _finishedEvent.Set();
                    }
                    catch (ObjectDisposedException)
                    {
                        //nothing to do
                    }
                    return;
                }
                CompletionCause = CompletionCause.Pending;
                // Only set running state after we have successfully assembled this machine.  From here on out, no
                // need to call Quit.
                ExecutableState = ExecutableState.Running;

                // Set all the extra finish & quit conditions that were included for global use. 
                ApplyExtraConstraints();

                // Set all the runtime triggers on each continuation transition.
                ApplyRuntimeTriggers();

                // Signal execution start.
                OnExecutableStarted();

                // Start the expiration timer if there was a timeout value set.
                StartTiming();
            }

            try
            {
                // Kick-off the execution by entering the initial node.
                ExecuteExecutable(ExecuteBehavior);
            }
            catch (Exception ex)
            {
                // Must do teardown directly here. The machine execution is not working, so cannot rely on the
                // ExitNode to call this.  This will release any locks too.
                QuitInternal(true, ex);
                return;
            }

            // If synchronous mode is desired, wait until the last activity or quit signals this is done.
            if (IsSynchronous)
            {
                try
                {
                    _executionSyncEvent.WaitOne();
                }
                catch (ObjectDisposedException)
                {
                    // machine was already disposed. No need for error.
                }
            }
        }

        /// <summary>
        /// Put this machine in edit mode, which allows you to assemble the structure and behavior.
        /// </summary>
        public override void BeginEditing()
        {
            if (!_isAssembled)
            {
                _isEditable = true;
            }
        }

        /// <summary>
        /// Take this machine out of edit mode.  This must be done before execute is called.
        /// </summary>
        public override void StopEditing()
        {
            if (_isEditable)
            {
                var lastConditional = _lastCreatedNode as ConditionalNode;
                // If the last node has a ContinueConstraint, then this is a special case that requires two transitions to 
                // the final node.  We leave the finish transition intact, while adding a continue transition that behaves
                // like the finish transition but does so with the continue constraints.
                if (lastConditional != null && FindContinueTransition(lastConditional) == null)
                {
                    lastConditional.ContinueTransition = lastConditional.TransitionTo(FinalNode);
                    lastConditional.ContinueTransition.Fired += HandleTransitionFired;
                    // Since this continue transition leads to the quit node, it also needs the quit transition effect.
                    lastConditional.ContinueTransition.Effect = FinishTransitionEffect;
                    // Use a copy of the default constraint since it will be extended.
                    // Resulting Continue Guard:     (DefaultContinueConstraint)
                    lastConditional.ContinueTransition.Guard = DefaultContinueConstraint.Copy();
                    // It's possible that the ContinueConstraint is not set yet if it is late-bound at runtime.
                    if (lastConditional.ContinueConstraint != null)
                    {
                        // Resulting Continue Guard:     (DefaultContinueConstraint && lastConditional.ContinueConstraint)
                        lastConditional.ContinueTransition.Guard = lastConditional.ContinueTransition.Guard.AndWith(lastConditional.ContinueConstraint);
                    }
                }
            }
            _isEditable = false;
            _isAssembled = true;
        }

        public override IActivityMachine Copy()
        {
            //TODO:
            return null;
        }

        /// <summary>
        /// This must be used before execution.
        /// </summary>
        /// <param name="resource"></param>
        public void UseLockOnResource(ISharedResource resource)
        {
            if (_isEditable)
            {
                throw new ActivityMachineException("Resource locks must be configured before the machine is in an edit mode.");
            }
            if (resource == null)
            {
                return;
            }
            Resources.Add(resource);
        }

        public void UseRuntimeTrigger(IUmlTrigger trigger)
        {
            if (!_isEditable)
            {
                throw new ActivityMachineException("Assembly can only be done while the machine is in an edit mode.");
            }
            RuntimeTriggers.Add(trigger);

            if (trigger is UmlTrigger)
            {
                (trigger as UmlTrigger).LogType = LogType;
            }
        }

        /// <summary>
        /// Use the given behavior as the last thing to do when this machine halts.
        /// <param name="behavior"></param>
        /// </summary>
        public void UseFinalExitBehavior(IExecutable behavior)
        {
            if (!_isEditable)
            {
                throw new ActivityMachineException("Assembly can only be done while the machine is in an edit mode.");
            }
            // Modify the Final Node with a DO behavior to stop in-progress machine operations.
            (FinalNode as BehavioralNode).SetDoBehavior(behavior);
        }

        /// <summary>
        /// Include an extra finish condition to the entire machine (all finish transitions).
        /// This will be OR-ed onto the end of the existing finish condition on each transition.
        /// </summary>
        /// <param name="finishCondition">the extra finish condition</param>
        public void UseAdditionalFinishCondition(IUmlConstraint finishCondition)
        {
            if (ExtraFinishConstraint == null)
            {
                // copy the incoming constraint because it may be modified and we don't know where it came from.
                ExtraFinishConstraint = finishCondition.Copy();
            }
            else
            {
                // OrWith uses a copy of the given finishCondition.
                ExtraFinishConstraint = ExtraFinishConstraint.OrWith(finishCondition);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="quitCondition"></param>
        public void UseAdditionalQuitCondition(IUmlConstraint quitCondition)
        {
            if (ExtraQuitConstraint == null)
            {
                // copy the incoming constraint because it may be modified and we don't know where it came from.
                ExtraQuitConstraint = quitCondition.Copy();
            }
            else
            {
                // OrWith uses a copy of the given quitCondition.
                ExtraQuitConstraint = ExtraQuitConstraint.OrWith(quitCondition);
            }
        }

        /// <summary>
        /// Add an Activity Node to the end of the chain. 
        /// </summary>
        /// <param name="behavior">the activity for the node to perform</param>
        public virtual void AddActivity(IExecutable behavior)
        {
            if (!_isEditable)
            {
                throw new ActivityMachineException("Assembly can only be done while the machine is in an edit mode.");
            }

            ActivityCount++;
            var activityName = string.IsNullOrEmpty(behavior.Name) ? "Activity" + ActivityCount : behavior.Name;
            // The continuation transition merely receives the machine's standard continue constraint.
            // Resulting Continue Guard: (DefaultContinueConstraint)
            AddNode(new ConditionalNode(activityName, Name, behavior) { LogType = LogType });
        }

        /// <summary>
        /// Add a Continue Condition node to the end of the chain.
        /// </summary>
        /// <param name="continueCondition">The condition required in order to continue</param>
        /// <param name="triggerOverrides">Triggers to use for transitions out of this node in liu of the configured machine-wide triggers.</param>
        public void AddContinueCondition(IUmlConstraint continueCondition, params IUmlTrigger[] triggerOverrides)
        {
            if (!_isEditable)
            {
                throw new ActivityMachineException("Assembly can only be done while the machine is in an edit mode.");
            }

            string conditionName = string.IsNullOrEmpty(continueCondition.Name) ? "Condition" + WaitCount : continueCondition.Name;
            AddContinueCondition(continueCondition, conditionName, triggerOverrides);
        }

        /// <summary>
        /// Add a Continue Condition node to the end of the chain.
        /// </summary>
        /// <param name="continueCondition">The condition required in order to continue</param>
        /// <param name="nodeName"></param>
        /// <param name="triggerOverrides">Triggers to use for transitions out of this node in liu of the configured machine-wide triggers.</param>
        protected void AddContinueCondition(IUmlConstraint continueCondition, string nodeName, params IUmlTrigger[] triggerOverrides)
        {
            if (!_isEditable)
            {
                throw new ActivityMachineException("Assembly can only be done while the machine is in an edit mode.");
            }

            WaitCount++;
            // Resulting Continue Guard: (DefaultContinueConstraint && condition)
            // Resulting Quit Guard:     (DefaultQuitConstraint)
            //TODO: use smarter way of sensing when previous node was a WAIT and therefore has to be followed by a constraint guarded transition
            var conditionalNode = new ConditionalNode(nodeName, Name, continueCondition) { LogType = LogType };
            // Give the node any overriding triggers. These will be the triggers that evaluate the conditional node's
            // constraint because the constraint becomes the Guard of the Continue Transition out of the new node.
            conditionalNode.OverridingTriggers.AddRange(triggerOverrides);
            AddNode(conditionalNode);
        }

        /// <summary>
        /// Add a Conditional Activity node pair to the end of the chain.
        /// </summary>
        /// <param name="continueCondition">The condition required in order to continue</param>
        /// <param name="behavior">the activity to perform</param>
        /// <param name="triggerOverrides">Triggers to use for transitions out of this conditional node in liu of the configured machine-wide triggers.</param>
        public void AddConditionalActivity(IUmlConstraint continueCondition, IExecutable behavior, params IUmlTrigger[] triggerOverrides)
        {
            // Resulting Continue Guard: (DefaultContinueConstraint && continueCondition)
            // Resulting Quit Guard:     (DefaultQuitConstraint)
            AddContinueCondition(continueCondition, triggerOverrides);
            // Resulting Continue Guard: (DefaultContinueConstraint)
            // Resulting Quit Guard:     (DefaultQuitConstraint)
            AddActivity(behavior);
        }

        /// <summary>
        /// Add a Quit (with Interrupted CompletionCause) or Continue Condition to the end of the chain.  
        /// This adds a quit transition with an Effect that sets the machine's CompletionCause to Interrupted.
        /// </summary>
        /// <param name="quitCondition">The additional condition that results in quitting with Finished completion cause.</param>
        /// <param name="continueCondition">the condition that results in continuing</param>
        /// <param name="triggerOverrides">Triggers to use for transitions out of this conditional node in liu of the configured machine-wide triggers.</param>
        public void AddQuitOrContinueCondition(IUmlConstraint quitCondition, IUmlConstraint continueCondition, params IUmlTrigger[] triggerOverrides)
        {
            // Resulting Continue Guard: (DefaultContinueConstraint && continueCondition)
            // Resulting Quit Guard:     (quitCondition)
            // Resulting Quit Effect:    CompletionCause = CompletionCause.Interrupted
            AddContinueCondition(continueCondition, "QUIT: " + quitCondition.Name, triggerOverrides);
            var conditionalNode = _lastCreatedNode as ConditionalNode;
            // override the default "never quit" guard with the supplied quit constraint.
            conditionalNode.QuitTransition.Guard = quitCondition;
        }

        /// <summary>
        /// Add a Finish (with Finished CompletionCause) or Continue Condition to the end of the chain.  
        /// This does not add an extra finish transition, it just modifies the default finish transition's 
        /// guard to include the new finish condition.
        /// </summary>
        /// <param name="finishCondition">The additional condition that results in quitting with Finished completion cause.</param>
        /// <param name="continueCondition">the condition that results in continuing</param>
        /// <param name="triggerOverrides">Triggers to use for transitions out of this conditional node in liu of the configured machine-wide triggers.</param>
        public void AddFinishOrContinueCondition(IUmlConstraint finishCondition, IUmlConstraint continueCondition, params IUmlTrigger[] triggerOverrides)
        {
            // Resulting Continue Guard: (DefaultContinueConstraint && continueCondition)
            // Resulting Finish Guard:     (DefaultFinishConstraint || finishCondition)
            AddContinueCondition(continueCondition, "FINISH: " + finishCondition.Name, triggerOverrides);
            var finishTransition = FindFinishTransition(_lastCreatedNode);
            // Update the finish transition's Guard to permit the trigger to fire under the additional finishCondition.
            finishTransition.Guard = finishTransition.Guard.OrWith(finishCondition);
        }

        /// <summary>
        /// Obtain operational locks on this machine's resources.
        /// </summary>
        public virtual bool ObtainResourceLocks()
        {
            foreach (var resource in Resources)
            {
                if (!resource.ObtainLock(this))
                {
                    //TODO: log it or throw exception
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Release operational locks on this machine's resources.
        /// </summary>
        public virtual void ReleaseResourceLocks()
        {
            Resources.ForEach(a => a.ReleaseLock(this));
        }

        /// <summary>
        /// Add a node to the end of the node list.  The new node will get a finish transition and
        /// a continue transition.
        /// </summary>
        /// <param name="node">the node to add</param>
        /// <param name="inboundContinueConstraint">An extra constraint to apply to the continue transition going IN to the added node</param>
        protected void AddNode(StateNode node, IUmlConstraint inboundContinueConstraint = null)
        {
            node.Entered += HandleNodeEntered;
            node.Exited += HandleNodeExited;
            node.Dispatcher = Dispatcher;
            node.Faulted += HandleNodeFaulted;
            node.TimedOut += HandleNodeTimedOut;

            // add transitions to the final node.
            AddFinalTransitions(node);

            if (_lastCreatedNode != null)
            {
                // Create the Continue transition for the previous node now that it has a consumer.
                UmlTransition continueTransition = _lastCreatedNode.TransitionTo(node);
                continueTransition.Fired += HandleTransitionFired;
                // Use a copy of the default constraint since it will be extended.
                continueTransition.Guard = DefaultContinueConstraint.Copy();
                if (inboundContinueConstraint != null)
                {
                    continueTransition.Guard = continueTransition.Guard.AndWith(inboundContinueConstraint);
                }

                var previousNode = _lastCreatedNode as ConditionalNode;
                if (previousNode != null)
                {
                    previousNode.ContinueTransition = continueTransition;
                    // If the previous node has a ContinueConstraint, then make the new transition inherit it as a guard.
                    // The given constraint must be AND-ed with the machine's guard for all continuation transitions.
                    // It's possible that the ContinueConstraint is not set yet if it is late-bound at runtime.
                    if (previousNode.ContinueConstraint != null)
                    {
                        continueTransition.Guard = continueTransition.Guard.AndWith(previousNode.ContinueConstraint);
                    }
                }
            }

            // Hold a reference to last new node for assembly purposes.
            _lastCreatedNode = node;
        }

        /// <summary>
        /// Adds the final transitions for the given node.  The node's first final transition is
        /// a Finish transition.  The second one is a Quit transition that gets a default guard so
        /// that it will never fire.
        /// </summary>
        /// <param name="node"></param>
        protected void AddFinalTransitions(UmlNode node)
        {
            // Set up the finish transition first so that it is triggered first.
            var finishTransition = node.TransitionTo(FinalNode);
            finishTransition.Fired += HandleTransitionFired;
            // Every node gets an finish transition pointing to Final using the Finish Constraint, Effect and Trigger.
            // Use a copy of the default constraint in case one transition needs to modify it uniquely.
            finishTransition.Guard = DefaultFinishConstraint.Copy();
            finishTransition.Effect = FinishTransitionEffect;
            // Use a copy of the finish trigger since it will likely be reused many times.
            finishTransition.UseTrigger(FinishTransitionTrigger.Copy());

            // Next, set up the quit transition with a default to never quit.
            if (node is ConditionalNode)
            {
                var conditionalNode = node as ConditionalNode;
                conditionalNode.QuitTransition = conditionalNode.TransitionTo(FinalNode);
                conditionalNode.QuitTransition.Guard = new DynamicConstraint<object>("Never True", new object(), (o) => false);
                conditionalNode.QuitTransition.Fired += HandleTransitionFired;
                conditionalNode.QuitTransition.Effect = new DynamicActivity("SetCompletionCauseInterrupted", RunQuitTransitionEffect) { LogType = LogType };
            }
        }

        /// <summary>
        /// Set all the configured runtime triggers on each transition in this machine. Default case
        /// uses the global triggers, but overrides are applied here if they exist.
        /// </summary>
        protected virtual void ApplyRuntimeTriggers()
        {
            var transitions = FindAllConnectors().OfType<UmlTransition>();
            foreach (var transition in transitions)
            {
                List<IUmlTrigger> triggers = RuntimeTriggers;
                // If the transition's supplier is a conditional node that was given overriding triggers, 
                // apply those instead of the machine's defaults.
                var supplier = transition.Supplier as ConditionalNode;
                if (supplier != null && supplier.OverridingTriggers.Count > 0)
                {
                    triggers = supplier.OverridingTriggers;
                }
                
                foreach (var trigger in triggers)
                {
                    // This change made in relation to a change to UmlTransition class.  A copy of the
                    // trigger used to be obtained in the UseTrigger method, but it proved cumbersome there.
                    // The Copy call was moved out of UmlTrigger, so putting it at the method usages.
                    // A copy is required here because each trigger is reused for each transition.
                    transition.UseTrigger(trigger.Copy());
                }
            }
        }

        /// <summary>
        /// Apply extra finish constraints to Finish transitions and extra quit constraints to Quit transitions.
        /// </summary>
        protected virtual void ApplyExtraConstraints()
        {
            var finalTransitions = FindAllConnectorsTo(FinalNode).OfType<UmlTransition>();
            foreach (var finalTransition in finalTransitions)
            {
                // Make sure it's a finish transition, not a quit transition.
                if (ExtraFinishConstraint != null && finalTransition == FindFinishTransition(finalTransition.Supplier as UmlNode))
                {
                    finalTransition.Guard = finalTransition.Guard.OrWith(ExtraFinishConstraint);
                }
                // Make sure it's a quit transition.
                else if (ExtraQuitConstraint != null && finalTransition == FindQuitTransition(finalTransition.Supplier as UmlNode))
                {
                    finalTransition.Guard = finalTransition.Guard.OrWith(ExtraQuitConstraint);
                }
            }
        }

        /// <summary>
        /// Run to completion ensures that the ActivityMachine is in a stable state before it gives
        /// up control to external events.  This implementation differs from the semantics of a 
        /// state machine in that a run to completion may include traversal over many sequential connectors, 
        /// and running through any number of actions.  
        /// A connector is considered acceptable even if it does have triggers.  This means that
        /// the guard condition must be designed to handle conditions present during this call.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        protected override void RunToCompletion()
        {
            lock (_executionLock)
            {
                // Escape if the machine is paused and unfinished.  However, if it has been expired, faulted, or interrupted
                // ignore the pause state and continue traversing to completion.
                if (CurrentNode == null || (ExecutableState == ExecutableState.Paused && CompletionCause == CompletionCause.Pending))
                {
                    return;
                }
            }

            // Get a read-only copy of the node's connectors before anything is put on the dispatcher.
            var connectors = CurrentNode.Connectors;

            if (connectors.Any())
            {
                // Just queue an activity to try traversing all connectors in order.  Only one, if any, will succeed.
                ExecuteExecutable(new DynamicActivity(Name + ".RunToCompletion", () =>
                {
                    bool transitioned = false;
                    // Try to move on to next node using the order of the connectors collection.
                    // By the way, using the order of connectors is a violation of UML spec.
                    foreach (var c in connectors)
                    {
                        transitioned = TraverseConnector(c);
                        if (transitioned)
                        {
                            break;
                        }
                    }
                    if (!transitioned)
                    {
                        LogService.Log(LogType, LogMessageType.Trace, Name,
                            string.Format(CultureInfo.InvariantCulture, "{0} execution impeded at '{1}' node.  Awaiting a trigger for transition reevaluation.", Name, CurrentNode.Name));
                    }
                }));
            }
            else
            {
                // If the current node was a dead end (Final node), just run its exit method and be done.
                if (!CurrentNode.TryExit())
                {
                    LogService.Log(LogType, LogMessageType.Error, Name,
                        string.Format(CultureInfo.InvariantCulture, "{0} could not exit '{1}' node.", Name, CurrentNode.Name));
                }
            }
        }

        internal void InitializeNodes()
        {
            if (InitialNode != null)
            {
                return;
            }
            InitialNode = new StateNode("Initial", Name) { LogType = LogType };
            FinalNode = new StateNode("Final", Name) { LogType = LogType };
            // This final behavior will happen on the machine's dispatcher.
            (FinalNode as StateNode).SetExitBehavior(null, new NodeBehavior("Finish", (reason) => CompleteExecution(reason, Fault)));
            // Only add the Initial node, not the Final node, because adding creates connectors for quitting.
            AddNode(InitialNode as StateNode);
        }

        /// <summary>
        /// The method used as the effect of an finish transition.  A machine's completion cause may not
        /// have been set yet when the current node transitions to the final state.  It could be that
        /// the current node only has one transition, which must then lead to final, or it could be
        /// that the node's finish transition guard evaluated to true.
        /// In either case the CompletionCause will not have been set yet, so this method sets the
        /// completion cause to Finished.
        /// </summary>
        private void RunFinishTransitionEffect()
        {
            lock (_executionLock)
            {
                if (CompletionCause == CompletionCause.Pending)
                {
                    CompletionCause = CompletionCause.Finished;
                }
            }
        }

        /// <summary>
        /// The method used as the effect of a quit transition.  A machine's completion cause may not
        /// have been set yet when the current node transitions to the final state.  It could be that
        /// the current node only has one transition, which must then lead to final, or it could be
        /// that the node's quit transition guard evaluated to true.
        /// In either case the CompletionCause will not have been set yet, so this method sets the
        /// completion cause to Interrupted.
        /// </summary>
        private void RunQuitTransitionEffect()
        {
            lock (_executionLock)
            {
                if (CompletionCause == CompletionCause.Pending)
                {
                    CompletionCause = CompletionCause.Interrupted;
                }
            }
        }

        private void InitializeComponents()
        {
            RuntimeTriggers = new List<IUmlTrigger>();
            Resources = new List<ISharedResource>();
            RuntimeData = new ExpandoObject();
            // Setup the behavior to start machine execution.
            ExecuteBehavior = new DynamicActivity("Execute", EnterInitialNode) { LogType = LogType };
            ExecuteBehavior.Faulted += HandleExecuteInitialBehaviorFaulted;
            // This makes every QuitTransition reactive to this machine's "Quitting" event.
            FinishTransitionTrigger = new QuitHandlingTrigger("Finishing", this) { LogType = LogType };
            // This allows a QuitTransition to fire if the machine's CompletionCause is set or if it finishes.
            DefaultFinishConstraint = new DynamicConstraint<DynamicActivityMachine>("Default Finish Constraint", this, CheckMachineCanFinish) { SuppressLogging = true };
            // This ensures that a CompletionCause is set (not pending) before FinalNode is entered (and before its completion behavior is run).
            FinishTransitionEffect = new DynamicActivity("SetCompletionCauseFinished", RunFinishTransitionEffect) { LogType = LogType };
            // This ensures that the machine's state is still running and that a completion cause is still pending.
            DefaultContinueConstraint = new DynamicConstraint<DynamicActivityMachine>("Default Continue Constraint", this, CheckMachineCanNotFinish) { SuppressLogging = true };
        }

        private bool AssembleMachine()
        {
            try
            {
                // Step 1: Get an operation lock on the actors to prepare this machine for its assembly.
                // It's okay to do this again if it was already called externally.
                if (RequiresResourceLocking && !ObtainResourceLocks())
                {
                    ReleaseResourceLocks();
                    // return without completing so that another attempt to lock can be made later.
                    return false;
                }

                BeginEditing();

                // Step 2: Use the builder and config to assemble this machine's instruction set.
                Builder.Build(this);

                StopEditing();

                return _isAssembled;
            }
            catch (Exception e)
            {
                CompletionCause = CompletionCause.Faulted;
                // Must do teardown directly here. The machine has not started executing itself yet, 
                // so cannot rely on the ExitNode to call this.  This will release any resource locks.
                QuitInternal(true, e);
            }
            return false;
        }

        /// <summary>
        /// Deactivate releases actor locks and signals that execution is done.
        /// The machine should only deactivate once, so call this from critical sections that can make such a promise.
        /// </summary>
        protected override void Deactivate()
        {
            base.Deactivate();

            if (RequiresResourceLocking)
            {
                ReleaseResourceLocks();
            }
            if (ExecuteBehavior != null)
            {
                ExecuteBehavior.Faulted -= HandleExecuteInitialBehaviorFaulted;
            }

            // signal the execution invoker that we are done.
            _executionSyncEvent.Set();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _executionSyncEvent.Dispose();
                foreach (var trigger in RuntimeTriggers)
                {
                    trigger.Dispose();
                }
                RuntimeTriggers.Clear();
                RuntimeTriggers = null;

                if (FinishTransitionTrigger != null)
                {
                    FinishTransitionTrigger.Dispose();
                }
                FinishTransitionEffect = null;

            }
            base.Dispose(disposing);
        }
        
        private void HandleExecuteInitialBehaviorFaulted(object sender, FaultedEventArgs args)
        {
            // Must do teardown directly here. The machine execution is not working, so cannot rely on the
            // ExitNode to call this.  This will release any locks too.
            QuitInternal(true, args.Cause);
        }

        private void HandleTransitionFired(object sender, TransitionEventArgs args)
        {
            var transition = sender as UmlTransition;
            // Put a new activity on the dispatcher to try to traverse the fired transition.
            ExecuteExecutable(new DynamicActivity(Name + ".HandleTransitionFired", () => TraverseConnector(transition, args)) { LogType = LogType });
        }
    }
}
