using System;
using Ventana.Core.Activities.Parts;

namespace Ventana.Core.Activities.Executables
{
    public class ActOnResultActivity<TResult> : DynamicActivity
    {
        public ActOnResultActivity(string name, Func<TResult> action, TResult resultFilter, Action response)
            : this(name, resultFilter)
        {
            Action = action;
            Response = response;
        }

        protected ActOnResultActivity(string name, TResult resultFilter) : base(name)
        {
            ResultFilter = resultFilter;
        }

        public TResult Result { get; protected set; }

        public TResult ResultFilter { get; private set; }

        protected Action Response { get; set; }

        /// <summary>
        /// Gets the action to be executed by this Activity.
        /// </summary>
        protected new Func<TResult> Action { get; set; }

        /// <summary>
        /// Execute the action, hold the result, and potentially respond.
        /// </summary>
        protected override void RunAction()
        {
            Result = Action();
            
            if (!ReferenceEquals(null, ResultFilter) && ResultFilter.Equals(Result))
            {
                Response();
            }
        }
    }
}
