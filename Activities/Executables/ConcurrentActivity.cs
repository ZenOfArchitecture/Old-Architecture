using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Ventana.Core.Activities.Parts;
using Ventana.Core.Logging;

namespace Ventana.Core.Activities.Executables
{
    public class ConcurrentActivity : DynamicActivity
    {
        private Task _task;

        public ConcurrentActivity(string name, string executionDescription, Action<CancellationToken> action)
            : this(name, executionDescription)
        {
            Action = action;
        }

        protected ConcurrentActivity(string name, string executionDescription)
            : base(name)
        {
            ExecutionDescription = executionDescription;
            CancellationSource = new CancellationTokenSource();
            _task = new Task(ConcurrentAction, CancellationSource.Token);
            _task.ContinueWith(OnTaskFaulted, TaskContinuationOptions.OnlyOnFaulted);
        }

        public string ExecutionDescription { get; set; }

        public CancellationToken CancellationToken { get { return CancellationSource.Token; } }

        protected new Action<CancellationToken> Action { get; set; }

        private CancellationTokenSource CancellationSource { get; set; }

        public override void Execute()
        {
            // The task's run delegate is the base activity's execute method,
            // which calls OnStarted and OnCompleted when appropriate.
            _task.Start();
        }

        protected override void RunAction()
        {
            Action(CancellationToken);
        }

        protected override void HandleExpirationTimerElapsed(object sender, ElapsedEventArgs args)
        {
            base.HandleExpirationTimerElapsed(sender, args);

            CancellationSource.Cancel();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                CancellationSource.Cancel();
                _task = null;
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        private void ConcurrentAction()
        {
            LogService.Log(LogType.System, LogMessageType.Debug, GetType().Name, "Beginning concurrent action");
            base.Execute();
            LogService.Log(LogType.System, LogMessageType.Debug, GetType().Name, "Finished concurrent action");
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        private void OnTaskFaulted(Task task)
        {
            var cause = _task.Exception;
            OnExecutableFaulted(cause);

            if (cause != null)
            {
                foreach (var inner in cause.InnerExceptions)
                {
                    var msg = string.Format(CultureInfo.InvariantCulture, "{0} while running a concurrent activity. ({1})", inner.GetType().Name, Name);
                    LogService.Log(LogType.System, LogMessageType.Error, ExecutionDescription, msg, inner);
                }
            }
        }
    }
}
