using System.ComponentModel;
using Ventana.Core.Activities.Parts;
using Ventana.Core.Base;
using Ventana.Core.Base.BusinessObjects;
using Ventana.Core.Base.CareGiver;
using Ventana.Core.Base.ExceptionHandling;
using Ventana.Core.Base.Processors;
using Ventana.Core.ExceptionHandling;
using Ventana.Core.Logging;
using Ventana.Core.Utilities;

namespace Ventana.Core.Activities.Machines
{
    /// <summary>
    /// This is a simple derivation of StationActivityMachine that handles delayed execution for
    /// ErrorHandling.  It acts the same as a StationActivityMachine, but it can also deal with
    /// an error handling scenario where its execution needs to be delayed until a station lock
    /// becomes obtainable.
    /// </summary>
    public class ErrorHandlingActivityMachine : StationActivityMachine
    {

        public ErrorHandlingActivityMachine(string name)
            : base(name)
        {
            RequiresResourceLocking = true;
        }

        #region Execution Triggers
        protected override void ActivateExecutionTriggers()
        {
            base.ActivateExecutionTriggers();

            TrayProcessor.BusinessLayerPropertyChanged += HandleDelayedExecutionTriggers;
        }

        protected override void DeactivateExecutionTriggers()
        {
            base.DeactivateExecutionTriggers();

            TrayProcessor.BusinessLayerPropertyChanged -= HandleDelayedExecutionTriggers;
        }

        private void HandleDelayedExecutionTriggers(object sender, PropertyChangedEventArgs args)
        {
            // We need to ensure that a lock is available before this machine can run,
            // so try to obtain a greedy lock here since Execute is just around the corner.
            // Execute will lock again, but that doesn't matter since we already got it here.
            if (args.PropertyName.Equals(TrayProcessor.PropertyToString(() => TrayProcessor.IsLocked))
                && TrayProcessor.Tray == null && !TrayProcessor.IsLocked && TrayProcessor.ObtainLock(this))
            {
                OnExecuteRequested(this, null);
            }
        }
        #endregion
    }
}
