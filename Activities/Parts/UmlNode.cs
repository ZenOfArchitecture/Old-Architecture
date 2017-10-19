using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Ventana.Core.Base.Activities;
using Ventana.Core.Logging;

namespace Ventana.Core.Activities.Parts
{
    public class UmlNode : IUmlNode
    {
        private readonly object IsDisposedLock = new Object();
        public bool IsDisposed { get; private set; }

        private readonly List<IUmlConnector> _connectors;

        public UmlNode(string name, string containerName)
        {
            Name = name;
            ContainerName = containerName;
            LogType = LogType;
            _connectors = new List<IUmlConnector>();
        }

        public event EventHandler<NodeEnteredEventArgs> Entered;
        public event EventHandler<EventArgs> Exited;

        public string Name { get; private set; }
        public LogType LogType { get; set; }
        public string ContainerName { get; private set; }
        /// <summary>
        /// Tells whether this node has been entered but has not been exited.
        /// </summary>
        public bool IsActive { get; protected set; }

        /// <summary>
        /// True when this node is not active.
        /// </summary>
        public virtual bool CanEnter
        {
            get { return !IsActive; }
        }

        /// <summary>
        /// True when this node is active.
        /// </summary>
        public virtual bool CanExit
        {
            get { return IsActive; }
        }

        public ReadOnlyCollection<IUmlConnector> Connectors
        {
            get { return _connectors.AsReadOnly(); }
        }

        protected object OriginOfEntry { get; private set; }
        protected TransitionEventArgs EntryArgs { get; private set; }

        // Public implementation of Dispose pattern callable by consumers. 
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
            if (disposing)
            {
                DisableConnectors();
                // Free any other managed objects here.
                foreach (var connector in _connectors)
                {
                    connector.Traversed -= HandleOutboundConnectorTraversed;
                    connector.Dispose();
                }
                _connectors.Clear();
            }

            // Free any unmanaged objects here.
            
        }

        public void AddConnector(IUmlConnector connector)
        {
            if (connector == null)
                return;

            _connectors.Add(connector);
            connector.Traversed += HandleOutboundConnectorTraversed;
        }

        public UmlTransition TransitionTo(IUmlNode nextNode)
        {
            var transition = new UmlTransition(ContainerName, this, nextNode) { LogType = LogType };
            AddConnector(transition);
            return transition;
        }

        /// <summary>
        /// Enter this Node from the given originator. It is the originator's responsibility
        /// to check whether this node CanEnter before calling this method.  It was done like that
        /// in order to prevent a Connector to Node traversal getting into an undefined state, which 
        /// could happen if both the Connector and the Node check CanEnter where the first check gives
        /// affirmative and the second gives negative.  Solution was to remove second check here.
        /// </summary>
        /// <param name="originator">the originator of the enter message</param>
        /// <param name="args">Optional event args related to the entry of this node.</param>
        public void EnterFrom(object originator, TransitionEventArgs args = null)
        {
            IsActive = true;
            OriginOfEntry = originator;
            EntryArgs = args;

            OnEntered();

            // Do whatever needs to be done for connectors on entry.
            EnableConnectors();

            // Call the virtual method that derived classes may modify.
            InternalEnter();
        }

        /// <summary>
        /// Try to exit this Node.
        /// </summary>
        /// <returns>true if node was exited</returns>
        public bool TryExit()
        {
            // Allow/Prevent exit based on the node's specific conditions, like a possible Postcondition.
            if (CanExit)
            {
                // Call the virtual method that derived classes may modify.
                return InternalTryExit();
            }
            return false;
        }

        public IUmlConnector FindConnectorTo(IUmlNode node)
        {
            return _connectors.FirstOrDefault(c => c.Consumer == node);
        }

        /// <summary>
        /// Try to exit this Node.
        /// This marks the node as not active, disables triggers and raises the Exited event.
        /// </summary>
        /// <returns>true</returns>
        protected virtual bool InternalTryExit()
        {
            IsActive = false;

            DisableConnectors();

            OnExited();
            return true;
        }

        /// <summary>
        /// Enter this node from the given originator.  
        /// This logs the entry.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        protected virtual void InternalEnter()
        {
            string origin = string.Empty;
            if (OriginOfEntry is IActivityMachine)
            {
                origin = (OriginOfEntry as IActivityMachine).Name;
            }
            else if (OriginOfEntry is StateMachine)
            {
                origin = (OriginOfEntry as StateMachine).Name;
            }
            else if (OriginOfEntry is IUmlConnector)
            {
                origin = (OriginOfEntry as IUmlConnector).Supplier.Name;
            }
            LogService.Log(LogType, LogMessageType.Trace, ContainerName,
                string.Format(CultureInfo.InvariantCulture, "Entry into '{0}' was from '{1}'.", Name, string.IsNullOrEmpty(origin) ? "unknown" : origin));
        }

        /// <summary>
        /// Empty base stub.
        /// </summary>
        internal virtual void DisableConnectors()
        {
            // let derived classes specify implementation.
        }

        /// <summary>
        /// Empty base stub.
        /// </summary>
        internal virtual void EnableConnectors()
        {
            // let derived classes specify implementation.
        }

        /// <summary>
        /// Raise the Entered event using the OriginOfEntry and TransitionEventArgs assigned to ConnectorArgs property.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        protected virtual void OnEntered()
        {
            if (Entered != null)
            {
                try
                {
                    LogService.Log(LogType, LogMessageType.Trace, ContainerName, "Entered node '" + Name + "'.");

                    Entered(this, new NodeEnteredEventArgs(OriginOfEntry) { ConnectorArgs = EntryArgs });
                }
                catch (Exception e)
                {
                    var msg = string.Format(CultureInfo.InvariantCulture, "{0} while entering Node '{1}'.\n{2}", e.GetType().Name, Name, e.StackTrace);
                    LogService.Log(LogType, LogMessageType.Error, ContainerName, msg, e);
                }
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters", MessageId = "Ventana.Core.Logging.LogService.Log(Ventana.Core.Logging.LogType,Ventana.Core.Logging.LogMessageType,System.String,System.String,System.Exception)")]
        protected virtual void OnExited()
        {
            if (Exited != null)
            {
                try
                {
                    LogService.Log(LogType, LogMessageType.Trace, ContainerName, "Exiting node '" + Name + "'.");

                    Exited(this, null);
                }
                catch (Exception e)
                {
                    var msg = string.Format(CultureInfo.InvariantCulture, "{0} while exiting Node '{1}'.\n{2}", e.GetType().Name, Name, e.StackTrace);
                    LogService.Log(LogType, LogMessageType.Error, ContainerName, msg, e);
                }
            }
        }

        private void HandleOutboundConnectorTraversed(object sender, EventArgs eventArgs)
        {
            //anything to do?
        }
    }
}
