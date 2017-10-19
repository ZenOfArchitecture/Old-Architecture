using System;
using Ventana.Core.Activities.Parts;
using Ventana.Core.Base.Activities;

namespace Ventana.Core.Activities.SpecializedTriggers
{
    /// <summary>
    /// This trigger is fired upon quitting an 
    /// Activity Machine.
    /// </summary>
    public class QuitHandlingTrigger : UmlTrigger
    {
        public QuitHandlingTrigger(string name, ActivityMachine source, IUmlConstraint guard) 
            : base(name, source, guard)
        {
        }

        public QuitHandlingTrigger(string name, ActivityMachine source)
            : this(name, source, null)
        {
        }

        public new ActivityMachine Source
        {
            get { return base.Source as ActivityMachine; }
            private set { base.Source = value; }
        }

        public override IUmlTrigger Copy()
        {
            var copy = new QuitHandlingTrigger(Name, Source, Guard) { LogType = LogType };
            if (IsLive)
            {
                copy.Enable();
            }
            return copy;
        }

        protected override void Connect()
        {
            Source.Quitting += HandleMachineQuitting;
        }

        protected override void Disconnect()
        {
            Source.Quitting -= HandleMachineQuitting;
        }

        private void HandleMachineQuitting(object sender, EventArgs eventArgs)
        {
            Trip();
        }

        protected override void Dispose(bool disposing)
        {
            // Dispose managed resources
            if (disposing)
            {
                Disconnect();
                Source = null;
            }
            // release unmanaged memory
            base.Dispose(disposing);
        }
    }
}
