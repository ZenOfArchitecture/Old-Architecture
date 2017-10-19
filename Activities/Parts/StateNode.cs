using System;
using System.Globalization;
using System.Linq;
using Microsoft.Win32;
using Ventana.Core.Base.Activities;
using Ventana.Core.Base.Common;
using Ventana.Core.Logging;

namespace Ventana.Core.Activities.Parts
{
    /// <summary>
    /// This class contains UML spec violations.  The order of connectors is relied
    /// upon for correct behavior.  See <see cref="EnableTransitionTriggers"/> method.
    /// </summary>
    public class StateNode : BehavioralNode, ISubmachineHost
    {
        public StateNode(string name, string containerName) : base(name, containerName)
        {
        }

        public StateNode(string name, string containerName, IExecutable onDo) : this(name, containerName)
        {
            SetDoBehavior(onDo);
        }

        public event EventHandler<SubmachineEventArgs> SubmachineDone;

        public event EventHandler<SubmachineEventArgs> SubmachineCreated;
        public event EventHandler<SubmachineEventArgs> SubmachinePaused;
        public event EventHandler<SubmachineEventArgs> SubmachineResumed;
        public event EventHandler<PausedEventArgs> SubmachinePausableNodeEntered;

        /// <summary>
        /// Tells whether this node's transition triggers have been enabled.
        /// </summary>
        public bool IsLive { get; protected set; }

        /// <summary>
        /// Tells whether this node can be exited.
        /// If this node has an <see cref="ISubmachineBehavior"/> DO behavior, 
        /// this only allows exit only when transition triggers are enabled, as denoted by <see cref="IsLive"/>.
        /// </summary>
        public override bool CanExit
        {
            get { return base.CanExit && (!(DoBehavior is ISubmachineBehavior) || IsLive); }
        }

        public UmlTransition TransitionTo(IUmlNode consumer, IUmlConstraint guard, Action effect, params IUmlTrigger[] triggers)
        {
            var transition = base.TransitionTo(consumer);
            transition.Guard = guard;
            if (effect != null)
            {
                transition.Effect = new DynamicActivity(effect.Method.Name, effect);
            }
            if (triggers != null)
            {
                foreach (var trigger in triggers)
                {
                    transition.UseTrigger(trigger);
                }
            }
            return transition;
        }

        public void SetEnterBehavior(IUmlConstraint precondition, Action stateAction)
        {
            SetEnterBehavior(precondition, new DynamicActivity(stateAction.Method.Name, stateAction));
        }

        public void SetEnterBehavior(IUmlConstraint precondition, Action<StateEnteredEventArgs> stateAction)
        {
            SetEnterBehavior(precondition, new StateBehavior(stateAction.Method.Name, stateAction));
        }

        public void SetDoBehavior(Action stateAction)
        {
            SetDoBehavior(new DynamicActivity(stateAction.Method.Name, stateAction));
        }

        public void SetDoBehavior(Action<StateEnteredEventArgs> stateAction)
        {
            SetDoBehavior(new StateBehavior(stateAction.Method.Name, stateAction));
        }

        /// <summary>
        /// Find this node's finish transition.
        /// Encapsulates a UML spec violation where transition/trigger order matters.
        /// </summary>
        /// <returns></returns>
        public UmlTransition FindFinishTransition()
        {
            if (Connectors.Count < 1)
            {
                return null;
            }
            return Connectors[0] as UmlTransition;
        }

        /// <summary>
        /// Find this node's quit transition, if it has one.
        /// Encapsulates a UML spec violation where transition/trigger order matters.
        /// </summary>
        /// <returns></returns>
        public UmlTransition FindQuitTransition()
        {
            if (Connectors.Count == 3)
            {
                return Connectors[1] as UmlTransition;
            }
            // It's a quit transition if its consumer is the same as the finish transition's consumer.
            if (Connectors.Count == 2 && Connectors[0].Consumer == Connectors[1].Consumer)
            {
                return Connectors[1] as UmlTransition;
            }
            return null;
        }

        /// <summary>
        /// Find this node's continue transition.
        /// Encapsulates a UML spec violation where transition/trigger order matters.
        /// </summary>
        /// <returns></returns>
        public UmlTransition FindContinueTransition()
        {
            if (Connectors.Count == 3)
            {
                return Connectors[2] as UmlTransition;
            }
            // It's a continue transition if its consumer is NOT the same as the finish transition's consumer.
            if (Connectors.Count == 2 && Connectors[0].Consumer != Connectors[1].Consumer)
            {
                return Connectors[1] as UmlTransition;
            }
            return null;
        }

        /// <summary>
        /// Enables the triggers of all outgoing transitions.
        /// This method contains a violation of the UML spec for transitions/triggers.
        /// It's behavior relies upon the order of connectors.
        /// </summary>
        internal override void EnableConnectors()
        {
            // This override causes delayed enabling of triggers if the DO behavior is a sub-machine.
            if (DoBehavior is ISubmachineBehavior)
            {
                SubscribeToDoSubmachine(true);
            }
            else
            {
                EnableTransitionTriggers();
            }
        }

        /// <summary>
        /// Disables the triggers of all outgoing transitions.
        /// </summary>
        internal override void DisableConnectors()
        {
            if (DoBehavior is ISubmachineBehavior)
            {
                SubscribeToDoSubmachine(false);
            }
            else if (IsLive)
            {
                IsLive = false;
                base.DisableConnectors();
                foreach (var connector in Connectors.OfType<UmlTransition>())
                {
                    connector.DisableTriggers();
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            SubscribeToDoSubmachine(false);
            SubscribeToSubmachineBehavior(false, DoBehavior as ISubmachineBehavior);

            base.Dispose(disposing);
        }

        protected void EnableTransitionTriggers()
        {
            if (IsLive)
            {
                return;
            }

            IsLive = true;
            base.EnableConnectors();
            foreach (var transition in Connectors.OfType<UmlTransition>())
            {
                transition.EnableTriggers();
            }
        }

        private void SubscribeToDoSubmachine(bool subscribe)
        {
            var submachineBehavior = DoBehavior as ISubmachineBehavior;
            if (submachineBehavior != null)
            {
                if (subscribe)
                {
                    submachineBehavior.SubmachineCreated += HandleSubmachineCreated;
                    submachineBehavior.SubmachineDone += HandleSubmachineDone;
                }
                else
                {
                    submachineBehavior.SubmachineCreated -= HandleSubmachineCreated;
                    submachineBehavior.SubmachineDone -= HandleSubmachineDone;
                }
            }
        }

        private void SubscribeToSubmachineBehavior(bool subscribe, ISubmachineBehavior behavior)
        {
            if (behavior != null)
            {
                if (subscribe)
                {
                    behavior.SubmachinePaused += HandleSubmachinePaused;
                    behavior.SubmachineResumed += HandleSubmachineResumed;
                    behavior.SubmachinePausableNodeEntered += HandleSubmachinePausableNodeEntered;
                }
                else
                {
                    behavior.SubmachinePaused -= HandleSubmachinePaused;
                    behavior.SubmachineResumed -= HandleSubmachineResumed;
                    behavior.SubmachinePausableNodeEntered -= HandleSubmachinePausableNodeEntered;
                }
            }
        }

        private void OnSubmachineDone(IActivityMachine machine)
        {
            if (SubmachineDone != null)
            {
                SubmachineDone(this, new SubmachineEventArgs() { Machine = machine });
            }
        }

        /// <summary>
        /// Responds to the <see cref="ISubmachineHost.SubmachineDone"/> event.
        /// If this node is not yet active, it will be made active with enabled triggers.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        private void HandleSubmachineDone(object sender, SubmachineEventArgs args)
        {
            var activity = DoBehavior as ISubmachineBehavior;
            SubscribeToDoSubmachine(false);
            SubscribeToSubmachineBehavior(false, activity);

            if (!activity.IsFinished)
            {
                LogService.Log(LogType, LogMessageType.Debug, "StateNode " + Name,
                    string.Format(CultureInfo.InvariantCulture, "HandleSubmachineDone: activity {0} {1}.", Name, activity.IsFaulted ? "IsFaulted" : activity.IsInterrupted ? "IsInterrupted" : "IsExpired"));
            }
            if (IsActive && !IsLive)
            {
                LogService.Log(LogType, LogMessageType.Debug, "StateNode " + Name,
                  string.Format(CultureInfo.InvariantCulture, "HandleSubmachineDone: enabling '{0}' transition triggers.", Name));
                EnableTransitionTriggers();

                OnSubmachineDone(args.Machine);
            }
        }

        private void HandleSubmachineCreated(object sender, SubmachineEventArgs args)
        {
            SubscribeToSubmachineBehavior(true, DoBehavior as ISubmachineBehavior);

            if (SubmachineCreated != null)
            {
                SubmachineCreated(this, args);
            }
        }

        private void HandleSubmachinePaused(object sender, SubmachineEventArgs args)
        {
            if (SubmachinePaused != null)
            {
                SubmachinePaused(this, args);
            }
        }

        private void HandleSubmachineResumed(object sender, SubmachineEventArgs args)
        {
            if (SubmachineResumed != null)
            {
                SubmachineResumed(this, args);
            }
        }

        private void HandleSubmachinePausableNodeEntered(object sender, PausedEventArgs args)
        {
            if (SubmachinePausableNodeEntered != null)
            {
                SubmachinePausableNodeEntered(this, args);
            }
        }
    }
}
