using System;
using System.Windows;
using Ventana.Core.Activities.Parts;
using Ventana.Core.Base.Activities;

namespace Ventana.Core.Activities.SpecializedTriggers
{
    /// <summary>
    /// This trigger is fired upon quitting an 
    /// Activity Machine.
    /// </summary>
    public class EventInvokedTrigger : UmlTrigger
    {

        public EventInvokedTrigger(string name, ActivityMachine source, EventHandler bindingEvent, object eventTarget)
            : base(name, source, null)
        {
            BindingEvent = bindingEvent;
        }

        public EventInvokedTrigger(string name, ActivityMachine source, IUmlConstraint constraint, EventHandler bindingEvent)
            : base(name, source, constraint)
        {
            BindingEvent = bindingEvent;
        }


        public new ActivityMachine Source
        {
            get { return base.Source as ActivityMachine; }
            private set { base.Source = value; }
        }

        public override IUmlTrigger Copy()
        {
            var copy = new EventInvokedTrigger(Name, Source, Guard, BindingEvent);
            {
                {
                    LogType = LogType;
                }
            }

            if (IsLive)
            {
                copy.Enable();
            }
            return copy;
        }

        public EventHandler BindingEvent { get; protected set; }

        /// <summary>
        /// the object the event requires to perform the delgated operation
        /// </summary>
        public Object BindingEventTarget { get; protected set; }

        protected override void Connect()
        {
            //this will only be valid as long
            //as the event is wired to the object
            if (BindingEvent != null && BindingEventTarget != null)
            {
               BindingEvent += HandleTargetBoundEvent;
            }
        }

        protected override void Disconnect()
        {
            //this will only be valid as long
            //as the event is wired to the object
            if (BindingEvent != null)
            {
                BindingEvent -= HandleTargetBoundEvent;
            }
        }

        private void HandleTargetBoundEvent(object sender, EventArgs eventArgs)
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
