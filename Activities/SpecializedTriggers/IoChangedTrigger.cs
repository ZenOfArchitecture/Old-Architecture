using System;
using Ventana.Core.Activities.Parts;
using Ventana.Core.Base.Activities;
using Ventana.Core.Base.DeviceOperations;

namespace Ventana.Core.Activities.SpecializedTriggers
{
    public class IoChangedTrigger : UmlTrigger
    {
        public IoChangedTrigger(string name, IIoValueChangedNotifier source, IUmlConstraint guard)
            : base(name, source, guard)
        {
        }

        public IoChangedTrigger(string name, IIoValueChangedNotifier source) : this(name, source, null)
        {
        }

        public override IUmlTrigger Copy()
        {
            var copy = new IoChangedTrigger(Name, Source) { LogType = LogType };
            if (IsLive)
            {
                copy.Enable();
            }
            return copy;
        }

        public new IIoValueChangedNotifier Source
        {
            get { return base.Source as IIoValueChangedNotifier; }
            private set { base.Source = value; }
        }

        protected override void Connect()
        {
            Source.IoValueChanged += HandleIoChanged;
        }

        protected override void Disconnect()
        {
            Source.IoValueChanged -= HandleIoChanged;
        }

        private void HandleIoChanged(object sender, IoValueChangedEventArgs e)
        {
            Trip();
        }
    }
}
