using System;
using Ventana.Core.Base.Activities;

namespace Ventana.Core.Activities.Parts
{
    public class EmptyConstraint : IUmlConstraint
    {
        public string Name { get { return "Always True"; } }

        public bool IsTrue()
        {
            return true;
        }

        public IUmlConstraint AndWith(IUmlConstraint andConstraint)
        {
            return andConstraint;
        }

        public IUmlConstraint OrWith(IUmlConstraint orConstraint)
        {
            return orConstraint;
        }

        public IUmlConstraint Copy()
        {
            return new EmptyConstraint();
        }
    }
}
