using System;
using Ventana.Core.Activities.Builders;
using Ventana.Core.Activities.Machines;
using Ventana.Core.Base;
using Ventana.Core.Base.Activities;
using Ventana.Core.Base.BusinessObjects;
using Ventana.Core.Base.ExceptionHandling;
using Ventana.Core.ExceptionHandling;

namespace Ventana.Core.Activities.Builders
{
    public class SystemEmergencyHandlingBuilder : ErrorHandlingBuilderBase
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

        protected override void SetSeverityActivities(DynamicActivityMachine machine, AtlasException ex)
        {
            //Raise error dialog
            SetNotificationActivities(machine, ex);

            var stationMachine = machine as ErrorHandlingActivityMachine;

            if (stationMachine != null)
            {
                SetEmergencyStopInstrumentActivity(stationMachine, ex);
            }
        }
    }
}
