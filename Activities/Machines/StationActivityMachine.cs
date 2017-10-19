using System;
using System.Globalization;
using Ventana.Core.Activities.Parts;
using Ventana.Core.Base;
using Ventana.Core.Base.Activities;
using Ventana.Core.Base.BusinessObjects;
using Ventana.Core.Base.Processors;
using Ventana.Core.Logging;

namespace Ventana.Core.Activities.Machines
{
    public class StationActivityMachine : DynamicActivityMachine
    {
        private IStation _station;
        /// <summary>
        /// Ctor for a StationActivityMachine.
        /// </summary>
        /// <param name="name">Name for the machine</param>
        public StationActivityMachine(string name) : base(name)
        {
            HaltOnFault = true;
            IsSynchronous = false;
        }

        public StationActivityMachine(IStation station) : base(station.Name)
        {
            SetStationAndTrayReferences(station);
        }

        public ITrayProcessor TrayProcessor { get; internal set; }

        public ITrayHandler TrayHandler { get; internal set; }

        public IInstrument Instrument { get; internal set; }

        public IInstrumentManager InstrumentManager { get; internal set; }

        public IExecutableEngine Engine { get; internal set; }

        /// <summary>
        /// Gets or sets a tray.
        /// A local tray property is used rather than the station's tray property because the latter may change during execution.
        /// </summary>
        public ITray Tray { get; internal set; }

        public void SetStationAndTrayReferences(IStation station)
        {
            TrayProcessor = station as ITrayProcessor;
            TrayHandler = station as ITrayHandler;
            _station = station;
            if (TrayProcessor != null)
            {
                Tray = TrayProcessor.Tray;
            }
        }

        #region Station conditional and unconditional Activities
        /// <summary>
        /// Set an activity that enables the station to do a tray pickup and moves the
        /// station's tray to the given tray state to Error (since it's an internal abort).
        /// </summary>
        public void SetActivitySystemAbortTray()
        {
            if (TrayHandler.Tray != null)
            {
                AddActivity(new DynamicActivity(string.Format(CultureInfo.InvariantCulture, "Notify Tray Aborted {0}", TrayHandler.Name), () => TrayHandler.Tray.OnAborted(TrayAbortType.Immediate)));
                AddActivity(new DynamicActivity(TrayHandler.Name + ".AbortTrayByError", () => InstrumentManager.AbortFlowForTray(TrayHandler.Tray)));
            }
        }

        public void SetActivityDisableOnNotProcessing(bool allowReenable)
        {            
            AddConditionalActivity(new DynamicConstraint<IStation>(TrayProcessor.Name, _station, s => s.State==StationState.Idle && !(s as ITrayDetector).IsTrayDetected),
                new DynamicActivity(TrayProcessor.Name + ".Disable", () => ((ISupportsDisabling)TrayProcessor).Disable(allowReenable)));
        }

        public void SetActivityDisableOnStatus(StationState safeToDisableStatus, bool allowReenable)
        {     
            // Changed from IStation to ISupportsDisabling interface as enable/disable removed from IStation. (J. Lawrence)
            SetActivityOnStationState(_station.Name + ".Disable", safeToDisableStatus,
                s => (s as ISupportsDisabling).Disable(allowReenable));
        }

        public void SetActivityDisable(bool allowReenable)
        {

            AddActivity(new DynamicActivity(_station.Name + ".Disable", () => ((ISupportsDisabling)_station).Disable(allowReenable)));
        }

        public void SetActivityStop()
        {

            AddActivity(new DynamicActivity(_station.Name + ".Stop", () => _station.Stop()));
        }

        public void SetActivityStopOnNotProcessing()
        {
            if (TrayProcessor == null)
            {
                return;
            }

            AddConditionalActivity(new DynamicConstraint<IStation>(_station.Name, _station, s => !(s as ITrayProcessor).IsProcessing),
                new DynamicActivity(_station.Name + ".Stop", () => _station.Stop()));
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(System.String,System.String)")]
        public void SetActivityReinitializeOnStopped(bool allowReenable)
        {
            if (TrayProcessor == null)
            {
                LogService.Log("StationActivityMachine", "Can not intialize a station that does not process trays.");
            }

            SetActivityOnStationState(_station.Name + ".Reinitialize", StationState.Stopped, s => (s as IStation).Start());
        }

        public void SetActivityPowerOff()
        {
            AddActivity(new DynamicActivity(_station.Name + ".TurnPowerOff", () => _station.PowerOff()));
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(System.String,System.String)")]
        public void SetActivityPowerOffOnStatus(StationState safeToPowerOffStatus)
        {
            if (TrayProcessor == null)
            {
                LogService.Log(GetType().Name, "Can't power off station this is not a tray processor.");
                return;
            }
            SetActivityOnStationState(_station.Name + ".TurnPowerOff", safeToPowerOffStatus, s => (s as IStation).PowerOff());
        }
        
        public void SetPauseUntilContinueOrFinish(StationState continueState, StationState finishState)
        {
            AddFinishOrContinueCondition(new DynamicConstraint<ITrayProcessor>("StationState==" + finishState, TrayProcessor, s => s.State == finishState),
                                       new DynamicConstraint<ITrayProcessor>("StationState==" + continueState, TrayProcessor, s => s.State == continueState));
        }
        #endregion

        #region Activities specifically typed for use with IStation
        /// <summary>
        /// Set the next  IActivity as a ConditionalActivity to be triggered for evaluation by a station status change.
        /// </summary>
        /// <param name="activityName">The name to give to the activity</param>
        /// <param name="state">StationState</param>
        /// <param name="action">An action with no return value.</param>
        protected void SetActivityOnStationState(string activityName, StationState state, Action<ITrayProcessor> action)
        {
            AddConditionalActivity(new DynamicConstraint<ITrayProcessor>("StationState==" + state, TrayProcessor, s => s.State == state),
                                   new DynamicActivity(activityName, () => action(TrayProcessor)));
        }

        /// <summary>
        /// Set the next IActivity as a ConditionalQuitActivity to be triggered for evaluation by a station status change.
        /// </summary>
        /// <param name="activityName">The name to give to the activity</param>
        /// <param name="status">StationState</param>
        /// <param name="actionWithResult">An action that returns a boolean value.</param>
        /// <param name="quitForThisResult">Tells the QuitActivity to request a machine quit if its result matches this boolean value.</param>
        protected void SetConditionalQuitActivityOnStationStatus(string activityName, StationState status, Func<IStation, bool> actionWithResult, bool quitForThisResult)
        {
            AddConditionalActivity(new DynamicConstraint<ITrayProcessor>(_station.Name, TrayProcessor, s => (s as IStation).State == status),
                                   new DynamicActivity(activityName, () => actionWithResult(TrayProcessor as IStation)));
        }
        
        /// <summary>
        /// Set the next IActivity as a ConditionalActivity to be triggered for evaluation by a tray status change.
        /// </summary>
        /// <param name="activityName">The name to give to the activity</param>
        /// <param name="status">TrayState</param>
        /// <param name="action">An action with no return value.</param>
        protected void SetActivityOnTrayStatus(string activityName, TrayState status, Action<IStation> action)
        {
            if (Tray != null)
            {
                AddConditionalActivity(new DynamicConstraint<ITrayProcessor>(_station.Name, TrayProcessor, s => s.Tray.State == status),
                                       new DynamicActivity(activityName, () => action(_station)));
            }
        }
        
        #endregion

        #region Runtime triggers for IStation
        /// <summary>
        /// Add a runtime trigger for a PropertyChanged event.
        /// </summary>
        /// <param name="propertyName">The name of the property that changed</param>
        /// <param name="sender">the event sender</param>
        //public void AddPropertyChangedRuntimeTrigger(string propertyName, IStation sender)
        //{
        //    AddPropertyChangedRuntimeTrigger(propertyName, sender, null);
        //}
        #endregion

        #region Quitting
        public void SetQuitOnTrayState(TrayState state)
        {
            if (Tray != null)
            {
                UseAdditionalFinishCondition(new DynamicConstraint<ITray>("TrayState==" + state, Tray, t => t.State == state));
                //SetQuitCondition(() => Tray.State == status);
            }
        }


        public void SetQuitOnStationState(StationState state)
        {
            UseAdditionalFinishCondition(new DynamicConstraint<ITrayProcessor>("StationState==" + state, TrayProcessor, s => s.State == state));
            //SetQuitCondition(() => Station.State == status);
        }

        public void SetQuitOnInstrumentState(InstrumentState state)
        {
            UseAdditionalFinishCondition(new DynamicConstraint<IInstrument>("InstrumentState==" + state, Instrument, i => i.State == state));
            //SetQuitCondition(() => _instrument.State == status);
        }

        public void SetQuitOnConditionOfStation(Func<ITrayProcessor, bool> condition)
        {
            UseAdditionalFinishCondition(new DynamicConstraint<ITrayProcessor>("StationCondition", TrayProcessor, condition));
            //SetQuitCondition(() => condition(Station));
        }

        public void SetPauseUntilConditionOfStation(Func<ITrayProcessor, bool> condition)
        {
            AddContinueCondition(new DynamicConstraint<ITrayProcessor>("StationCondition", TrayProcessor, condition));
            //SetPauseCondition(Station, condition);
        }

        public void SetPauseUntilStationStatus(StationState state)
        {
            AddContinueCondition(new DynamicConstraint<ITrayProcessor>("StationState==" + state, TrayProcessor, s => s.State == state));
            //SetPauseCondition(Station, s => s.State == status);
        }

        public void SetPauseUntilInstrumentState(InstrumentState state)
        {
            AddContinueCondition(new DynamicConstraint<IInstrument>("InstrumentState==" + state, Instrument, i => i.State == state));
            //SetPauseCondition(_instrument, i => i.State == status);
        }
        #endregion

        #region Execution & Runtime Triggers
        /// <summary>
        /// Deactivate this machine's dispatcher and stop listening to all triggers.
        /// </summary>
        //protected override void DeactivateRuntimeTriggers()
        //{
        //    base.DeactivateRuntimeTriggers();
            
        //    // Unhook from the StationMachine specific triggers.
        //    Station.PropertyChanged -= HandleStationsPropertyChanged;
        //    if (_instrument != null)
        //    {
        //        _instrument.PropertyChanged -= HandleInstrumentStateChanged;
        //    }
        //    if (Tray != null)
        //    {
        //        Tray.PropertyChanged -= HandleTraysPropertyChanged;
        //    }
        //}

        ///// <summary>
        ///// Make this ActivityMachine reactive to its relevant triggers.
        ///// </summary>
        //protected override void ActivateRuntimeTriggers()
        //{
        //    base.ActivateRuntimeTriggers();
            
        //    // Hookup the StationMachine specific triggers.
        //    Station.PropertyChanged += HandleStationsPropertyChanged;
        //    _instrument.PropertyChanged += HandleInstrumentStateChanged;
        //    if (Tray != null)
        //    {
        //        Tray.PropertyChanged += HandleTraysPropertyChanged;
        //    }
        //}
        #endregion

        #region Station Locking
        //public override bool ObtainActorLocks()
        //{
        //    if (RequiresActorLocking && Station is ISharedResource)
        //    {
        //        // A StationActivityMachine must begin execution with an operation lock on the station.
        //        // The lock is released when the this machine completes or is interrupted.
        //        return (Station as ISharedResource).ObtainLock(this);
        //    }
        //}

        //public override void ReleaseActorLocks()
        //{
        //    if (Station is ISharedResource)
        //    {
        //        (Station as ISharedResource).ReleaseLock(this);
        //    }
        //}
        #endregion

        #region Trigger Handling
        //private void HandleStationsPropertyChanged(object sender, PropertyChangedEventArgs args)
        //{
        //    string trigger = Station.GetType().Name + "." + args.PropertyName;
        //    if (args.PropertyName.Equals(Station.PropertyToString(() => Station.State)))
        //    {
        //        RunUntilPausedOrCompleted(trigger);
        //    }
        //}

        //private void HandleTraysPropertyChanged(object sender, PropertyChangedEventArgs args)
        //{
        //    string trigger = Station.GetType().Name + "." + args.PropertyName;
        //    if (args.PropertyName.Equals(Tray.PropertyToString(() => Tray.State)))
        //    {
        //        RunUntilPausedOrCompleted(trigger);
        //    }
        //}

        //private void HandleInstrumentStateChanged(object sender, PropertyChangedEventArgs args)
        //{
        //    string trigger = Station.GetType().Name + "." + args.PropertyName;
        //    if (args.PropertyName.Equals(_instrument.PropertyToString(() => _instrument.State)))
        //    {
        //        if (_instrument.State == InstrumentState.Running)
        //        {
        //            RunUntilPausedOrCompleted(trigger);
        //        }
        //        else if (_instrument.State == InstrumentState.Stopping)
        //        {
        //            Quit(true);
        //        }
        //    }
        //}
        #endregion
    }
}
