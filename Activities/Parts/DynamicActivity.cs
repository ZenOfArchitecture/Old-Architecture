using System;
using Ventana.Core.Activities.Executables;

namespace Ventana.Core.Activities.Parts
{
    public class DynamicActivity : AsyncTimedExecutable, IDisposable
    {
        public DynamicActivity(string name, Action action) : this(name)
        {
            Action = action;
        }

        protected DynamicActivity(string name) : base(name)
        {
        }

        protected Action Action { get; set; }

        /// <summary>
        /// Inherited from IExecutable
        /// </summary>
        public override void Execute()
        {
            try
            {
                OnExecutableStarted();
                StartTiming();
                RunAction();
                StopTiming();
                OnExecutableFinished();
            }
            catch (Exception e)
            {
                StopTiming();
                OnExecutableFaulted(e);
            }
        }

        /// <summary>
        /// Derived classes can override this to run the action in a unique way.
        /// </summary>
        protected virtual void RunAction()
        {
            // Execute the delegate.
            Action();
        }

        protected override void Dispose(bool disposing)
        {
            // Dispose managed resources
            if (disposing)
            {
                Action = null;
            }
            // release unmanaged memory
        }
    }
}
