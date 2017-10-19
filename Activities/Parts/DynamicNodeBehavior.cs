using System;
using Ventana.Core.Activities.Parts.Generic;
using Ventana.Core.Base.Activities;

namespace Ventana.Core.Activities.Parts
{
    public class DynamicNodeBehavior : DynamicActivity<dynamic>, INodeBehavior
    {
        public DynamicNodeBehavior(string name, Action<dynamic> action, dynamic target)
            : base(name, action)
        {
            Argument = target;
        }

        public IUmlConnector EntryOrigin { get; set; }
        public TransitionEventArgs EntryArgs { get; set; }
    }
}
