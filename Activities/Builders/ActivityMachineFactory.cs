using System;
using System.Globalization;
using Ventana.Core.Activities.Machines;
using Ventana.Core.Base.Activities;
using Ventana.Core.Base.Activities.Resources;
using Ventana.Core.Base.BusinessObjects;
using Ventana.Core.Base.CommandOperations;
using Ventana.Core.Base.Configuration;
using Ventana.Core.Base.ExceptionHandling;
using Ventana.Core.Base.Transportation;
using Ventana.Core.Base.Utilities;
using Ventana.Core.ExceptionHandling;
using Ventana.Core.Logging;
using Ventana.Core.Utilities;
using Ventana.Core.Utilities.ExtensionMethods;

namespace Ventana.Core.Activities.Builders
{
    public static class ActivityMachineFactory
    {
        //TODO: consolidate this factory and BusinessObjectsMachineFactory
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        public static IActivityMachine Create(DynamicConfiguration config)
        {
            try
            {
                IActivityMachine machine = null;
                if (BuilderTypes.StationErrorHandlingMachineBuilder == config.ResourceSelector ||
                    BuilderTypes.SystemErrorHandlingMachineBuilder == config.ResourceSelector ||
                    BuilderTypes.SystemEmergencyHandlingMachineBuilder == config.ResourceSelector)
                {
                    machine = CreateMachineForTrayProcessorErrorHandling(config);
                }
                else if (config.ResourceSelector == BuilderTypes.MotionSystemErrorHandlingBuilder)
                {
                    machine = CreateMachineForMotionErrorHandling(config);
                }
                else if (BuilderTypes.CommandExecutingMachineBuilder.EndsWith(config.ResourceSelector, StringComparison.OrdinalIgnoreCase))
                {
                    machine = CreateMachineForCommandExecuting(config);
                }
                else if (config.ResourceSelector == BuilderTypes.TrayMovingMachineBuilder)
                {
                    machine = CreateMachineForTrayMoving(config);
                }
                else if (config.ResourceSelector == BuilderTypes.TransportOperationMachineBuilder)
                {
                    machine = CreateMachineForTransportOperation(config);
                }

                machine.Configuration = config;

                return machine;

            }
            catch (Exception ex)
            {
                //TODO: throw custom exception
                LogService.Log(LogType.System, LogMessageType.Error, "ActivityMachineFactory",
                    String.Format(CultureInfo.InvariantCulture, "Machine creation failed for selector '{0}': {1}", config != null ? config.ResourceSelector : "unknown", ex.Message), ex);
            }
            return null;
        }

        private static IActivityMachine CreateMachineForCommandExecuting(DynamicConfiguration config)
        {
            CommandExecutingMachine activityMachine;
            // If no script treenode was given yet, try to create one here.
            if (config.HasDataKey("ScriptName") && config.HasDataKey("DeviceType") && !config.HasDataValue("TreeNode"))
            {
                config.Data.TreeNode = ((IExecuterLoader)config.Data.Loader).
                    GetScriptNode(config.Data.ScriptName, config.Data.DeviceType);
            }

            if (config.HasDataValue("IsSubmachine") && config.Data.IsSubmachine)
            {
                activityMachine = new CommandExecutingMachine(config, config.Data.Name);
            }
            else
            {
                activityMachine = new CommandExecutingMachine(config.Data.ProcessName, config);
            }

            if (config.HasDataValue(Key.Instrument))
            {
                var instrument = config.Data.Instrument as IInstrument;
                config.Data.IsInServiceMode = (instrument != null && instrument.InServiceMode);
            }

            activityMachine.Builder = ActivityMachineBuilderLoader.GetActivityMachineBuilder(config);
            activityMachine.LogType = LogType.Script;

            return activityMachine;
        }

        public static IActivityMachine CreateMachineForTrayProcessorErrorHandling(DynamicConfiguration config)
        {
            var ae = config.Data.Exception as AtlasException;
            config.Data.Station = ae.Station;

            var activityMachine = new ErrorHandlingActivityMachine(GetErrorMachineName(ae));
            activityMachine.Builder = ActivityMachineBuilderLoader.GetActivityMachineBuilder(config);

            return activityMachine;
        }

        public static IActivityMachine CreateMachineForMotionErrorHandling(DynamicConfiguration config)
        {
            var ae = config.Data.Exception as AtlasException;
            config.Data.Station = ae.Station;

            var activityMachine = new DynamicActivityMachine(GetErrorMachineName(ae));
            activityMachine.Builder = ActivityMachineBuilderLoader.GetActivityMachineBuilder(config);

            return activityMachine;
        }

        private static string GetErrorMachineName(AtlasException ae)
        {
            string name = string.Empty;
            switch (ae.Severity)
            {
                case ExceptionSeverityCategory.Standard:
                    name = "StandardError-" + ae.Code;
                    break;

                case ExceptionSeverityCategory.Severe:
                    name = "SevereError-" + ae.Code;
                    break;

                case ExceptionSeverityCategory.Critical:
                    name = "CriticalError-" + ae.Code;
                    break;
                case ExceptionSeverityCategory.Fatal:
                    name = "FatalError-" + ae.Code;
                    break;
                case ExceptionSeverityCategory.Warning:
                    name = "Warning-" + ae.Code;
                    break;
                case ExceptionSeverityCategory.Information:
                    name = "Information-" + ae.Code;
                    break;
            }

            if (ae.Station != null)
            {
                name = ae.Station.Name + ":" + name;
            }

            return name;
        }

        public static IActivityMachine CreateMachineForTrayMoving(DynamicConfiguration config)
        {
            var source = config.Data.SourceStation as ITrayDetector;
            var destination = config.Data.DestinationStation as ITrayHandler;

            var activityMachine = new TrayMover(source.Name, destination.Name);
            activityMachine.Instrument = config.Data.Instrument as IInstrument;
            activityMachine.NumberOfRetries = config.Data.NumberOfRetries;
            activityMachine.Destination = destination;
            activityMachine.Source = source;
            activityMachine.Transport = config.Data.Transport as ITransport;
            activityMachine.SourceLockOwner = config.Data.SourceLockOwner;
            activityMachine.Configuration = config;

            // No Builder needed for this machine

            return activityMachine;
        }

        internal static IActivityMachine CreateMachineForTransportOperation(DynamicConfiguration config)
        {
            var dispatcher = config.Data.Dispatcher as SimpleDispatcher;
            var operation = config.Data.TransportOperation;
            var activityMachine = new TransportOperationMachine(Enum.GetName(typeof(TransportOperation), operation), dispatcher);

            // Try to reuse a builder if one was supplied.
            try
            {
                activityMachine.Builder = config.Data.Builder as IActivityMachineBuilder;
            }
            catch (Exception)
            {
                //nothing to do
            }
            if (activityMachine.Builder == null)
            {
                activityMachine.Builder = ActivityMachineBuilderLoader.GetActivityMachineBuilder(config);
            }

            return activityMachine;
        }
    }
}
