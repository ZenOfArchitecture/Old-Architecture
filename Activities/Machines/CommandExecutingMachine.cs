using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Globalization;
using Ventana.Core.Activities.Parts;
using Ventana.Core.Base;
using Ventana.Core.Base.Activities;
using Ventana.Core.Base.BusinessObjects;
using Ventana.Core.Base.Common;
using Ventana.Core.Base.Utilities;
using Ventana.Core.ExceptionHandling;
using Ventana.Core.Logging;
using Ventana.Core.Utilities;
using Ventana.Core.Utilities.ExtensionMethods;

namespace Ventana.Core.Activities.Machines
{
    public class CommandExecutingMachine : DynamicActivityMachine
    {
        private const string VariablesKey = "Variables";
        //private int _pauseCount;

        /// <summary>
        /// Ctor for building a top-level machine that may own submachines.  This creates a new
        /// DataContext.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="config"></param>
        public CommandExecutingMachine(string name, DynamicConfiguration config)
            : base(ExtractNamePrefix(config.Data as IDictionary<string, object>) + name)
        {
            DataContext = new ExpandoObject();
            DataContext.Device = config.Data.Device;
            //TODO: get rid of Station and only use Module
            DataContext.Station = config.Data.Station;
            DataContext.Module = config.Data.Module;
            DataContext.ErrorLevel = 0;
            DataContext.Variables = new Dictionary<string, object>();
            if (config.HasDataKey(Key.Translator))
            {
                DataContext.Translator = config.Data.Translator;
            }
            if (config.HasDataKey(Key.Instrument))
            {
                DataContext.Instrument = config.Data.Instrument;
            }

            InitializeComponents(config);
        }
        
        /// <summary>
        /// Ctor used for building nested machines because it tries to reuse the DataContext from a root machine.
        /// If the given config has a SimpleDispatcher included,  this machine will use it.  
        /// Otherwise, this machine will create its own SimpleDispatcher.
        /// </summary>
        /// <param name="config"></param>
        /// <param name="name"></param>
        public CommandExecutingMachine(DynamicConfiguration config, string name)
            : base(ExtractNamePrefix(config.Data as IDictionary<string, object>) + name, ExtractDispatcher(config))
        {
            DataContext = ExtractDataContext(config);

            InitializeComponents(config);
        }

        public event EventHandler<UmlNodeCreatedEventArgs> PausableNodeAdded;
        public event EventHandler<PausedEventArgs> PausableNodeEntered;

        public bool SupportsSelfPausing { get; internal set; }

        public dynamic DataContext { get; set; }

        public static object ExtractDataContext(DynamicConfiguration config)
        {
            object dataContext = null;
            if (config.HasDataKey(Key.DataContext))
            {
                dataContext = config.Data.DataContext;
            }
            return dataContext;
        }

        public static ActivityMachine ExtractRootMachine(IDictionary<string, object> configData)
        {
            ActivityMachine rootMachine = null;
            if (configData.ContainsKey(Key.RootMachine))
            {
                rootMachine = configData[Key.RootMachine] as ActivityMachine;
            }
            else if (configData.ContainsKey(Key.DataContext) && ((IDictionary<string, object>)configData[Key.DataContext]).ContainsKey(Key.RootMachine))
            {
                rootMachine = ((IDictionary<string, object>)configData[Key.DataContext])[Key.RootMachine] as ActivityMachine;
            }
            return rootMachine;
        }

        public static string ExtractNamePrefix(IDictionary<string, object> configData)
        {
            ActivityMachine rootMachine = ExtractRootMachine(configData);
            if (rootMachine != null)
            {
                return rootMachine.Name + "::";
            }
            return string.Empty;
        }

        public static string ExtractThreadName(DynamicConfiguration config)
        {
            ActivityMachine rootMachine = ExtractRootMachine(config.Data as IDictionary<string, object>);
            if (rootMachine != null)
            {
                return rootMachine.ThreadName;
            }
            return string.Empty;
        }

        public bool HasDataContextKey(string dataKey)
        {
            if ((DataContext is IDictionary<string, object>)
                && (DataContext as IDictionary<string, object>).ContainsKey(dataKey))
            {
                return true;
            }
            return false;
        }

        public bool HasDataContextValue(string dataKey)
        {
            if (HasDataContextKey(dataKey)
                && (DataContext as IDictionary<string, object>)[dataKey] != null)
            {
                return true;
            }
            return false;
        }

        public bool HasDataContextVariable(string variableName)
        {
            if ((DataContext is IDictionary<string, object>)
                && (DataContext as IDictionary<string, object>).ContainsKey(VariablesKey))
            {
                var variables = (DataContext as IDictionary<string, object>)[VariablesKey] as IDictionary<string, object>;
                if (variables != null)
                {
                    return variables.ContainsKey(variableName);
                }
            }
            return false;
        }

        public bool HasDataContextVariableValue(string variableName)
        {
            if (HasDataContextVariable(variableName)
                && ((DataContext as IDictionary<string, object>)[VariablesKey] as IDictionary<string, object>)[variableName] != null)
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Add an Activity Node to the end of the chain. 
        /// </summary>
        /// <param name="behavior">the activity for the node to perform</param>
        public override void AddActivity(IExecutable behavior)
        {
            if (!_isEditable)
            {
                throw new ActivityMachineException("Assembly can only be done while the machine is in an edit mode.");
            }

            base.AddActivity(behavior);

            // If the DO behavior is host to a nested ActivityMachine, need to add a handler for the SubmachineDone event to the node.
            var lastStateNode = _lastCreatedNode as StateNode;
            if (lastStateNode != null && lastStateNode.DoBehavior is ISubmachineHost)
            {
                lastStateNode.SubmachineDone += HandleSubmachineDone;
                lastStateNode.SubmachinePaused += HandleSubmachinePaused;
                lastStateNode.SubmachineResumed += HandleSubmachineResumed;
                lastStateNode.SubmachinePausableNodeEntered += HandleSubmachinePausableNodeEntered;
            }
        }

        /// <summary>
        /// Add a Continue Condition node to the end of the chain.  The continue constraint will be late-bound.
        /// It will not be generated until the node is entered.
        /// </summary>
        /// <param name="nodeName">The </param>
        /// <param name="metaData"></param>
        /// <param name="createConstraintDelegate"></param>
        /// <param name="triggerOverrides">Triggers to use for transitions out of this node in liu of the configured machine-wide triggers.</param>
        public void AddContinueCondition(string nodeName, dynamic metaData, Func<dynamic, IUmlConstraint> createConstraintDelegate, params IUmlTrigger[] triggerOverrides)
        {
            if (!_isEditable)
            {
                throw new ActivityMachineException("Assembly can only be done while the machine is in an edit mode.");
            }

            AddContinueCondition(null, nodeName, triggerOverrides);
            if (_lastCreatedNode is ConditionalNode)
            {
                (_lastCreatedNode as ConditionalNode).CreateContinueConstraintDelegate = createConstraintDelegate;
                (_lastCreatedNode as ConditionalNode).MetaData = metaData;
            }
        }

        public void AddPausableNode(string nodeName, int pauseNumber)
        {
            if (!_isEditable)
            {
                throw new ActivityMachineException("Assembly can only be done while the machine is in an edit mode.");
            }
            SupportsSelfPausing = true;

            // Resulting Continue Guard: (DefaultContinueConstraint)
            // Resulting Quit Guard:     (DefaultQuitConstraint)
            var pauseNode = new PausableNode(nodeName + pauseNumber, Name, this) { LogType = LogType, Index = pauseNumber };
            AddNode(pauseNode);
            OnPausableNodeAdded(pauseNode);
        }

        /// <summary>
        /// Pause a running machine. This override drills down into a sub-machine if there is one.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        public override void Pause()
        {
            var behavioralNode = CurrentNode as BehavioralNode;
            if (behavioralNode != null)
            {
                var doBehave = behavioralNode.DoBehavior as ISubmachineBehavior;
                if (doBehave != null && doBehave.Machine != null && doBehave.IsPaused)
                {
                    // This machine will be paused by an eventhandler listening on Paused event of the sub-machine.
                    doBehave.Machine.Pause();
                }
                else
                {
                    base.Pause();
                }
            }
            else
            {
                base.Pause();
            }
        }

        /// <summary>
        /// Resume a paused machine. This override drills down into a sub-machine if there is one.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        public override void Resume()
        {
            var behavioralNode = CurrentNode as BehavioralNode;
            if (behavioralNode != null)
            {
                var doBehave = behavioralNode.DoBehavior as ISubmachineBehavior;
                if (doBehave != null && doBehave.Machine != null && doBehave.IsPaused)
                {
                    // This machine will be unpaused by an eventhandler listening on Resumed event of the sub-machine.
                    doBehave.Machine.Resume();
                }
                else
                {
                    base.Resume();
                }
            }
            else
            {
                base.Resume();
            }
        }

        /// <summary>
        /// Sets a pausable node at the given one-relative index to pause or not pause this machine upon entry.
        /// </summary>
        /// <param name="nodeNumber">A 1-relative index for the node to toggle</param>
        /// <param name="pause">indicates whether to set the node to pause or not</param>
        public void PauseAtNode(int nodeNumber, bool pause)
        {
            if (!SupportsSelfPausing)
                return;

            var pausable = FindAllPausableNodes();
            if (pausable.ContainsKey(nodeNumber))
            {
                // This covers the easy case that the node to be paused is located in this machine.
                pausable[nodeNumber].PauseParentMachine = pause;
            }
            else if (IsPaused)
            {
                var behavioralNode = CurrentNode as BehavioralNode;
                ISubmachineBehavior doBehave = null;
                if (behavioralNode != null)
                {
                    doBehave = behavioralNode.DoBehavior as ISubmachineBehavior;
                }
                if (doBehave != null && doBehave.Machine is CommandExecutingMachine && doBehave.IsPaused)
                {
                    // This covers the case that we need to drill down into existing submachines to find the node to pause.
                    (doBehave.Machine as CommandExecutingMachine).PauseAtNode(nodeNumber, pause);
                }
                else if (Configuration.HasDataKey("InitialBreakpoints"))
                {
                    // This covers the case that the node to be paused will exist in a submachine, but hasn't been instantiated yet.
                    var breakpoints = Configuration.Data.InitialBreakpoints as List<int>;
                    if (pause)
                    {
                        if (!breakpoints.Contains(nodeNumber))
                        {
                            breakpoints.Add(nodeNumber);
                        }
                    }
                    else
                    {
                        breakpoints.Remove(nodeNumber);
                    }
                }
            }
        }

        public int GetCurrentLineNumber()
        {
            if (SupportsSelfPausing && IsPaused)
            {
                var pausableNode = CurrentNode as PausableNode;
                var behavioralNode = CurrentNode as BehavioralNode;
                if (pausableNode != null)
                {
                    return pausableNode.Index;
                }
                else if (behavioralNode != null)
                {
                    var doBehave = behavioralNode.DoBehavior as ISubmachineBehavior;
                    if (doBehave != null && doBehave.Machine is CommandExecutingMachine && doBehave.IsPaused)
                    {
                        return (doBehave.Machine as CommandExecutingMachine).GetCurrentLineNumber();
                    }
                }
            }
            return -1;
        }

        public void SetEnterBehaviorOnLastAddedNode(string name, Action<dynamic> entryBehavior, dynamic entryBehaviorTarget)
        {
            if (!_isEditable)
            {
                throw new ActivityMachineException("SetEnterBehaviorOnLastNode can only be done while the machine is in an edit mode.");
            }

            if (_lastCreatedNode is BehavioralNode)
            {
                (_lastCreatedNode as BehavioralNode).SetEnterBehavior(null, new DynamicNodeBehavior(name, entryBehavior, entryBehaviorTarget));
            }
        }

        internal Dictionary<int, PausableNode> FindAllPausableNodes()
        {
            var allPausable = new Dictionary<int, PausableNode>();
            FindAllPausableNodes(InitialNode, allPausable);
            return allPausable;
        }

        protected override void HandleNodeEntered(object sender, EventArgs args)
        {
            var pausableNode = sender as PausableNode;
            if (pausableNode == null)
                return;

            OnPausableNodeEntered(pausableNode.Index);
        }

        protected override bool CompleteExecution(string explanation, Exception causeOfFault = null)
        {
            Exception ex = null;
            var errorCode = 0;
            if (HasDataContextKey("ErrorLevel"))
            {
                errorCode = DataContext.ErrorLevel;

                //Prevent parent machines from detecting the same error code
                DataContext.ErrorLevel = 0;
            }
            var station = DataContext.Station as IStation;
            if (causeOfFault != null)
            {
                if (!(causeOfFault is AtlasException))
                {
                    ex = new AtlasException(errorCode, station, causeOfFault);
                    lock (_executionLock)
                    {
                        Fault = ex;
                    }
                }
                else
                {
                    ex = causeOfFault;
                }
            }
            else if (errorCode > 0)
            {
                ex = new AtlasException(errorCode, station);
                lock (_executionLock)
                {
                    Fault = ex;
                }

            }

            if (ex != null)
            {
                lock (_executionLock)
                {
                    _completionCause = CompletionCause.Faulted;
                }
            }
            return base.CompleteExecution(explanation, ex);
        }

        protected override void OnPaused(PausedEventArgs args)
        {
            var pausableNode = CurrentNode as PausableNode;
            if (args == null && pausableNode != null)
            {
                args = new PausedEventArgs();
                args.PausedActivityNumber = pausableNode.Index;
            }
            base.OnPaused(args);
        }

        /// <summary>
        /// Recursively finds all the pausable conditional nodes.
        /// </summary>
        /// <param name="currentNode">starting node</param>
        /// <param name="found">list of found nodes</param>
        private void FindAllPausableNodes(IUmlNode currentNode, Dictionary<int, PausableNode> found)
        {
            var current = currentNode as UmlNode;
            if (current != null)
            {
                foreach (var connector in current.Connectors)
                {
                    if (connector != null)
                    {
                        var conditionalNode = connector.Consumer as PausableNode;
                        if (conditionalNode != null && !found.ContainsValue(conditionalNode))
                        {
                            found.Add(conditionalNode.Index, conditionalNode);
                        }
                        FindAllPausableNodes(connector.Consumer, found);
                    }
                }
            }
        }

        private void HandleSubmachineDone(object sender, TimeStampedEventArgs args)
        {
            var submachineHost = sender as ISubmachineHost;
            if (submachineHost != null)
            {
                submachineHost.SubmachineDone -= HandleSubmachineDone;
                submachineHost.SubmachinePaused -= HandleSubmachinePaused;
                submachineHost.SubmachineResumed -= HandleSubmachineResumed;
                submachineHost.SubmachinePausableNodeEntered -= HandleSubmachinePausableNodeEntered;
            }

            // Try to exit the current node.
            RunToCompletion();
        }

        /// <summary>
        /// Pause this sub-machine host in response to a sub-machine being paused.  This must not
        /// disable connectors though because
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        private void HandleSubmachinePaused(object sender, SubmachineEventArgs args)
        {
            base.Pause();
        }

        private void HandleSubmachineResumed(object sender, SubmachineEventArgs args)
        {
            base.Resume();
        }

        private void HandleSubmachinePausableNodeEntered(object sender, PausedEventArgs args)
        {
            OnPausableNodeEntered(args.PausedActivityNumber);
        }

        private void OnPausableNodeAdded(PausableNode node)
        {
            if (PausableNodeAdded != null)
            {
                try
                {
                    PausableNodeAdded(this, new UmlNodeCreatedEventArgs() { Node = node });
                }
                catch (Exception e)
                {
                    LogService.Log(LogType, LogMessageType.Error, GetType().Name,
                                    e.GetType().Name + " while raising PausableNodeAdded event. (" + Name + "): " + e.Message, e);
                }
            }
        }

        private void OnPausableNodeEntered(int nodeindex)
        {
            if (PausableNodeEntered != null)
            {
                try
                {
                    PausableNodeEntered(this, new PausedEventArgs() { PausedActivityNumber = nodeindex });
                }
                catch (Exception e)
                {
                    LogService.Log(LogType, LogMessageType.Error, GetType().Name,
                                    e.GetType().Name + " while raising PausableNodeEntered event. (" + Name + "): " + e.Message, e);
                }
            }
        }

        private void InitializeComponents(DynamicConfiguration config)
        {
            HaltOnFault = true;
            Configuration = config;
            ThreadName = ExtractThreadName(config);
            // On the Started event, machine assembly has already transpired but execution hasn't actually started.  
            // Time to set any existing breakpoints on the pausable nodes.
            Started += ApplyPreexistingBreakpoints;

            var rootMachine = ExtractRootMachine(config.Data);
            if (DataContext != null && rootMachine != null)
            {
                DataContext.RootMachine = rootMachine;
            }

            // Give the dispatcher to the config so that any nested machine may use it.
            config.Data.Dispatcher = Dispatcher;
        }

        private void ApplyPreexistingBreakpoints(object sender, TimeStampedEventArgs args)
        {
            if (!SupportsSelfPausing || !Configuration.HasDataValue("InitialBreakpoints"))
            {
                return;
            }

            var initialBreakpoints = Configuration.Data.InitialBreakpoints as List<int>;
            foreach (var nodeNumber in initialBreakpoints)
            {
                PauseAtNode(nodeNumber, true);
            }
        }
    }
}
