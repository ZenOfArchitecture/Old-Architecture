using System.ComponentModel;
using SimpleMvvmToolkit;
using Ventana.Core.Activities.Parts;
using Ventana.Core.Base.Activities;
using Ventana.Core.Utilities;

namespace Ventana.Core.Activities.SpecializedTriggers
{
    /// <summary>
    /// A trigger for the NonDispatchedPropertyChanged event of an IDualNotifyPropertyChanged or an INotifyPropertyChanged implementor.
    /// This trigger acts like a data binding in that it can respond to a chain of hierarchal 
    /// PropertyChanged events, as long as each object in the chain implements either INotifyPropertyChanged or IDualNotifyPropertyChanged.
    /// </summary>
    public class PropertyChangedTrigger : UmlTrigger
    {
        /// <summary>
        /// Create an unfiltered trigger that trips for all PropertyChanged events from the source.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="source"></param>
        public PropertyChangedTrigger(string name, INotifyPropertyChanged source)
            : this(name, source, null, null)
        {
        }

        /// <summary>
        /// Create a trigger that responds to PropertyChanged events on INotifyPropertyChanged implementations.
        /// Providing a value in propertyNameChain causes the trigger to only trip on a property name match.
        /// As long as every property in a chain references an INotifyPropertyChanged, the name may specify
        /// a nested property, such as "Object1.Object2.Object3.Property1"
        /// </summary>
        /// <param name="name"></param>
        /// <param name="source"></param>
        /// <param name="propertyNameChain">optional property name chain to use as a filter</param>
        public PropertyChangedTrigger(string name, INotifyPropertyChanged source, string propertyNameChain)
            : this(name, source, propertyNameChain, null)
        {
        }

        /// <summary>
        /// Create a trigger that responds to PropertyChanged events on INotifyPropertyChanged implementations.
        /// Providing a value in propertyNameChain causes the trigger to only trip on a property name match.
        /// As long as every property in a chain references an INotifyPropertyChanged, the name may specify
        /// a nested property, such as "Object1.Object2.Object3.Property1"
        /// </summary>
        /// <param name="name"></param>
        /// <param name="source"></param>
        /// <param name="propertyNameChain">optional property name chain to use as a filter</param>
        /// <param name="guard"></param>
        public PropertyChangedTrigger(string name, INotifyPropertyChanged source, string propertyNameChain, IUmlConstraint guard)
            : base(name, source, guard)
        {
            if (!string.IsNullOrEmpty(propertyNameChain) && propertyNameChain.Contains("."))
            {
                // Build the entire chain of PropertyBinding objects for the given nested properties.
                NestedObserver = new PropertyBinding(propertyNameChain);
                // Now set the observer references using the source object as the top-level parent.  NestedObserver's property is set now, ok to use that name.
                NestedObserver.NotificationSource = Source;
            }
            // Either way, hold on to the entire string.  For nested properties, this is needed for deep copying.
            PropertyName = propertyNameChain;
        }

        ~PropertyChangedTrigger()
        {
            Disconnect();
            if (NestedObserver != null)
            {
                NestedObserver.Dispose();
            }
        }

        public new INotifyPropertyChanged Source
        {
            get { return base.Source as INotifyPropertyChanged; }
            private set { base.Source = value; }
        }

        private string PropertyName { get; set; }

        private PropertyBinding NestedObserver { get; set; }

        public override IUmlTrigger Copy()
        {
            var copy = new PropertyChangedTrigger(Name, Source, PropertyName, Guard) { LogType = LogType };
            if (IsLive)
            {
                copy.Enable();
            }
            return copy;
        }

        protected override void Connect()
        {
            if (NestedObserver == null)
            {
                if (Source is IDualNotifyPropertyChanged)
                {
                    (Source as IDualNotifyPropertyChanged).BusinessLayerPropertyChanged += HandleSourcePropertyChanged;
                }
                else 
                {
                    Source.PropertyChanged += HandleSourcePropertyChanged;
                }
            }
            else
            {
                NestedObserver.Last.SourcePropertyChanged += HandleNestedPropertyChanged;
            }
        }

        protected override void Disconnect()
        {
            if (NestedObserver == null)
            {
                if (Source is IDualNotifyPropertyChanged)
                {
                    (Source as IDualNotifyPropertyChanged).BusinessLayerPropertyChanged -= HandleSourcePropertyChanged;
                }
                else
                {
                    Source.PropertyChanged -= HandleSourcePropertyChanged;
                }
            }
            else
            {
                if (NestedObserver.Last != null)
                {
                    NestedObserver.Last.SourcePropertyChanged -= HandleNestedPropertyChanged;
                }
            }
        }

        /// <summary>
        /// A PropertyChanged handler that is only hooked up if this does listen to a nested property.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void HandleNestedPropertyChanged(object sender, PropertyChangedEventArgs args)
        {
            Trip();
        }

        /// <summary>
        /// A PropertyChanged handler that is only hooked up if this does not listen to a nested property.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void HandleSourcePropertyChanged(object sender, PropertyChangedEventArgs args)
        {
            if (string.IsNullOrEmpty(PropertyName) || args.PropertyName.Equals(PropertyName))
            {
                Trip();
            }
        }
    }
}
