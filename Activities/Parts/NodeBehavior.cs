using System;
using Ventana.Core.Activities.Parts.Generic;
using Ventana.Core.Base.Activities;

namespace Ventana.Core.Activities.Parts
{
    public class NodeBehavior : DynamicActivity<string>, INodeBehavior
    {
        public NodeBehavior(string name, Action<string> action) : base(name, action)
        {
        }

        public IUmlConnector EntryOrigin { get; set; }
        public TransitionEventArgs EntryArgs { get; set; }

        /// <summary>
        /// Derived classes can override this to run the action in a unique way.
        /// </summary>
        protected override void RunAction()
        {
            if (EntryOrigin.Guard != null)
            {
                Argument = EntryOrigin.Guard.Name;
            }
            base.RunAction();
        }
    }
}
