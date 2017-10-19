using System;
using System.Collections.Generic;
using System.Globalization;
using Ventana.Core.Base.Activities;
using Ventana.Core.Base.Configuration;
using Ventana.Core.Logging;
using Ventana.Core.Utilities;

namespace Ventana.Core.Activities.Builders
{
    /// <summary>
    /// Class Loader to create IActivityMachineBuilder objects.
    /// </summary>
    public static class ActivityMachineBuilderLoader
    {
        private static readonly List<Type> BuilderTypes = new List<Type>();

        static ActivityMachineBuilderLoader()
        {
            BuilderTypes = TypeLoader.LoadTypes<IActivityMachineBuilder>();
        }

        /// <summary>
        /// Dynamically load an instance of an IActivityMachineBuilder specified by the resource selector
        /// in the given configuration.
        /// </summary>
        /// <param name="configuration">A pre-poulated ActivityMachine configuration</param>
        /// <returns>IActivityMachineBuilder, or null if type was not found or instantiation failed.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        public static IActivityMachineBuilder GetActivityMachineBuilder(DynamicConfiguration configuration)
        {
            foreach (var builderType in BuilderTypes)
            {
                if (builderType.FullName.Equals(configuration.ResourceSelector))
                {
                    try
                    {
                        var builder = Activator.CreateInstance(builderType) as IActivityMachineBuilder;
                        return builder;
                    }
                    catch (Exception e)
                    {
                        LogService.Log(LogType.System, LogMessageType.Error, "ActivityMachineBuilderLoader",
                            string.Format(CultureInfo.InvariantCulture, "Failed to load instance of class [{0}]: [{1}]", configuration.ResourceSelector, e.Message), e);
                    }
                }
            }
            LogService.Log(LogType.System, LogMessageType.Error, typeof(ActivityMachineBuilderLoader).Name,
                           "Could not locate the ActivityMachine builder " + configuration.ResourceSelector + ".");

            return null;
        }
    }
}

