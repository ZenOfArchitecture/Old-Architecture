using SimpleMvvmToolkit;
using Ventana.Core.Base.Activities;

namespace Ventana.Core.Activities.Parts
{
    public class PausableNode : ConditionalNode
    {
        /// <summary>
        /// Create a new PausableNode that, as its Entry behavior, will pause the given machine if the PreventExit value is true.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="containerName"></param>
        /// <param name="machine"></param>
        public PausableNode(string name, string containerName, IActivityMachine machine) 
            : base(name, containerName, new EmptyConstraint())
        {
            // Use the base method because the override is empty on purpose.
            base.SetEnterBehavior(null, new DynamicActivity("Pause " + containerName, () =>
                {
                    if (PauseParentMachine)
                    {
                        machine.Pause();
                    }
                }));
        }

        /// <summary>
        /// Gets the index of this pausable node with respect to the activity machine's linked list of activities.
        /// </summary>
        public int Index { get; internal set; }
        
        /// <summary>
        /// Indicates whether this node breaks the machine's execution flow by preventing the machine from
        /// exiting out of here.
        /// </summary>
        internal bool PauseParentMachine { get; set; }

        /// <summary>
        /// Disallows public modification to the Enter behavior with an empty override.
        /// </summary>
        /// <param name="precondition"></param>
        /// <param name="enterBehavior"></param>
        public override void SetEnterBehavior(IUmlConstraint precondition, IExecutable enterBehavior)
        {
        }
    }
}
