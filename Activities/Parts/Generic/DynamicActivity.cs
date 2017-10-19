using System;

namespace Ventana.Core.Activities.Parts.Generic
{
    public class DynamicActivity<TParam> : DynamicActivity
    {
        public DynamicActivity(string name, Action<TParam> action) : base(name)
        {
            Action = action;
        }

        public DynamicActivity(string name, Action<TParam> action, TParam argument) : this(name, action)
        {
            Argument = argument;
        }

        protected new Action<TParam> Action { get; set; }

        /// <summary>
        /// Gets or sets the argument to give to this DynamicActivity's action.
        /// </summary>
        public TParam Argument { get; set; }

        /// <summary>
        /// Derived classes can override this to run the action in a unique way.
        /// </summary>
        protected override void RunAction()
        {
            // Execute the delegate with the given parameter.
            Action(Argument);
        }
    }
}
