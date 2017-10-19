using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using Ventana.ATLAS.Interfaces;

namespace Ventana.ATLAS.Activities.ActivityMachines
{
    public class WasteProcessingActivityMachine : ExpressionBasedMachine<IWasteProcessor>
    {
        protected readonly IInstrument _instrument;
        protected readonly IExecutableEngine _engine;

        public WasteProcessingActivityMachine(IWasteProcessor actor, ActivityMachineConfiguration config, string name)
            : base(actor, name)
        {
            RequiresActorLocking = false;
            IsSynchronous = false;
            _instrument = config.Instrument;
            _engine = config.Engine;
        }

        /// <summary>
        /// Copy constructor
        /// </summary>
        /// <param name="templateMachine"></param>
        protected WasteProcessingActivityMachine(WasteProcessingActivityMachine templateMachine)
            : this(templateMachine.Actor, templateMachine.Builder.MachineConfiguration, templateMachine.Name)
        {
            Builder = templateMachine.Builder;
        }

        /// <summary>
        /// Set the next IActivity as a ConditionalActivity to be triggered for evaluation by a 
        /// </summary>
        /// <param name="doneTarget">the target used to test the done condition.</param>
        /// <param name="doneCondition">An </param>
        public void SetConditionalActivityRepeatWhenDone(IStation doneTarget, Func<IStation, bool> doneCondition)
        {
            AddActivity(new ConditionalActivity<IStation>(Name + ".Repeat", doneTarget, doneCondition,
                () => _engine.Execute(new WasteProcessingActivityMachine(this))));
        }

        public void SetQuitOnInstrumentStopped()
        {
            SetQuitCondition(() => _instrument.Status == InstrumentStatus.Stopped);
        }

        #region Runtime triggers for IStation
        /// <summary>
        /// Add a runtime trigger for a PropertyChanged event.
        /// </summary>
        /// <param name="propertyName">The name of the property that changed</param>
        /// <param name="sender">the event sender</param>
        public void AddPropertyChangedRuntimeTrigger(string propertyName, IStation sender)
        {
            AddPropertyChangedRuntimeTrigger(propertyName, sender, null);
        }
        #endregion

        #region Execution & Runtime Triggers
        /// <summary>
        /// Deactivate this machine's dispatcher and stop listening to all triggers.
        /// </summary>
        protected override void DeactivateRuntimeTriggers()
        {
            base.DeactivateRuntimeTriggers();

            _instrument.PropertyChanged -= HandleInstrumentStatusChanged;
        }

        /// <summary>
        /// Make this ActivityMachine reactive to its relevant triggers.
        /// </summary>
        protected override void ActivateRuntimeTriggers()
        {
            base.ActivateRuntimeTriggers();

            _instrument.PropertyChanged += HandleInstrumentStatusChanged;
        }
        #endregion

        protected override void ObtainActorLock()
        {
            // empty impl
        }

        protected override void ReleaseActorLock()
        {
            // empty impl
        }

        #region Trigger Handling
        private void HandleInstrumentStatusChanged(object sender, PropertyChangedEventArgs args)
        {
            string trigger = Actor.GetType().Name + "." + args.PropertyName;
            if (args.PropertyName.Equals(_instrument.PropertyToString(() => _instrument.Status)))
            {
                if (_instrument.Status == InstrumentStatus.Stopping)
                {
                    Quit(true);
                }
            }
        }
        #endregion
    }
}
