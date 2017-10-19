using System;
using System.Globalization;
using Ventana.Core.Base.Activities;
using Ventana.Core.ExceptionHandling;
using Ventana.Core.Logging;

namespace Ventana.Core.Activities.Builders
{
    public abstract class ActivityMachineBuilder : IActivityMachineBuilder
    {
        public abstract void Build(IActivityMachine machine);

        protected static Action GetFailAction(string name)
        {
            return () => { throw new Exception(string.Format(CultureInfo.InvariantCulture, "{0} Command Failed", name)); };
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        protected Action HandleFailedAction(string name, AtlasException ex, bool handle = true)
        {
            return () =>
            {
                if (handle)
                {
                    ex.Handle();
                }
                LogService.Log(LogType.System, LogMessageType.Error, GetType().Name,
                               string.Format(CultureInfo.InvariantCulture, "{0} Command Failed", name));
                throw ex;
            };
        }

    }
}
