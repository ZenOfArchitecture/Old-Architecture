using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using SimpleMvvmToolkit;
using Ventana.Core.Activities.Parts;
using Ventana.Core.Base.Activities;

namespace Ventana.Core.Activities.SpecializedTriggers
{
    /// <summary>
    /// A trigger for the NonDispatchedPropertyChanged event on a set of IDualNotifyPropertyChanged objects.
    /// Unlike PropertyChangedTrigger, this can not respond to nested property changed events.
    /// </summary>
    /// <remarks>Using this on ObservableCollection class gave unreliable timing</remarks>
    public class MultiPropertyChangedTrigger : UmlTrigger
    {
        public MultiPropertyChangedTrigger(string name, IEnumerable<IDualNotifyPropertyChanged> sources)
            : this(name, sources, null, null)
        {
        }

        public MultiPropertyChangedTrigger(string name, IEnumerable<IDualNotifyPropertyChanged> sources, string propertyName)
            : this(name, sources, propertyName, null)
        {
        }

        public MultiPropertyChangedTrigger(string name, IEnumerable<IDualNotifyPropertyChanged> sources, string propertyName, IUmlConstraint guard)
            : base(name, null, guard)
        {
            PropertyName = string.IsNullOrEmpty(propertyName) ? string.Empty : propertyName;
            Sources = sources;
        }
        
        public new IDualNotifyPropertyChanged Source
        {
            get { return base.Source as IDualNotifyPropertyChanged; }
            private set { base.Source = value; }
        }

        private IEnumerable<IDualNotifyPropertyChanged> Sources { get; set; } 

        private string PropertyName { get; set; }

        public override IUmlTrigger Copy()
        {
            var copy = new MultiPropertyChangedTrigger(Name, Sources, PropertyName, Guard) { LogType = LogType };
            if (IsLive)
            {
                copy.Enable();
            }
            return copy;
        }

        protected override void Connect()
        {
            foreach (var s in Sources)
            {
                s.BusinessLayerPropertyChanged += HandlePropertyChanged;
            }

            // Not that this trigger is active, make it observe any objects that get added later.
            var observableSource = Sources as INotifyCollectionChanged;
            if (observableSource != null)
            {
                observableSource.CollectionChanged += HandleCollectionChanged;
            }
        }

        protected override void Disconnect()
        {
            foreach (var s in Sources)
            {
                s.BusinessLayerPropertyChanged -= HandlePropertyChanged;
            }
            
            var observableSource = Sources as INotifyCollectionChanged;
            if (observableSource != null)
            {
                observableSource.CollectionChanged -= HandleCollectionChanged;
            }
        }

        private void HandlePropertyChanged(object sender, PropertyChangedEventArgs args)
        {
            if (string.IsNullOrEmpty(PropertyName) || args.PropertyName.Equals(PropertyName))
            {
                Source = sender as IDualNotifyPropertyChanged;
                Trip();
            }
        }

        private void HandleCollectionChanged(object sender, NotifyCollectionChangedEventArgs args)
        {
            if (args.NewItems != null && args.NewItems.Count > 0)
            {
                foreach (var item in args.NewItems.OfType<IDualNotifyPropertyChanged>())
                {
                    item.BusinessLayerPropertyChanged += HandlePropertyChanged;
                }
            }
            else if (args.OldItems != null && args.OldItems.Count > 0)
            {
                foreach (var item in args.OldItems.OfType<IDualNotifyPropertyChanged>())
                {
                    item.BusinessLayerPropertyChanged -= HandlePropertyChanged;
                }
            }
        }
    }
}