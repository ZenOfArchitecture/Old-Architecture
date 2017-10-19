using System;
using System.Globalization;
using System.Threading;
using Ventana.Core.Base.Activities;
using Ventana.Core.Logging;
using Ventana.Core.Utilities;

namespace Ventana.Core.Activities.Parts
{
    public abstract class BehavioralNode : UmlNode
    {
        protected BehavioralNode(string name, string containerName)
            : base(name, containerName)
        {
        }

        public event EventHandler<FaultedEventArgs> Faulted;
        public event EventHandler<EventArgs> TimedOut;
        public event EventHandler<NodeEnteredEventArgs> EntryBehaviorsFinished;

        public override bool CanEnter
        {
            get { return base.CanEnter && IsPreconditionMet(); }
        }

        public override bool CanExit
        {
            get { return base.CanExit && IsPostconditionMet(); }
        }

        public virtual IUmlConstraint Precondition { get; protected set; }
        public virtual IUmlConstraint Postcondition { get; protected set; }
        public IExecutable EnterBehavior { get; private set; }
        public IExecutable DoBehavior { get; private set; }
        public IExecutable ExitBehavior { get; private set; }

        internal SimpleDispatcher Dispatcher { get; set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SetEnterBehavior(null, null);
                SetDoBehavior(null);
                SetExitBehavior(null, null);
            }

            base.Dispose(disposing);
        }

        public void SetDoBehavior(IExecutable doBehavior)
        {
            if (DoBehavior != null)
            {
                if (DoBehavior is IDisposable)
                {
                    (DoBehavior as IDisposable).Dispose();
                }
                SubscribeToBehavior(false, DoBehavior);
            }
            DoBehavior = doBehavior;
            SubscribeToBehavior(true, doBehavior);
        }

        public virtual void SetEnterBehavior(IUmlConstraint precondition, IExecutable enterBehavior)
        {
            if (EnterBehavior != null)
            {
                if (EnterBehavior is IDisposable)
                {
                    (EnterBehavior as IDisposable).Dispose();
                }
                SubscribeToBehavior(false, EnterBehavior);
            }
            if (precondition != null)
            {
                Precondition = precondition.Copy();
            }
            EnterBehavior = enterBehavior;
            SubscribeToBehavior(true, enterBehavior);
        }

        public void SetExitBehavior(IUmlConstraint postcondition, IExecutable exitBehavior)
        {
            if (ExitBehavior != null)
            {
                if (ExitBehavior is IDisposable)
                {
                    (ExitBehavior as IDisposable).Dispose();
                }
                SubscribeToBehavior(false, ExitBehavior);
            }
            if (postcondition != null)
            {
                Postcondition = postcondition.Copy();
            }
            ExitBehavior = exitBehavior;
            SubscribeToBehavior(true, exitBehavior);
        }

        /// <summary>
        /// Enter this Node from the given connector.  An attempt will be made to traverse
        /// all outbound connectors after the EnterBehavior and DoBehaviors are done.
        /// All behaviors are marshalled onto a SimpleDispatcher.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        protected override void InternalEnter()
        {
            base.InternalEnter();

            try
            {
                if (EnterBehavior != null)
                {
                    var msg = string.Format(CultureInfo.InvariantCulture, "Node '{0}' running ENTER behavior '{1}'.", Name, EnterBehavior.Name);
                    LogService.Log(LogType, LogMessageType.Debug, ContainerName, msg);

                    if (EnterBehavior is INodeBehavior)
                    {
                        (EnterBehavior as INodeBehavior).EntryOrigin = OriginOfEntry as IUmlConnector;
                        (EnterBehavior as INodeBehavior).EntryArgs = EntryArgs;
                    }
                    ExecuteExecutable(EnterBehavior);
                }

                if (DoBehavior != null)
                {
                    var msg = string.Format(CultureInfo.InvariantCulture, "Node '{0}' running DO behavior '{1}'.", Name, DoBehavior.Name);
                    LogService.Log(LogType, LogMessageType.Debug, ContainerName, msg);

                    if (DoBehavior is INodeBehavior)
                    {
                        (DoBehavior as INodeBehavior).EntryOrigin = OriginOfEntry as IUmlConnector;
                        (DoBehavior as INodeBehavior).EntryArgs = EntryArgs;
                    }
                    ExecuteExecutable(DoBehavior);
                }

                OnEntryBehaviorsFinished();
            }
            catch (Exception e)
            {
                OnFaulted(new FaultedEventArgs(e));
            }
        }

        /// <summary>
        /// Try to exit this node after CanExit has indicated exit is allowed..  This is synchronous.
        /// </summary>
        /// <returns>False if there was an exception, true otherwise.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        protected override bool InternalTryExit()
        {
            try
            {
                // If there is an exit behavior, run it now.
                if (ExitBehavior != null)
                {
                    var msg = string.Format(CultureInfo.InvariantCulture, "Node '{0}' running EXIT behavior '{1}'.", Name, ExitBehavior.Name);
                    LogService.Log(LogType, LogMessageType.Debug, ContainerName, msg);

                    if (ExitBehavior is INodeBehavior)
                    {
                        (ExitBehavior as INodeBehavior).EntryOrigin = OriginOfEntry as IUmlConnector;
                    }
                    ExecuteExecutable(ExitBehavior);
                }
                // Let base do administrative Exit tasks.
                return base.InternalTryExit();
            }
            catch (Exception e)
            {
                OnFaulted(new FaultedEventArgs(e));
            }

            return false;
        }

        /// <summary>
        /// Executes the given executable using a Dispatcher or the current thread.
        /// This is NOT a duplicate implementation of ActivityMachine.ExecuteExecutable,
        /// though it is close.
        /// </summary>
        /// <param name="executable">IExecutable</param>
        protected void ExecuteExecutable(IExecutable executable)
        {
            if (Dispatcher == null || Dispatcher.Equals(Thread.CurrentThread))
            {
                executable.Execute();
            }
            else
            {
                Dispatcher.Run(executable);
            }
        }

        protected bool IsPreconditionMet()
        {
            return Precondition == null || Precondition.IsTrue();
        }

        protected bool IsPostconditionMet()
        {
            return Postcondition == null || Postcondition.IsTrue();
        }

        private void SubscribeToBehavior(bool subscribe, IExecutable behavior)
        {
            if (behavior != null)
            {
                if (subscribe)
                {
                    behavior.Faulted += HandleBehaviorFaulted;
                    behavior.Expired += HandleBehaviorExpired;
                }
                else
                {
                    behavior.Faulted -= HandleBehaviorFaulted;
                    behavior.Expired -= HandleBehaviorExpired;
                }
            }
        }

        private void HandleBehaviorFaulted(object sender, FaultedEventArgs eventArgs)
        {
            SubscribeToBehavior(false, sender as IExecutable);
            OnFaulted(eventArgs);
        }

        private void HandleBehaviorExpired(object sender, EventArgs eventArgs)
        {
            SubscribeToBehavior(false, sender as IExecutable);
            OnTimedOut();
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        private void OnFaulted(FaultedEventArgs args)
        {
            try
            {
                var message = string.Format(CultureInfo.InvariantCulture, "Node '{0}' faulted: {1}", Name, args.Cause == null ? "cause unknown" : args.Cause.Message);
                LogService.Log(LogType, LogMessageType.Error, ContainerName, message, args.Cause);

                if (Faulted != null)
                {
                    Faulted(this, args);
                }
            }
            catch (Exception ex)
            {
                var message = string.Format(CultureInfo.InvariantCulture, "Node '{0}' encountered a {1} while raising the Faulted event", Name, ex.GetType().Name);
                LogService.Log(LogType, LogMessageType.Error, ContainerName, message, ex);
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        private void OnTimedOut()
        {
            try
            {
                LogService.Log(LogType, LogMessageType.Debug, ContainerName, "Node '" + Name + "' timed out.");
                if (TimedOut != null)
                {
                    TimedOut(this, null);
                }
            }
            catch (Exception ex)
            {
                var message = string.Format(CultureInfo.InvariantCulture, "Node '{0}' encountered a {1} while raising the Expired event", Name, ex.GetType().Name);
                LogService.Log(LogType, LogMessageType.Error, ContainerName, message, ex);
            }
        }

        /// <summary>
        /// Marshall the raising of EntryBehaviorsFinished event onto a SimpleDispatcher.
        /// </summary>
        private void OnEntryBehaviorsFinished()
        {
            if (EntryBehaviorsFinished != null)
            {
                EntryBehaviorsFinished(this, new NodeEnteredEventArgs(OriginOfEntry));
            }
        }
    }
}
