using System;
using Ventana.Core.Activities.Machines;
using Ventana.Core.Base;
using Ventana.Core.Base.Activities;
using Ventana.Core.Base.BusinessObjects;
using Ventana.Core.Base.ExceptionHandling;
using Ventana.Core.ExceptionHandling;

namespace Ventana.Core.Activities.Builders
{
    public class SystemErrorHandlingBuilder : ErrorHandlingBuilderBase
    {
        private const int MachineExpirationInMinutes = 10;  //TODO: change this time.

        public override void Build(IActivityMachine machine)
        {
            var e = machine.Configuration.Data.Exception as AtlasException;
            var errorMachine = machine as ErrorHandlingActivityMachine;
            
            if (e != null && errorMachine != null)
            {
                errorMachine.ExpirationTimespan = TimeSpan.FromMinutes(MachineExpirationInMinutes);
                errorMachine.Instrument = machine.Configuration.Data.Instrument as IInstrument;
                errorMachine.InstrumentManager = machine.Configuration.Data.InstrumentManager as IInstrumentManager;
                errorMachine.Engine = machine.Configuration.Data.Engine as IExecutableEngine;

                AssembleMachineForTrayProcessor(e, errorMachine);
            }
        }
        
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        protected override void SetSeverityActivities(DynamicActivityMachine machine, AtlasException ex)
        {
            //Raise error dialog
            SetNotificationActivities(machine, ex);

            var stationMachine = machine as ErrorHandlingActivityMachine;
            var station = ex.Station;

            switch (ex.Severity)
            {
                case ExceptionSeverityCategory.Fatal:
                    //stop commands running on stations and shut down instrument.
                    if (IsAfmInitializing(station))
                    {
                        station.SetError(ex);
                    }
                    SetStopInstrumentActivity(stationMachine, ex);
                    break;

                case ExceptionSeverityCategory.Standard:
                    if (IsAfmInitializing(station))
                    {
                        //Treat error as fatal
                        station.SetError(ex);
                        SetStopInstrumentActivity(stationMachine, ex);
                    }
                    else
                    {
                        //sets the degraded mode if it exists in the ExceptionOperationsGroup
                        SetDegradedModeActivity(machine, ex);
                    }
                    break;
            }
        }

        private static bool IsAfmInitializing(IStation station)
        {
            return (station != null && station.Type == StationType.Afm && station.State == StationState.Initializing);
        }
    }
}
