using System;
using System.Collections.Generic;
using System.Globalization;
using Ventana.Core.Base.Activities;
using Ventana.Core.Logging;

namespace Ventana.Core.Activities.Parts
{
    public class DynamicConstraint<TTarget> : IUmlConstraint
    {
        public DynamicConstraint(string name, TTarget target, Func<TTarget, bool> condition)
        {
            Name = name;
            Target = target;
            Condition = condition;
            LogType = LogType.System;
        }

        /// <summary>
        /// The Target on which this DynamicConstraint will be evaluated.
        /// </summary>
        public TTarget Target { get; protected set; }

        /// <summary>
        /// The Function to be evaluated on this DynamicConstraint's target.
        /// </summary>
        public Func<TTarget, bool> Condition { get; protected set; }

        public LogType LogType { get; set; }

        public bool SuppressLogging { get; set; }

        protected IUmlConstraint RightConstraint { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        public bool IsTrue()
        {
            if (Target != null)
            {
                try
                {
                    return Evaluate();
                }
                catch (Exception ex)
                {
                    LogService.Log(LogType, LogMessageType.Error, GetType().Name,
                        string.Format(CultureInfo.InvariantCulture, "Exception while evaluating '{0}' constraint.", Name), ex);
                    throw;
                }
            }
            LogService.Log(LogType, LogMessageType.Error, GetType().Name,
                string.Format(CultureInfo.InvariantCulture, "'{0}' cannot be evaluated on a null target.", Name));
            return false;
        }

        public string Name { get; private set; }

        /// <summary>
        /// Set an additional constraint to be AND-ed logically with this constraint.
        /// This uses a clone of the incoming constraint.
        /// </summary>
        /// <param name="andConstraint">The constraint to AND with this constraint</param>
        /// <returns>IUmlConstraint</returns>
        public IUmlConstraint AndWith(IUmlConstraint andConstraint)
        {
            if (andConstraint != null)
            {
                RightConstraint = andConstraint;
                var binaryAnd = new DynamicConstraint<DynamicConstraint<TTarget>>(Name + " && " + RightConstraint.Name, this,
                    (target) => target.IsTrue() && target.RightConstraint.IsTrue()) { LogType = LogType };
                return binaryAnd;
            }
            return this;
        }

        /// <summary>
        /// Set an additional constraint to be OR-ed logically with this constraint.
        /// This uses a clone of the incoming constraint.
        /// </summary>
        /// <param name="orConstraint">The constraint to OR with this constraint</param>
        /// <returns>IUmlConstraint</returns>
        public IUmlConstraint OrWith(IUmlConstraint orConstraint)
        {
            if (orConstraint != null)
            {
                RightConstraint = orConstraint;
                var binaryOr = new DynamicConstraint<DynamicConstraint<TTarget>>(Name + " || " + RightConstraint.Name, this,
                    (target) => target.IsTrue() || target.RightConstraint.IsTrue()) { LogType = LogType };
                return binaryOr;
            }
            return this;
        }

        public virtual IUmlConstraint Copy()
        {
            var copy = new DynamicConstraint<TTarget>(Name, Target, Condition) { LogType = LogType };
            copy.RightConstraint = RightConstraint;
            copy.SuppressLogging = SuppressLogging;
            return copy;
        }

        /// <summary>
        /// This allows a derived class to alter the way this constraint's evaluation is performed.
        /// </summary>
        /// <returns>bool</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        protected virtual bool Evaluate()
        {
            if (Condition != null)
            {
                var result = Condition(Target);
                if (!SuppressLogging && result && !string.IsNullOrEmpty(Name) && !Name.Contains(" && ") && !Name.Contains(" || "))
                {
                    LogService.Log(LogType, LogMessageType.Trace, GetType().Name,
                        string.Format(CultureInfo.InvariantCulture, "Condition was satisfied for '{0}' constraint.", Name));
                }
                return result;
            }
            return false;
        }
    }
}
