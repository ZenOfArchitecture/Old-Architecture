using System;
using System.Collections.Generic;
using System.Linq;
using Ventana.Core.Activities.Parts;
using Ventana.Core.Base.Activities;

namespace Ventana.Core.Activities.SpecializedTriggers
{
    public class ExecutableFinishedTrigger : UmlTrigger
    {
        public ExecutableFinishedTrigger(string name, IExecutable source, IUmlConstraint guard) : base(name, source, guard)
        {
        }
        
        public new IExecutable Source
        {
            get { return base.Source as IExecutable; }
            private set { base.Source = value; }
        }

        public override IUmlTrigger Copy()
        {
            var copy = new ExecutableFinishedTrigger(Name, Source, Guard) { LogType = LogType };
            if (IsLive)
            {
                copy.Enable();
            }
            return copy;
        }

        protected override void Connect()
        {
            Source.Finished += HandleBehaviorFinished;
        }

        protected override void Disconnect()
        {
            Source.Finished -= HandleBehaviorFinished;
        }

        private void HandleBehaviorFinished(object sender, EventArgs args)
        {
            //TODO:
        }
    }
}
