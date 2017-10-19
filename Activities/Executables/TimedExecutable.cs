using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using MicroLibrary;
using Ventana.Core.Base.Activities;
using Ventana.Core.Base.Common;
using Ventana.Core.Logging;

namespace Ventana.Core.Activities.Executables
{
    public abstract class TimedExecutable : IExecutable, IDisposable
    {
        private readonly object _waitHandleLock = new object();
        protected ManualResetEvent _internalWaitEvent = new ManualResetEvent(false);

        protected TimedExecutable(string name, int timeoutMillis, bool expireSilently)
            : this(name, timeoutMillis)
        {
            ExpiresSilently = expireSilently;
        }

        protected TimedExecutable(string name, int timeoutMillis)
        {
            Name = name;
            TimeoutMilliseconds = timeoutMillis;
            ExpiresSilently = false;
            WaitWatcher = new MicroStopwatch();
            LogType = LogType;
            LogMessageType = LogMessageType.Trace;
        }

        ~TimedExecutable()
        {
            Dispose(false);
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

        public LogMessageType LogMessageType { get; set; }

        public int TimeoutMilliseconds { get; protected set; }

        public bool ExpiresSilently { get; set; }

        public virtual bool WasSignaled { get; protected set; }

        protected Stopwatch WaitWatcher { get; set; }

        /// <summary>
        /// Inherited from IExecutable
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        public void Execute()
        {
            try
            {
                OnExecutableStarted();
                WaitWatcher.Restart();
                RunAction();
                WasSignaled = _internalWaitEvent.WaitOne(TimeoutMilliseconds);
                if (WasSignaled || ExpiresSilently)
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
                LogService.Log(LogType, LogMessageType.Debug, GetType().Name,
                        String.Format(CultureInfo.InvariantCulture, "Unexpected {0}: {1}", e.GetType().Name, e.Message), e);
                OnExecutableFaulted(e);
            }
            finally
            {
                if (WaitWatcher.IsRunning)
                {
                    WaitWatcher.Stop();
                }
                LogService.Log(LogType, LogMessageType, GetType().Name,
                        string.Format(CultureInfo.InvariantCulture, "{0} waited for {1}ms.", Name, WaitWatcher.ElapsedMilliseconds));
                Dispose();
            }
        }

        /// <summary>
        /// Derived classes can override this to run the action in a unique way.
        /// </summary>
        protected abstract void RunAction();

        /// <summary>
        /// Signal this TimedExecutable that its dependency was satisfied before a timeout occurred.
        /// </summary>
        public void Continue()
        {
            WaitWatcher.Stop();
            lock (_waitHandleLock)
            {
                if (_internalWaitEvent != null)
                {
                    _internalWaitEvent.Set();
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            // Dispose managed resources
            if (disposing)
            {
                lock (_waitHandleLock)
                {
                    if (_internalWaitEvent != null)
                    {
                        _internalWaitEvent.Set();
                        _internalWaitEvent.Dispose();
                        _internalWaitEvent = null;
                    }
                }
            }
            // release unmanaged memory
        }

        #region Raising Events
        /// <summary>
        /// Raise the Finished event when the machine completes fully or prematurely.
        /// </summary>
        protected void OnExecutableFinished()
        {
            if (Finished != null)
            {
                Finished(this, new TimeStampedEventArgs());
            }
        }

        /// <summary>
        /// Raise the Expired event when the executable's expiration timer elapses or a node expires.
        /// </summary>
        protected virtual void OnExecutableExpired()
        {
            if (Expired != null)
            {
                Expired(this, new TimeStampedEventArgs());
            }
        }

        /// <summary>
        /// Raise the Faulted event.
        /// </summary>
        protected virtual void OnExecutableFaulted(Exception e)
        {
            if (Faulted != null)
            {
                Faulted(this, new FaultedEventArgs(e));
            }
        }

        protected virtual void OnExecutableStarted()
        {
            // Signal that this executable was started.
            if (Started != null)
            {
                Started(this, new TimeStampedEventArgs());
            }
        }
        #endregion
    }
}
