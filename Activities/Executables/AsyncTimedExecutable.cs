using System;
using System.Timers;
using Ventana.Core.Base.Activities;
using Ventana.Core.Base.Common;
using Ventana.Core.Logging;
using Timer = System.Timers.Timer;

namespace Ventana.Core.Activities.Executables
{
    public abstract class AsyncTimedExecutable : IExecutable
    {
        private readonly object IsDisposedLock = new Object();
        public bool IsDisposed { get; private set; }
        private Timer _expirationTimer = null;

        protected AsyncTimedExecutable(string name)
        {
            Name = name;
            LogType = LogType.System;
        }

        /// <summary>
        /// Raised when this IExecutable expires via its internal timer.
        /// </summary>
        public event EventHandler<TimeStampedEventArgs> Expired;

        /// <summary>
        /// Raised when this machine is told to start.
        /// </summary>
        public event EventHandler<TimeStampedEventArgs> Started;

        /// <summary>
        /// Raised when all of this machine's activities have been executed.  In synchronous mode,
        /// this is when all activities are finished.  In asynchronous mode, it's when all activities
        /// have been dispatched.
        /// </summary>
        public event EventHandler<TimeStampedEventArgs> Finished;

        /// <summary>
        /// Raised when this machine is interrupted prematurely by encountering an error.
        /// </summary>
        public event EventHandler<FaultedEventArgs> Faulted;

        /// <summary>
        /// Gets or sets this Executable's unique identifier.
        /// </summary>
        public Guid Id { get; set; }

        public string Name { get; set; }

        public LogType LogType { get; set; }

        public bool TimeoutElapsed { get; protected set; }

        public bool IsTiming
        {
            get
            {
                if (_expirationTimer == null)
                {
                    return false;
                }
                return _expirationTimer.Enabled;
            }
        }

        /// <summary>
        /// Gets or sets the timespan that this IExecutable uses to self-expire.
        /// Setting a positive timespan will enable a timer that causes this machine to quit.
        /// If a TimeSpan of 0 or less milliseconds is given, no expiration will occur.
        /// </summary>
        public TimeSpan ExpirationTimespan
        {
            //TODO: make a BusyExpirationTimespan and a IdleExpirationTimespan?
            get
            {
                if (_expirationTimer == null)
                {
                    return TimeSpan.Zero;
                }
                return TimeSpan.FromMilliseconds(_expirationTimer.Interval);
            }
            set
            {
                if (value.TotalMilliseconds > 0)
                {
                    if (_expirationTimer == null)
                    {
                        _expirationTimer = new Timer(value.TotalMilliseconds);
                        _expirationTimer.AutoReset = false;
                        _expirationTimer.Elapsed += HandleExpirationTimerElapsed;
                    }
                }
                else if (_expirationTimer != null)
                {
                    _expirationTimer.Interval = 0;
                    _expirationTimer.Enabled = false;
                }
            }
        }

        public abstract void Execute();

        /// <summary>
        /// Start the expiration timer, if there is one.
        /// </summary>
        protected void StartTiming()
        {
            if (_expirationTimer != null)
            {
                _expirationTimer.Enabled = true;
            }
        }

        protected void StopTiming()
        {
            if (_expirationTimer != null)
            {
                _expirationTimer.Enabled = false;
            }
        }

        protected virtual void HandleExpirationTimerElapsed(object sender, ElapsedEventArgs args)
        {
            StopTiming();
            TimeoutElapsed = true;
            OnExecutableExpired();
        }

        #region Raising Events
        /// <summary>
        /// Raise the Finished event when the executable completes.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        protected void OnExecutableFinished()
        {
            if (Finished != null)
            {
                try
                {
                    Finished(this, new TimeStampedEventArgs());
                }
                catch (Exception e)
                {
                    LogService.Log(LogType, LogMessageType.Error, GetType().ToString(), "Executable '" + Name + "' encountered exception while raising Finished event: " + e.Message, e);
                }
            }
        }

        /// <summary>
        /// Raise the Expired event when the executable's expiration timer elapses or a node expires.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        protected virtual void OnExecutableExpired()
        {
            if (Expired != null)
            {
                try
                {
                    Expired(this, new TimeStampedEventArgs());
                }
                catch (Exception e)
                {
                    LogService.Log(LogType, LogMessageType.Error, GetType().ToString(), "Executable '" + Name + "' encountered exception while raising Expired event: " + e.Message, e);
                }
            }
        }

        /// <summary>
        /// Raise the Faulted event.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        protected virtual void OnExecutableFaulted(Exception e)
        {
            if (Faulted != null)
            {
                try
                {
                    Faulted(this, new FaultedEventArgs(e));
                }
                catch (Exception ex)
                {
                    LogService.Log(LogType, LogMessageType.Error, GetType().ToString(), "Executable '" + Name + "' encountered exception while raising Faulted event: " + ex.Message, ex);
                }
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        protected virtual void OnExecutableStarted()
        {
            // Signal that this executable was started.
            if (Started != null)
            {
                try
                {
                    Started(this, new TimeStampedEventArgs());
                }
                catch (Exception e)
                {
                    LogService.Log(LogType, LogMessageType.Error, GetType().ToString(), "Executable '" + Name + "' encountered exception while raising Started event: " + e.Message, e);
                }
            }
        }
        #endregion

        public void Dispose()
        {
            lock (IsDisposedLock)
            {
                if (IsDisposed) return;
                IsDisposed = true;
            }


            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            // Dispose managed resources
            if (disposing)
            {
                StopTiming();
                if (_expirationTimer != null)
                {
                    _expirationTimer.Dispose();
                    _expirationTimer = null;
                }

            }
            // release unmanaged memory
        }
    }
}
