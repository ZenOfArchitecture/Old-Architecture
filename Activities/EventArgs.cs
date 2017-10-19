using System;
using Ventana.Core.Base;
using Ventana.Core.Base.Activities;
using Ventana.Core.Base.Common;
using Ventana.Core.Base.Transportation;

namespace Ventana.Core.Activities
{
    public class TransportOperationEventArgs : EventArgs
    {
        public TransportOperation Operation { get; set; }
    }

    public class UmlNodeCreatedEventArgs : EventArgs
    {
        public IUmlNode Node { get; set; }
    }

    public class ActivityMachineEventArgs : TimeStampedEventArgs
    {
        public ExecutableState ExecutableState { get; set; }
    }
}
