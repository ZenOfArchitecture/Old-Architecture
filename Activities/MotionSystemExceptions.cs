using System;
using Ventana.Core.Base.BusinessObjects;
using Ventana.Core.Base.Transportation;
using Ventana.Core.ExceptionHandling;

namespace Ventana.Core.Activities
{
    public class TrayMoveInternalException : AtlasException
    {
        public TrayMoveInternalException(string message, ITransport transport, string stationName, string operation, string cause)
            : base(1101, null, message, transport as IStation, stationName, operation, cause)
        { }

        public TrayMoveInternalException(string message, Exception ex, ITransport transport, string stationName, string operation, string cause)
            : base(1101, ex, message, transport as IStation, stationName, operation, cause)
        { }
    }

    public class TrayMoveComponentException : AtlasException
    {
        public TrayMoveComponentException(string message, ITransport transport, string stationName, string operation)
            : base(1102, null, message, transport as IStation, stationName, operation)
        { }

        public TrayMoveComponentException(string message, Exception ex, ITransport transport, string stationName, string operation)
            : base(1102, ex, message, transport as IStation, stationName, operation)
        { }
    }

    public class TrayMoveExpirationException : AtlasException
    {
        public TrayMoveExpirationException(string message, ITransport transport, string stationName, string operation)
            : base(1103, null, message, transport as IStation, stationName, operation)
        {}
    }
}
