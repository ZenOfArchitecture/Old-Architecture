using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Ventana.Core.Base.Activities;

namespace Ventana.Core.Activities.Builders
{
    public class ReplicatingMachineBuilder : ActivityMachineBuilder
    {
        public override void Build(IActivityMachine machine)
        {
            //if (activityMachine is WasteProcessingActivityMachine)
            //{
            //    AssembleMachineForRecurringWastePurge(activityMachine as WasteProcessingActivityMachine);
            //}
            //else
            //{
            //    LogService.Log(LogType.System, LogMessageType.Warning, GetType().Name, "Attempted to build a RepeatableActivityMachine for an unknown purpose.");
            //}
        }


        //public void AssembleMachineForRecurringWastePurge(WasteProcessingActivityMachine activityMachine)
        //{
        //    var stainer = activityMachine.Builder.MachineConfiguration.Stations.FirstOrDefault(s => s.Type == DeviceType.Stainer);
        //    var afm = activityMachine.Builder.MachineConfiguration.Stations.FirstOrDefault(s => s.Type == DeviceType.AFM_Waste) as IWasteProcessor;

        //    if (stainer != null)
        //    {
        //        activityMachine.SetQuitOnInstrumentStopped();

        //        // add a new runtime trigger that responds to a stainer's PropertyChanged event and filtered on the State property.
        //        activityMachine.AddPropertyChangedRuntimeTrigger(stainer.PropertyToString(() => stainer.State), stainer);
        //        // Now pause the machine until the added trigger from the stainer leads to the stainer being Done.
        //        activityMachine.SetPauseCondition(stainer, s => s.State == StationState.Done);
        //        // Now purge the waste using an IWasteProcessor.
        //        activityMachine.SetActivity("PurgeWaste", delegate { afm.EmptyPressureTrap(); });
        //        // Finally, reschedule a clone of this machine once the stainer is no longer done.
        //        activityMachine.SetConditionalActivityRepeatWhenDone(stainer, s => s.State != StationState.Done);
        //    }
        //    else
        //    {
        //        activityMachine.Quit();
        //        //log warning
        //    }
        //}
    }
}
