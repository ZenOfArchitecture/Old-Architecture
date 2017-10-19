using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace Ventana.Core.Activities.Executables
{
    public class ActOnResultConcurrentActivity<TResult> : ConcurrentActivity
    {
        public ActOnResultConcurrentActivity(string name, string executionDescription, Func<CancellationToken, TResult> action, TResult resultFilter, Action response)
            : base(name, executionDescription)
        {
            ResultFilter = resultFilter;
            Action = action;
            Response = response;
        }

        public TResult Result { get; protected set; }

        public TResult ResultFilter { get; private set; }

        protected Action Response { get; private set; }

        /// <summary>
        /// Gets the action to be executed by this Activity.
        /// </summary>
        protected new Func<CancellationToken, TResult> Action { get; set; }

        /// <summary>
        /// Execute the action, hold the result, and potentially respond.
        /// </summary>
        protected override void RunAction()
        {
            Result = Action(CancellationToken);

            if (!CancellationToken.IsCancellationRequested && ResultFilter != null && ResultFilter.Equals(Result))
            {
                Response();
            }
        }
    }
}
