using System;
using System.Net;
using System.Threading;

namespace NTDLS.MemQueue
{
    /// <summary>
    /// For local IPC, this class Instantiates an in-process server if one is not running. If the instance
    /// running the server terminates, one of the connected clients will take over the role of server - however,
    /// messages queued at the server will be lost.
    /// </summary>
    public class NMQLocalOnly : IDisposable
    {
        #region Public properties.

        public delegate NMQServer ServerCreationCallback();
        public delegate NMQClient ClientCreationCallback();

        /// <summary>
        /// The TCP/IP port which will be used to communicate locally.
        /// </summary>
        public int Port { get; private set; }

        /// <summary>
        /// The client instance used for sending/receving messages.
        /// </summary>
        public NMQClient Client { get; private set; }

        /// <summary>
        /// If there is no server running, one will be Instantiated - if that occurs, this will be set.
        /// </summary>
        public NMQServer Server { get; private set; }

        /// <summary>
        /// Returns true if this instacne is acting as the server.
        /// </summary>
        public bool IsServer => Server != null;

        #endregion

        #region Private backend variables.

        private ServerCreationCallback _serverCallback;
        private ClientCreationCallback _clientCallback;

        private bool _isLocalServer = false;
        private Mutex _mutex = null;

        #endregion

        #region Constructors.

        public NMQLocalOnly()
        {
            Startup(NMQConstants.DEFAULT_PORT);
        }

        public NMQLocalOnly(ServerCreationCallback serverCallback)
        {
            _serverCallback = serverCallback;
            Startup(NMQConstants.DEFAULT_PORT);
        }

        public NMQLocalOnly(ClientCreationCallback clientCallback)
        {
            _clientCallback = clientCallback;
            Startup(NMQConstants.DEFAULT_PORT);
        }

        public NMQLocalOnly(ServerCreationCallback serverCallback, ClientCreationCallback clientCallback)
        {
            _serverCallback = serverCallback;
            _clientCallback = clientCallback;
            Startup(NMQConstants.DEFAULT_PORT);
        }

        public NMQLocalOnly(int port)
        {
            Startup(port);
        }

        public NMQLocalOnly(int port, ServerCreationCallback serverCallback)
        {
            _serverCallback = serverCallback;
            Startup(port);
        }

        public NMQLocalOnly(int port, ClientCreationCallback clientCallback)
        {
            _clientCallback = clientCallback;
            Startup(port);
        }

        public NMQLocalOnly(int port, ServerCreationCallback serverCallback, ClientCreationCallback clientCallback)
        {
            _serverCallback = serverCallback;
            _clientCallback = clientCallback;
            Startup(port);
        }

        #endregion

        private void Startup(int port)
        {
            Port = port;

            InstantiateClient();

            _mutex = new Mutex(true, $"NMQLocalOnly:{port}", out bool createdNew);
            _isLocalServer = createdNew;
            _isLocalServer = _mutex.WaitOne(0);

            if (_isLocalServer)
            {
                InstantiateServer();
            }

            Client.Connect(IPAddress.Parse("127.0.0.1"), port);
        }

        private void InstantiateServer()
        {
            if (_serverCallback != null)
            {
                Server = _serverCallback.Invoke();
            }
            else
            {
                Server = new NMQServer();
            }

            Server.Start(Port);
        }

        private void InstantiateClient()
        {
            if (_clientCallback != null)
            {
                Client = _clientCallback.Invoke();
            }
            else
            {
                Client = new NMQClient();
            }

            Client.OnDisconnected += Client_OnDisconnected;
        }

        private void Client_OnDisconnected(NMQClient sender)
        {
            try
            {
                //If we take the mutex, then we are the server now. If the server crashed, then the mutex will be abandoned.
                _isLocalServer = _mutex.WaitOne(0);
            }
            catch(AbandonedMutexException)
            {
                //The mutex was abandoned, see if we can take it now.
                _mutex = new Mutex(true, $"NMQLocalOnly:{Port}", out bool createdNew);
                _isLocalServer = _mutex.WaitOne(0);
            }

            if (_isLocalServer)
            {
                InstantiateServer();
            }
        }

        public void Dispose()
        {
            if (_mutex != null)
            {
                _mutex.ReleaseMutex();
                _mutex.Dispose();
                _mutex = null;
            }
        }
    }
}
