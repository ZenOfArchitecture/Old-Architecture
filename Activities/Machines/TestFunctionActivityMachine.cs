using System;
using System.ComponentModel;
using Ventana.Core.Base;
using Ventana.Core.Base.Activities;
using Ventana.Core.Base.BusinessObjects;
using Ventana.Core.Utilities;

namespace Ventana.Core.Activities.Machines
{
    public class TestFunctionActivityMachine : StationActivityMachine
    {
        public TestFunctionActivityMachine(string name) : base(name)
        {
            RequiresResourceLocking = true;
        }

        public IErrorHandler ErrorHandler { get; internal set; }

        #region TestFunction Activities
        /// <summary>
        /// Set the next Activity to be triggered for evaluation by a station status change.
        /// </summary>
        /// <param name="scriptName">The name of the maintenence script to run.</param>
        /// <param name="errorCode">The error code to raise if the executive call fails.</param>
        //public void SetActivityPerformMaintenance(string scriptName, string errorCode)
        //{
        //    var activity = new ErrorRaisingActivity<string>("Executive.PerformMaintenanceProcess-" + scriptName, 
        //        () => _Executive.PerformMaintenanceProcess(Actor.StationID, scriptName), _ErrorHandler, errorCode, "ok");
        //    AddActivity(activity);
        //}

        ///// <summary>
        ///// Set the next activity to send an error message to the StandardHandler.
        ///// </summary>
        ///// <param name="errorCode">The error code being thrown.</param>
        //public void SetActivityRaiseError(string errorCode)
        //{
        //    //AddActivity(new Activity("StandardHandler.HandleError:" + errorCode, () => action(_ErrorHandler)));
        //    AddActivity(new Activity("StandardHandler.HandleError:" + errorCode, () => _ErrorHandler.HandleError(errorCode)));
        //}

        //public void SetActivityAssignStatusOnStationStatus(StationState safeToActStatus, StationState newStatus)
        //{
        //    SetActivityOnStationStatus(Actor.Name + ".Update", safeToActStatus, s => s.Update(newStatus));
        //}
        #endregion

        #region Quitting
        /// <summary>
        /// Set a quit conditional activity for the machine.  If this quit condition evaluates to true, it will
        /// send the specified error to StandardHandler before quitting.
        /// </summary>
        /// <param name="condition">The condition that causes this machine to quit.</param>
        /// <param name="errorCode"></param>
        //protected void SetQuitConditionWithErrorActivity(Func<bool> condition, string errorCode)
        //{
        //    // Because we don't want to restrict interrupt conditions to a specific target, the condition is built
        //    // here to ignore the TActor generic type.
        //    var quitCondition = new ConditionalActivity<IStation>(Name + ".QuitAndRaiseError:" + errorCode, Actor, actor => condition(), 
        //        () => _ErrorHandler.HandleError(new AtlasException(errorCode, null, null, new [] { Actor.Name })));
        //    AddQuitCondition(quitCondition);
        //}
        #endregion

        #region Execution Triggers
        protected override void ActivateExecutionTriggers()
        {
            base.ActivateExecutionTriggers();
            //Instrument.NonDispatchedPropertyChanged += HandleInstrumentEnteredAnExecutableMode;
        }

        protected override void DeactivateExecutionTriggers()
        {
            base.DeactivateExecutionTriggers();
            //Instrument.NonDispatchedPropertyChanged-= HandleInstrumentEnteredAnExecutableMode;
        }

        private void HandleInstrumentEnteredAnExecutableMode(object sender, PropertyChangedEventArgs args )
        {
            //if (args.PropertyName.Equals(_instrument.PropertyToString(() => _instrument.State)))
            //{
            //    switch (_instrument.State)
            //    {
            //        case InstrumentState.TestFunction:
            //            OnExecuteRequested(this, null); //run the machine
            //            break;
            //    }
            //}
        }
        #endregion
    }
}
