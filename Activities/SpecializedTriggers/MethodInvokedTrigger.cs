using System;
using SimpleMvvmToolkit.VentanaExtensions;
using Ventana.Core.Activities.Parts;
using Ventana.Core.Base.Activities;

namespace Ventana.Core.Activities.SpecializedTriggers
{
    public class MethodInvokedTrigger : UmlTrigger
    {
        public MethodInvokedTrigger(string name, INotifyMethodInvoked source, IUmlConstraint guard, string tripOnMethodName, object invocationContextFilter)
            : base(name, source, guard)
        {
            MethodNameFilter = tripOnMethodName ?? string.Empty;
            InvocationContextFilter = invocationContextFilter;
        }

        public MethodInvokedTrigger(string name, INotifyMethodInvoked source, string tripOnMethodName, object invocationContextFilter)
            : this(name, source, null, tripOnMethodName, invocationContextFilter)
        {
        }

        public MethodInvokedTrigger(string name, INotifyMethodInvoked source, string tripOnMethodName)
            : this(name, source, tripOnMethodName, null)
        {
        }

        /// <summary>
        /// Limit trigger tripping by filtering against the name of the invoked method.
        /// </summary>
        public string MethodNameFilter { get; private set; }

        /// <summary>
        /// Limit trigger tripping by filtering against the method invocation's contextual identifier.
        /// </summary>
        public object InvocationContextFilter { get; private set; }

        public new INotifyMethodInvoked Source
        {
            get { return base.Source as INotifyMethodInvoked; }
            private set { base.Source = value; }
        }

        public override IUmlTrigger Copy()
        {
            var copy = new MethodInvokedTrigger(Name, Source, Guard, MethodNameFilter, InvocationContextFilter) { LogType = LogType };
            if (IsLive)
            {
                copy.Enable();
            }
            return copy;
        }

        protected override void Connect()
        {
            Source.MethodInvoked += HandleSourceMethodInvoked;
        }

        protected override void Disconnect()
        {
            Source.MethodInvoked -= HandleSourceMethodInvoked;
        }

        private void HandleSourceMethodInvoked(object sender, MethodInvokedEventArgs args)
        {
            if ((string.IsNullOrEmpty(MethodNameFilter) || MethodNameFilter.Equals(args.MethodName))
                && (InvocationContextFilter == null || InvocationContextFilter.Equals(args.InvocationContext.Identifier)))
            {
                Trip(args.InvocationContext == null ? null : args.InvocationContext.Data as IExecutionContext);
            }
        }
    }
}
