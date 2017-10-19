using System;
using System.Threading;
using System.Timers;
using Ventana.Core.Activities.Parts;
using Ventana.Core.Base.Activities;

namespace Ventana.Core.Activities.Executables
{
    public class WaitForConditionActivity<TTarget> : DynamicActivity
    {
        private readonly AutoResetEvent _internalWaitEvent = new AutoResetEvent(false);
        private int _timeoutMilliseconds = -1;

        public WaitForConditionActivity(string name, Func<TTarget, IUmlTrigger> getTriggerFunc, Func<TTarget, int> getTimeoutFunc, Action<TTarget> expiredAction, TTarget dataContext)
            : base(name)
        {
            DataContext = dataContext;

            GetTriggerFunc = getTriggerFunc;
            GetTimeoutFunc = getTimeoutFunc;
            ExpiredAction = expiredAction;
            Started += WaitForConditionActivity_Started;
        }

        protected TTarget DataContext { get; set; }
        protected Func<TTarget, IUmlTrigger> GetTriggerFunc { get; set; }
        protected Func<TTarget, int> GetTimeoutFunc { get; set; }
        protected Action<TTarget> ExpiredAction { get; set; }
        protected IUmlTrigger Trigger { get; set; }
        protected bool WasSignaled { get; set; }

        protected int TimeoutMilliseconds
        {
            get { return _timeoutMilliseconds; }
            set
            {
                if (value > 0)
                {
                    _timeoutMilliseconds = value;
                }
            }
        }

        void WaitForConditionActivity_Started(object sender, EventArgs e)
        {
            if (GetTimeoutFunc != null)
            {
                TimeoutMilliseconds = GetTimeoutFunc(DataContext);
            }

            Trigger = GetTriggerFunc(DataContext);
            Trigger.Tripped += HandleTriggerTripped;
            Trigger.Enable();
            Trigger.Trip();
        }

        public override void Execute()
        {
            try
            {
                OnExecutableStarted();
                RunAction();
                if (WasSignaled)
                {
                    OnExecutableFinished();
                }
                else
                {
                    OnExecutableExpired();
                }
            }
            catch (Exception e)
            {
                OnExecutableFaulted(e);
            }
            finally
            {
                Dispose();
            }
        }

        void HandleTriggerTripped(object sender, System.EventArgs e)
        {
            Trigger.Disable();
            _internalWaitEvent.Set();
        }

        protected override void RunAction()
        {
            //Wait for either the trigger to trip or the timer to expire
            WasSignaled = _internalWaitEvent.WaitOne(TimeoutMilliseconds);
            if (!WasSignaled)
            {
                TimeoutElapsed = true;
                Trigger.Disable();
                OnExpiredAction();
            }
        }

        private void OnExpiredAction()
        {
            if (ExpiredAction != null)
            {
                ExpiredAction(DataContext);
            }
        }

        protected override void Dispose(bool disposing)
        {
            Started -= WaitForConditionActivity_Started;
            if (Trigger != null)
            {
                Trigger.Tripped -= HandleTriggerTripped;
                Trigger = null;
            }
            DataContext = default(TTarget);
            GetTriggerFunc = null;
            GetTimeoutFunc = null;
            ExpiredAction = null;
            base.Dispose(disposing);
        }
    }
}
