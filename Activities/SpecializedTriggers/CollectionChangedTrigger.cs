using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using Ventana.Core.Activities.Parts;
using Ventana.Core.Base.Activities;
using Ventana.Core.Logging;

namespace Ventana.Core.Activities.SpecializedTriggers
{
    public class CollectionChangedTrigger : UmlTrigger
    {
        public CollectionChangedTrigger(string name, INotifyCollectionChanged source)
            : this(name, source, -1)
        {
        }

        public CollectionChangedTrigger(string name, INotifyCollectionChanged source, int targetCount)
            : this(name, source, targetCount, null)
        {
        }

        public CollectionChangedTrigger(string name, INotifyCollectionChanged source, int targetCount, IUmlConstraint guard)
            : base(name, source, guard)
        {
            TargetCount = targetCount;
        }

        public new INotifyCollectionChanged Source
        {
            get { return base.Source as INotifyCollectionChanged; }
            private set { base.Source = value; }
        }

        private int TargetCount { get; set; }

        public override IUmlTrigger Copy()
        {
            var copy = new CollectionChangedTrigger(Name, Source, TargetCount) { LogType = LogType };
            if (IsLive)
            {
                copy.Enable();
            }
            return copy;
        }

        protected override void Connect()
        {
            Source.CollectionChanged += HandleCollectionChanged;
        }

        protected override void Disconnect()
        {
            Source.CollectionChanged -= HandleCollectionChanged;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        private void HandleCollectionChanged(object sender, NotifyCollectionChangedEventArgs args)
        {
            if (args.NewItems != null && args.OldItems != null && args.NewItems.Count != args.OldItems.Count)
            {
                var collection = sender as ICollection;
                // Get a local copy of the count because making multiple queries in here seems to give different results.
                var itemCount = collection.Count;
                if (TargetCount < 0 || TargetCount == itemCount)
                {
                    LogService.Log(LogType.System, LogMessageType.Debug, Name,
                    string.Format(CultureInfo.InvariantCulture, "CollectionChangedTrigger tripping for item count change: {0} items remaining.",
                                  Source is ICollection ? collection.Count.ToString(CultureInfo.InvariantCulture) : "unknown"));
                    Trip();
                }
                else
                {
                    LogService.Log(LogType.System, LogMessageType.Debug, Name,
                    string.Format(CultureInfo.InvariantCulture, "CollectionChangedTrigger ignoring change on collection with {0} items.", collection.Count));
                }
            }
        }
    }
}
