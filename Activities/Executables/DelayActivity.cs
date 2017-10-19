using System;
using System.Collections.Generic;
using System.Globalization;
using Ventana.Core.Activities.Parts;
using Ventana.Core.Base.Common;
using Ventana.Core.Logging;

namespace Ventana.Core.Activities.Executables
{
    public class DelayActivity : TimedExecutable
    {
        public DelayActivity(string name, int timeoutMillis, IDictionary<string, object> variables, bool expireSilently = true)
            : base(name, timeoutMillis, expireSilently)
        {
            Action = null;
            Variables = variables;
        }

        public DelayActivity(string name, Func<IDictionary<string, object>, int> action, IDictionary<string, object> variables, bool expireSilently = true)
            : base(name, -1, expireSilently)
        {
            Action = action;
            Variables = variables;
        }

        protected IDictionary<string, object> Variables { get; set; }

        protected Func<IDictionary<string, object>, int> Action { get; set; }

        protected override void RunAction()
        {
            if (Action != null)
            {
                // Execute the delegate that returns the milliseconds to delay.
                TimeoutMilliseconds = Action(Variables);
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        public void HandleMachineQuitting(object sender, TimeStampedEventArgs args)
        {
            var quitter = sender as ActivityMachine;
            if (quitter != null)
            {
                quitter.Quitting -= HandleMachineQuitting;
            }

            // Nothing to do if it's already disposed
            if (_internalWaitEvent != null)
            {
                LogService.Log(LogType, LogMessageType.Trace, GetType().Name,
                    String.Format(CultureInfo.InvariantCulture, "Signaling {0} to exit delay because parent machine has quit.", Name));
                Continue();
            }
        }
    }
}