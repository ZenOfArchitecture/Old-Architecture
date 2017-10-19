using System;
using Ventana.Core.Base.Activities.Resources;
using Ventana.Core.Base.BusinessObjects;
using Ventana.Core.Base.Transportation;
using Ventana.Core.Utilities;

namespace Ventana.Core.Activities
{
    /// <summary>
    /// Creates ActivityMachine configurations.
    /// </summary>
    public static class Configurations
    {
        public static DynamicConfiguration CreateTrayMoverConfig(ITrayDetector source, ITrayHandler destination, IInstrument instrument, object sourceLocker)
        {
            var transport = instrument.FindStation("Transportation");
            var machineConfig = new DynamicConfiguration(BuilderTypes.TrayMovingMachineBuilder);
            machineConfig.Data.DestinationStation = destination;
            machineConfig.Data.SourceStation = source;
            machineConfig.Data.Instrument = instrument;
            machineConfig.Data.Transport = transport as ITransport;
            machineConfig.Data.NumberOfRetries = 0;
            machineConfig.Data.SourceLockOwner = sourceLocker;

            machineConfig.Data.ProcessType = CodeProcess.MoveTray;
            machineConfig.Data.ProcessName = CodeProcess.MoveTray.ToString();

            return machineConfig;
        }

        public static DynamicConfiguration CreateTransportOperationConfig(TransportOperation operation, ITrayDetector station, ITransport transport, IInstrument instrument, object sourceLocker, ITransportDock destination = null)
        {
            var config = new DynamicConfiguration(BuilderTypes.TransportOperationMachineBuilder);
            config.Data.Instrument = instrument;
            config.Data.Station = station;
            config.Data.Transport = transport;
            config.Data.TransportOperation = operation;
            config.Data.SourceLockOwner = sourceLocker;
            config.Data.MoveCancellationToken = new MoveCancellationToken();
            // This will be the time allotted to each transport operation machine. 
            config.Data.ExpirationTimespan = new TimeSpan(0, 0, 1, 15);
            // The source as an obstruction is only relevant for CompletePickup from Stainer.
            if (destination != null)
            {
                config.Data.DestinationZLocation = destination.DockZ;
            }
            return config;
        }
    }
}
