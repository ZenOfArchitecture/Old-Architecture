using System.Linq;
using Ventana.Core.Base;

namespace Ventana.Core.Activities.Extensions
{
    public static class ActivitiesExtensions
    {
        public static bool IsNoneOf(this CompletionCause state, params CompletionCause[] notTheseStates)
        {
            return notTheseStates.All(otherState => otherState != state);
        }

        public static bool IsOneOf(this CompletionCause state, params CompletionCause[] theseStates)
        {
            return theseStates.Any(otherState => otherState == state);
        }
    }
}
