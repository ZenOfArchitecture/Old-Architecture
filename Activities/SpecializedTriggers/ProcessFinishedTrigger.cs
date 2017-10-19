using System;
using Ventana.Core.Activities.Parts;
using Ventana.Core.Base.Activities;
using Ventana.Core.Base.BusinessObjects;

namespace Ventana.Core.Activities.SpecializedTriggers
{
    public class ProcessFinishedTrigger : UmlTrigger
    {
        public ProcessFinishedTrigger(string name, IProcessExecuter source, IUmlConstraint guard)
            : base(name, source, guard)
        {
        }

        public new IProcessExecuter Source
        {
            get { return base.Source as IProcessExecuter; }
            private set { base.Source = value; }
        }

        public override IUmlTrigger Copy()
        {
            var copy = new ProcessFinishedTrigger(Name, Source, Guard) { LogType = LogType };
            if (IsLive)
            {
                copy.Enable();
            }
            return copy;
        }

        protected override void Connect()
        {
            Source.ProcessFailed += HandleBehaviorFinished;
            Source.ProcessSucceeded += HandleBehaviorFinished;
        }

        protected override void Disconnect()
        {
            Source.ProcessFailed -= HandleBehaviorFinished;
            Source.ProcessSucceeded -= HandleBehaviorFinished;
        }

        private void HandleBehaviorFinished(object sender, EventArgs args)
        {
            Trip();
        }
    }
}
