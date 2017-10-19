using System;
using Ventana.Core.Activities.Parts.Generic;
using Ventana.Core.Base.Activities;

namespace Ventana.Core.Activities.Parts
{
    public class StateBehavior : DynamicActivity<StateEnteredEventArgs>, INodeBehavior
    {
        public StateBehavior(string name, Action<StateEnteredEventArgs> action) : base(name, action)
        {
        }

        public IUmlConnector EntryOrigin { get; set; }
        public TransitionEventArgs EntryArgs { get; set; }

        /// <summary>
        /// Derived classes can override this to run the action in a unique way.
        /// </summary>
        protected override void RunAction()
        {
            if (EntryOrigin != null && EntryOrigin.Supplier is StateNode)
            {
                Argument = new StateEnteredEventArgs((EntryOrigin.Supplier as StateNode).Name) { ConnectorArgs = EntryArgs };
            }
            base.RunAction();
        }
    }
}
