using System;

namespace Ventana.Core.Activities.Executables
{
    public class ActOnParamResultActivity<TConditionParam, TResult> : ActOnResultActivity<TResult>
    {
        public ActOnParamResultActivity(string name, TConditionParam conditionParam, Func<TConditionParam, TResult> action, TResult resultFilter, Action response)
            : base(name, resultFilter)
        {
            Action = action;
            Response = response;
            ConditionParam = conditionParam;
        }

        /// <summary>
        /// Gets or sets the target object for the parameter of the condition that guards execution of the submachine. 
        /// </summary>
        protected TConditionParam ConditionParam { get; set; }

        /// <summary>
        /// Gets the action to be executed by this Activity.
        /// </summary>
        protected new Func<TConditionParam, TResult> Action { get; set; }

        /// <summary>
        /// Execute the action, hold the result, and potentially respond.
        /// </summary>
        protected override void RunAction()
        {
            Result = Action(ConditionParam);

            if (!ReferenceEquals(null, ResultFilter) && ResultFilter.Equals(Result))
            {
                Response();
            }
        }
    }
    
}
