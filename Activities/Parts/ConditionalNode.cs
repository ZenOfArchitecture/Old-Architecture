using System;
using System.Collections.Generic;
using System.Globalization;
using Ventana.Core.Base.Activities;
using Ventana.Core.Logging;

namespace Ventana.Core.Activities.Parts
{
    /// <summary>
    /// The ConditionalNode class supports Activity Machine framework.  It holds references to
    /// Quit and Continue transitions that are the halmark of the Activity Machine structure.
    /// </summary>
    public class ConditionalNode : StateNode
    {
        private Func<dynamic, IUmlConstraint> _createContinueConstraintDelegate;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="name"></param>
        /// <param name="containerName"></param>
        /// <param name="continueConstraint"></param>
        public ConditionalNode(string name, string containerName, IUmlConstraint continueConstraint)
            : base(name, containerName)
        {
            ContinueConstraint = null;
            if (continueConstraint != null)
            {
                var constraint = continueConstraint.Copy();
                ContinueConstraint = constraint;
                HasContinueConstraint = true;
            }
            OverridingTriggers = new List<IUmlTrigger>();
        }

        /// <summary>
        /// Constructs a ConditionalNode that acts like a basic activity without a condition.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="containerName"></param>
        /// <param name="onDo"></param>
        public ConditionalNode(string name, string containerName, IExecutable onDo)
            : base(name, containerName, onDo)
        {
            ContinueConstraint = null;
            OverridingTriggers = new List<IUmlTrigger>();
        }

        /// <summary>
        /// Indicates whether this node has a continue constraint that will be given to an
        /// outgoing continue transition.  When a continue constraint requires late binding,
        /// this node does not have the continue constraint until it is entered.
        /// </summary>
        public bool HasContinueConstraint { get; private set; }

        /// <summary>
        /// Gets or sets an optional constraint.  If supplied, it is given to the outgoing Continue
        /// transition to create the "wait until, then continue" effect.  A continue constraint
        /// adds an extra constraint to the default continuation constraint, so the combination must
        /// be true before the machine may enter the next non-final node.
        /// This can be null when there is a delegate supplied to generate the constraint.
        /// </summary>
        public IUmlConstraint ContinueConstraint { get; protected set; }

        /// <summary>
        /// A collection of triggers that, when present, will override the global triggers given
        /// to the parent machine for this node only.
        /// </summary>
        public List<IUmlTrigger> OverridingTriggers { get; private set; }

        /// <summary>
        /// The transition that leads to the next non-final activity node.  This can be null when
        /// this node is the last non-final node.
        /// </summary>
        public UmlTransition ContinueTransition { get; internal set; }

        /// <summary>
        /// The transition that leads to directly to the final node and causes the machine to be interrupted.
        /// This can be null when this node was not given a quit condition and the machine was not given
        /// a global quit condition.
        /// </summary>
        public UmlTransition QuitTransition { get; internal set; }

        /// <summary>
        /// Sometimes the continue constraint needs to have its evaluating action compiled when
        /// this node is entered so that all the values that go into evaluation are available.
        /// This delegate is used to perform the late binding. 
        /// </summary>
        internal Func<dynamic, IUmlConstraint> CreateContinueConstraintDelegate
        {
            get { return _createContinueConstraintDelegate; }
            set
            {
                _createContinueConstraintDelegate = value;
                HasContinueConstraint = (value != null);
            }
        }

        /// <summary>
        /// Node meta-data that could be anything.
        /// </summary>
        internal dynamic MetaData { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        protected override void InternalEnter()
        {
            base.InternalEnter();
            // Creation of the late-bound continue constraint must now occur.
            if (ContinueConstraint == null && CreateContinueConstraintDelegate != null)
            {
                ContinueConstraint = CreateContinueConstraintDelegate(MetaData);
                // Now need to AND the late-bound ContinueConstraint with the Continue transition's Guard.
                // Normally, the constraint is ANDed at build time in AddNode(), but in this case the constraint
                // didn't exist yet.
                var continueTransition = FindContinueTransition();
                continueTransition.Guard = continueTransition.Guard.AndWith(ContinueConstraint);
            }
            if (ContinueConstraint != null)
            {
                LogService.Log(LogType, LogMessageType.Debug, ContainerName,
                    string.Format(CultureInfo.InvariantCulture, "Waiting for condition '{0}'", ContinueConstraint.Name));
            }
        }
    }
}
