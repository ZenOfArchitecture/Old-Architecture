using System;
using System.Globalization;
using Ventana.Core.Base.Activities;
using Ventana.Core.Logging;

namespace Ventana.Core.Activities.Parts
{
    /// <summary>
    /// This class realizes a UML Trigger.  It departs from the UML spec in that
    /// it has an optional Guard that is checked before being triggered.  Said Guard
    /// does not otherwise participate in the conditional firing of its parent Transition.
    /// </summary>
    public class UmlTrigger : IUmlTrigger
    {
        public UmlTrigger()
        {
            LogType = LogType.System;
        }

        public event EventHandler<TriggerEventArgs> Tripped;

        public string Name { get; private set; }
        public LogType LogType { get; set; }
        public object Source { get; protected set; }
        public bool IsLive { get; private set; }
        public virtual IUmlConstraint Guard { get; private set; }

        public UmlTrigger(string name, object source, IUmlConstraint guard)
            : this(name)
        {
            Source = source;
            Guard = guard;
        }

        protected UmlTrigger(string name)
        {
            Name = name;
            IsLive = false;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            // Dispose managed resources

            // release unmanaged memory
        }

        public void Enable()
        {
            if (!IsLive)
            {
                IsLive = true;
                Connect();
            }
        }

        public void Disable()
        {
            if (IsLive)
            {
                IsLive = false;
                Disconnect();
            }
        }

        /// <summary>
        /// Please override this in your base class.  Copies are used by
        /// DynamicActivityMachine.
        /// </summary>
        /// <returns></returns>
        public virtual IUmlTrigger Copy()
        {
            return new UmlTrigger(Name, Source, Guard) { LogType = LogType };
        }

        protected virtual void Connect()
        {
            // Empty base impl.
        }

        protected virtual void Disconnect()
        {
            // Empty base impl.
        }

        public void Trip(IExecutionContext executionContext = null)
        {
            if (IsLive && (Guard == null || Guard.IsTrue()))
            {
                OnTripped(executionContext);
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        private void OnTripped(IExecutionContext executionContext)
        {
            if (Tripped != null)
            {
                try
                {
                    Tripped(this, new TriggerEventArgs() { ExecutionContext = executionContext });
                }
                catch (Exception e)
                {
                    LogService.Log(LogType, LogMessageType.Error, Name,
                        string.Format(CultureInfo.InvariantCulture, "UmlTrigger exception while raising Tripped event: {0}", e.Message), e);
                }
            }
        }
    }
}
