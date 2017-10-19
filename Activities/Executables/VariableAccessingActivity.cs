using System;
using Ventana.Core.Activities.Parts;

namespace Ventana.Core.Activities.Executables
{
    public class VariableAccessingActivity : DynamicActivity
    {
        public VariableAccessingActivity(string name, Action<dynamic> action, dynamic dataContext)
            : base(name)
        {
            Action = action;
            DataContext = dataContext;
        }

        protected new Action<dynamic> Action { get; set; }

        protected dynamic DataContext { get; set; }

        /// <summary>
        /// Derived classes can override this to run the action in a unique way.
        /// </summary>
        protected override void RunAction()
        {
            // Execute the delegate that results from compiling the expression now.
            Action(DataContext);
        }
        protected override void Dispose(bool disposing)
        {
            DataContext = null;
            Action = null;
            base.Dispose(disposing);
        }
    }
}
