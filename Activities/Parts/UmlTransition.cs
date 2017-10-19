using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Ventana.Core.Base.Activities;
using Ventana.Core.Logging;
using Ventana.Core.Utilities;

namespace Ventana.Core.Activities.Parts
{
    public class UmlTransition : IUmlConnector
    {
        private readonly object IsDisposedLock = new Object();
        private string _effectName = string.Empty;
        private IExecutable _effect;
        public bool IsDisposed { get; private set; }

        public UmlTransition(string containerName, IUmlNode supplier)
            : this(containerName, supplier, null)
        {
        }

        public UmlTransition(string containerName, IUmlNode supplier, IUmlNode consumer)
            : this(containerName, supplier, consumer, null)
        {
        }

        public UmlTransition(string containerName, IUmlNode supplier, IUmlNode consumer, IUmlConstraint guard)
        {
            ContainerName = containerName;
            Triggers = new List<IUmlTrigger>();
            Supplier = supplier;
            SupplierName = supplier.Name;
            Consumer = consumer;
            ConsumerName = consumer.Name;
            Guard = guard;
            LogType = LogType;
        }

        public event EventHandler<EventArgs> Traversed;
        public event EventHandler<TransitionEventArgs> Fired;

        public List<IUmlTrigger> Triggers { get; private set; }
        public IUmlConstraint Guard { get; set; }
        public IUmlNode Supplier { get; private set; }
        public IUmlNode Consumer { get; set; }

        public IExecutable Effect
        {
            get { return _effect; }
            set
            {
                _effect = value;
                if (_effect != null)
                {
                    _effectName = _effect.Name;
                }
            }
        }

        /// <summary>
        /// Indicates whether this connector will succeed if <see cref="Traverse"/> is called.
        /// </summary>
        public bool CanTraverse
        {
            get
            {
                bool result = Supplier != null && Consumer != null && (Guard == null || Guard.IsTrue());
                return result;
            }
        }
        private string ContainerName { get; set; }
        public LogType LogType { get; set; }

        internal SimpleDispatcher Dispatcher { get; set; }

        public string SupplierName { get; private set; }

        public string ConsumerName { get; private set; }

        internal uint DispatchPriority { get; set; }

        public void Dispose()
        {
            lock (IsDisposedLock)
            {
                if (IsDisposed) return;
                IsDisposed = true;
            }
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var trigger in Triggers)
                {
                    trigger.Tripped -= HandleTriggerTripped;
                    trigger.Dispose();
                }
                Triggers.Clear();

                //Nullify
                Triggers = null;
                Guard = null;
                Supplier = null;
                Consumer = null;
                Effect = null;
            }
        }

        public void EnableTriggers()
        {
            foreach (var trigger in Triggers)
            {
                trigger.Enable();
            }
        }

        public void DisableTriggers()
        {
            foreach (var trigger in Triggers)
            {
                trigger.Disable();
            }
        }

        /// <summary>
        /// Try to traverse this connector.  On success, the EFFECT behavior will occur
        /// and the consuming node of this connector will be entered.
        /// All events and behaviors are marshalled onto a SimpleDispatcher.
        /// </summary>
        /// <param name="args">TransitionEventArgs related to the attempt to traverse this Connector</param>
        /// <returns>true on traversal succeeded</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        public bool Traverse(TransitionEventArgs args)
        {
            if (CanTraverse && Consumer.CanEnter)
            {
                try
                {
                    // Perform any effect behavior.  It's okay to do effect here, even if it effects the
                    // consumer node, because CanEnter was already affirmed and it won't be checked again.
                    DoEffect();
                    // Indicate connector traversal.
                    OnTraversed();
                    // Enter the consuming node.
                    Consumer.EnterFrom(this, args);
                    return true;
                }
                catch (Exception e)
                {
                    LogService.Log(LogType, LogMessageType.Error, ContainerName, string.Format(CultureInfo.InvariantCulture, "Exception while traversing transition '{0}'->'{1}'", SupplierName, ConsumerName), e);
                }
            }
            else
            {
                LogService.Log(LogType, LogMessageType.Trace, ContainerName,
                    string.Format(CultureInfo.InvariantCulture,"Cannot traverse transition '{0}'->'{1}': Guard not satisfied or cannot enter consumer.", SupplierName, ConsumerName));
            }
            return false;
        }

        /// <summary>
        /// This no longer clones the incoming trigger. If your trigger will be reused on other
        /// transitions, be sure to feed a copy in rather than the original instance.
        /// </summary>
        /// <param name="trigger"></param>
        public void UseTrigger(IUmlTrigger trigger)
        {
            if (trigger == null)
                return;

            Triggers.Add(trigger);
            trigger.Tripped += HandleTriggerTripped;
        }

        /// <summary>
        /// Marshalls DoEffect behavior to a SimpleDispatcher
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        private void DoEffect()
        {
            if (Effect != null)
            {
                try
                {
                    var msg = string.Format(CultureInfo.InvariantCulture, "Transition '{0}'->'{1}' running EFFECT behavior '{2}'.", Supplier.Name, Consumer.Name, Effect.Name);
                    LogService.Log(LogType, LogMessageType.Trace, ContainerName, msg);

                    ExecuteExecutable(Effect);
                }
                catch (Exception e)
                {
                    LogService.Log(LogType, LogMessageType.Error, ContainerName,
                        string.Format(CultureInfo.InvariantCulture, "Exception in transition '{0}'->'{1}' for DoEffect behavior {2}.", SupplierName, ConsumerName, _effectName), e);
                }
            }
        }

        /// <summary>
        /// Raise the base connector's Traversed event to indicate that this transition fired.
        /// To be called after the transition Effect occurs and before the consumer node is entered.
        /// Work is marshalled onto a SimpleDispatcher.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        protected void OnTraversed()
        {
            var msg = string.Format(CultureInfo.InvariantCulture, "Transition '{0}'->'{1}' traversed.", Supplier.Name, Consumer.Name);
            LogService.Log(LogType, LogMessageType.Trace, ContainerName, msg);
            if (Traversed != null)
            {
                try
                {
                    Traversed(this, null);
                }
                catch (Exception e)
                {
                    LogService.Log(LogType, LogMessageType.Error, ContainerName,
                        string.Format(CultureInfo.InvariantCulture, "Exception in transition '{0}'->'{1}' while raising Traversed event..", SupplierName, ConsumerName), e);
                }
            }
        }

        /// <summary>
        /// Raise the Fired event to indicate that this transition went off.
        /// Work is marshalled onto a SimpleDispatcher.
        /// </summary>
        /// <param name="args">TransitionEventArgs related to the fired trigger</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        private void OnFired(TransitionEventArgs args)
        {
            if (Fired != null && args != null && args.Trigger != null)
            {
                try
                {
                    var msg = string.Format(CultureInfo.InvariantCulture, "Transition '{0}'->'{1}' fired from trigger '{2}'.",
                        SupplierName, ConsumerName, (args.Trigger != null) ? args.Trigger.Name : "?");
                    LogService.Log(LogType, LogMessageType.Trace, ContainerName, msg);
                    Fired(this, args);
                }
                catch (Exception e)
                {
                    LogService.Log(LogType, LogMessageType.Error, ContainerName,
                        string.Format(CultureInfo.InvariantCulture, "Exception while firing transition '{0}'->'{1}'.", SupplierName, ConsumerName), e);
                }
            }
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

        private void HandleTriggerTripped(object source, TriggerEventArgs args)
        {
            if (CanTraverse)
            {
                // Indicate transition fired.
                OnFired(new TransitionEventArgs(SupplierName, source as IUmlTrigger, args.ExecutionContext));
            }
        }
    }
}
