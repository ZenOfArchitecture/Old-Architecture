using Ventana.Core.Activities.Parts;
using Ventana.Core.Activities.Parts.Generic;
using Ventana.Core.Base.Activities;

namespace Ventana.Core.Activities.SpecializedTriggers
{
    /// <summary>
    /// A trigger that listens for the StateChanged event from a state machine.
    /// StateChanged is raised after the state's Enter & Do behaviors are done.
    /// </summary>
    /// <typeparam name="TState"></typeparam>
    public class StateChangedTrigger<TState> : UmlTrigger where TState : struct
    {
        public StateChangedTrigger(string name, StateMachine source, IUmlConstraint guard, TState? tripOnState)
            : base(name, source, guard)
        {
            State = tripOnState;
        }

        public StateChangedTrigger(string name, StateMachine source, TState? tripOnState)
            : this(name, source, null, tripOnState)
        {
        }

        public StateChangedTrigger(string name, StateMachine source)
            : this(name, source, null, null)
        {
        }

        public TState? State { get; private set; }

        public override IUmlTrigger Copy()
        {
            return new StateChangedTrigger<TState>(Name, Source, Guard, State) { LogType = LogType };
        }

        public new StateMachine<TState> Source
        {
            get { return base.Source as StateMachine<TState>; }
            private set { base.Source = value; }
        }

        protected override void Connect()
        {
            Source.StateChanged += HandleSourceStateChanged;
        }

        protected override void Disconnect()
        {
            Source.StateChanged -= HandleSourceStateChanged;
        }

        private void HandleSourceStateChanged(object sender, StateChangedEventArgs args)
        {
            var genericArgs = args as StateChangedEventArgs<TState>;
            if (State == null || genericArgs.NewState.Equals((TState)State))
            {
                Trip();
            }
        }
    }
}
