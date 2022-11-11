﻿using NTDLS.MemQueue.Library;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace NTDLS.MemQueue
{
    /// <summary>
    /// Server class that listens for clients and routes messages between them.
    /// </summary>
    public class NMQServer : NMQBase
    {
        /// <summary>
        /// User data, use as you will.
        /// </summary>
        public object Tag { get; set; }

        /// <summary>
        /// The port that server eas started on.
        /// </summary>
        public object ListenPort { get; private set; }

        /// <summary>
        /// The number of seconds to allow an item to remain in the queue after being added. 0 = infinite.
        /// </summary>
        public int StaleItemExpirationSeconds { get; set; } = NMQConstants.DEFAULT_STALE_EXPIRATION_SECONDS;

        /// <summary>
        /// Whether the servers should broadcast the messages as soon as they are received or buffer them for distribution by a pump thread.
        /// </summary>
        public bool BroadcastMessagesAsReceived { get; set; } = true;

        #region Backend Variables.

        private bool _continueRunning = false;
        private readonly int _listenBacklog = NMQConstants.DEFAULT_TCPIP_LISTEN_SIZE;
        private Socket _listenSocket;
        private readonly List<Peer> _peers = new List<Peer>();
        private AsyncCallback _onDataReceivedCallback;
        private readonly Dictionary<string, List<Socket>> _subscriptions = new Dictionary<string, List<Socket>>();
        private readonly object _broadcastMessagesLock = new object();
        private readonly Dictionary<string, ListQueue<NMQMessageBase>> _queues = new Dictionary<string, ListQueue<NMQMessageBase>>();

        #endregion

        #region Ctor/~.

        public NMQServer()
        {
        }

        public NMQServer(int listenBacklog)
        {
            this._listenBacklog = listenBacklog;
        }

        #endregion

        #region Management.

        public Dictionary<string, List<Socket>> Subscriptions
        {
            get
            {
                return _subscriptions;
            }
        }

        /// <summary>
        /// The number of queues.
        /// </summary>
        public int QueueCount
        {
            get
            {
                return _queues.Sum(o => o.Value.Count);
            }
        }

        /// <summary>
        /// The number of messages in a single queue.
        /// </summary>
        public int QueueDepth(string name)
        {
            return _queues[name].Count;
        }

        /// <summary>
        /// The number of messages in all queues.
        /// </summary>
        public int QueueDepth()
        {
            try
            {
                Monitor.Enter(_broadcastMessagesLock);
                return _queues.Sum(o => o.Value.Count);
            }
            catch
            {
                throw;
            }
            finally
            {
                Monitor.Exit(_broadcastMessagesLock);
            }
        }

        public Dictionary<string, ListQueue<NMQMessageBase>> Queues
        {
            get
            {
                return _queues;
            }
        }

        #endregion

        #region Start/Stop.

        /// <summary>
        /// Starts the server on the default port.
        /// </summary>
        public void Start()
        {
            Start(NMQConstants.DEFAULT_PORT);
        }

        /// <summary>
        /// Starts the server on the specified port.
        /// </summary>
        /// <param name="listenPort"></param>
        public void Start(int listenPort)
        {
            ListenPort = listenPort;

            _listenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            _listenSocket.Bind(new IPEndPoint(IPAddress.Any, listenPort));
            _listenSocket.Listen(_listenBacklog);
            _listenSocket.BeginAccept(new AsyncCallback(ClientConnectProc), null);

            _continueRunning = true;

            new Thread(BroadcastThread).Start();
        }

        /// <summary>
        /// Stops the server.
        /// </summary>
        public void Stop()
        {
            _continueRunning = false;

            try
            {
                if (_listenSocket != null)
                {
                    _listenSocket.Disconnect(false);
                    _listenSocket.Shutdown(SocketShutdown.Both);
                    _listenSocket.Dispose();
                }
            }
            catch (Exception ex)
            {
                LogException(ex);
            }

            try
            {
                var peers = _peers.ToList();

                foreach (var peer in peers)
                {
                    try
                    {
                        peer.Socket.Disconnect(false);
                    }
                    catch (Exception ex)
                    {
                        LogException(ex);
                    }
                }

                _peers.Clear();
            }
            catch (Exception ex)
            {
                LogException(ex);
            }
        }

        #endregion

        #region Events.
        public override void LogException(Exception ex)
        {
            OnExceptionOccured?.Invoke(this, ex);
        }

        /// <summary>
        /// Allows excpetions to be logged.
        /// </summary>
        public event ExceptionOccuredEvent OnExceptionOccured;
        public delegate void ExceptionOccuredEvent(NMQServer sender, Exception exception);

        public event OnClientConnectEvent OnClientConnect;
        public delegate ClientConnectAction OnClientConnectEvent(NMQServer sender, Socket socket);

        public event OnBeforeCommandReceiveEvent OnBeforeMessageReceive;
        public delegate PayloadReceiveAction OnBeforeCommandReceiveEvent(NMQServer sender, NMQMessageBase message);

        public event OnBeforeCommandSendEvent OnBeforeMessageSend;
        public delegate PayloadSendAction OnBeforeCommandSendEvent(NMQServer sender, NMQMessageBase message);

        #endregion

        #region Socket server.

        private void BroadcastThread()
        {
            //Messaes are broadcast as they are received - but there are instances where we only have sender
            //  clients and no subscribers so we will need to buffer the messages and periodiacally attempt to broadcast them.
            while (_continueRunning)
            {
                BroadcastMessages();
                Thread.Sleep(10);
            }
        }

        private void ClientConnectProc(IAsyncResult asyn)
        {
            try
            {
                Monitor.Enter(_broadcastMessagesLock);

                Socket socket = _listenSocket.EndAccept(asyn);
                var peer = new Peer(socket);

                lock (this)
                {
                    var action = OnClientConnect?.Invoke(this, socket);
                    if (action == ClientConnectAction.Reject)
                    {
                        CleanupConnection(socket);
                        return;
                    }

                    _peers.Add(peer);
                }

                // Let the worker Socket do the further processing for the just connected client.
                WaitForData(peer);

                _listenSocket.BeginAccept(new AsyncCallback(ClientConnectProc), null);
            }
            catch (Exception ex)
            {
                LogException(ex);
            }
            finally
            {
                Monitor.Exit(_broadcastMessagesLock);
            }
        }

        private void WaitForData(Peer peer)
        {
            if (_onDataReceivedCallback == null)
            {
                _onDataReceivedCallback = new AsyncCallback(OnDataReceived);
            }

            peer.Socket.BeginReceive(peer.Buffer, 0, peer.Buffer.Length, SocketFlags.None, _onDataReceivedCallback, peer);
        }

        private void OnDataReceived(IAsyncResult asyn)
        {
            Socket socket = null;

            try
            {
                Peer peer = (Peer)asyn.AsyncState;

                socket = peer.Socket;

                peer.BytesReceived = peer.Socket.EndReceive(asyn);

                if (peer.BytesReceived == 0)
                {
                    CleanupConnection(peer.Socket);
                    return;
                }

                Packetizer.DissasemblePacketData(this, peer, CommandHandler);

                if (BroadcastMessagesAsReceived)
                {
                    BroadcastMessages();
                }

                WaitForData(peer);
            }
            catch (ObjectDisposedException)
            {
                CleanupConnection(socket);
                return;
            }
            catch (SocketException)
            {
                CleanupConnection(socket);
                return;
            }
            catch (Exception ex)
            {
                LogException(ex);
            }
        }

        private void CleanupConnection(Socket socket)
        {
            try
            {
                Monitor.Enter(_broadcastMessagesLock);

                lock (this)
                {
                    try
                    {
                        try
                        {
                            socket?.Shutdown(SocketShutdown.Both);
                        }
                        finally
                        {
                            socket?.Close();
                        }
                    }
                    catch
                    {
                        //Discard.
                    }

                    _peers.RemoveAll(o => o.Socket == socket);
                    RemoveAllSubscriptions(socket);

                    socket.Dispose();
                }
            }
            catch (Exception ex)
            {
                LogException(ex);
            }
            finally
            {
                Monitor.Exit(_broadcastMessagesLock);
            }
        }

        private void RemoveAllSubscriptions(Socket socket)
        {
            try
            {
                lock (this)
                {
                    foreach (string queueName in _subscriptions.Keys)
                    {
                        try
                        {
                            _subscriptions[queueName].RemoveAll(o => o == socket);
                        }
                        catch
                        {
                            //Discard.
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogException(ex);
            }
        }

        #endregion

        #region Commands.

        private void BroadcastMessages()
        {
            if (Monitor.TryEnter(_broadcastMessagesLock))
            {
                try
                {
                    DateTime staleTime = DateTime.UtcNow.AddSeconds(-StaleItemExpirationSeconds);

                    //Remove stale and expired items.
                    DateTime now = DateTime.UtcNow;
                    foreach (var queue in _queues)
                    {
                        if (StaleItemExpirationSeconds > 0)
                        {
                            //queue.Value.RemoveAll(o => o.CreatedTime < staleTime);
                        }

                        //queue.Value.RemoveAll(o => now > o.ExpireTime);
                    }

                    var erroredSockets = new List<Socket>();

                    List<string> queueNames = null;

                    lock (this)
                    {
                        queueNames = _subscriptions.Keys.ToList();
                    }

                    foreach (string queueName in queueNames)
                    {
                        if (_queues.ContainsKey(queueName) == false)
                        {
                            continue; //The queue is empty.
                        }

                        var queue = _queues[queueName];

                        if (queue == null || queue.Count == 0)
                        {
                            continue;
                        }

                        List<Socket> sockets = null;

                        lock (this)
                        {
                            sockets = _subscriptions[queueName];
                        }

                        if (sockets == null || sockets.Count == 0)
                        {
                            continue;
                        }

                        int messagesSent = -1;

                        while (queue.Count > 0 && messagesSent != 0)
                        {
                            NMQMessageBase queueItem = queue.Peek();

                            messagesSent = 0;
                            foreach (Socket socket in sockets)
                            {
                                try
                                {
                                    var payload = new NMQCommand()
                                    {
                                        Message = queueItem,
                                        CommandType = PayloadCommandType.ProcessMessage
                                    };

                                    byte[] messagePacket = Packetizer.AssembleMessagePacket(this, payload);

                                    if (socket.Connected && messagePacket != null)
                                    {
                                        var action = OnBeforeMessageSend?.Invoke(this, payload.Message);

                                        if (action == PayloadSendAction.Discard)
                                        {
                                            messagesSent++; //Make sure the message is removed from the queue.
                                            continue;
                                        }
                                        else if (action == PayloadSendAction.Skip)
                                        {
                                            continue; //Skip sending to this client.
                                        }

                                        //socket.SendAsync(messagePacket, SocketFlags.None);
                                        socket.Send(messagePacket, SocketFlags.None);

                                        //socket.Send(messagePacket);
                                        messagesSent++;
                                    }
                                }
                                catch (SocketException)
                                {
                                    erroredSockets.Add(socket);
                                }
                                catch (Exception ex)
                                {
                                    LogException(ex);
                                }
                            }

                            if (messagesSent > 0)
                            {
                                lock (this)
                                {
                                    queue.Dequeue();
                                }
                            }
                        }
                    }

                    while (erroredSockets.Count > 0)
                    {
                        lock (this)
                        {
                            CleanupConnection(erroredSockets[0]);
                            erroredSockets.RemoveAt(0);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogException(ex);
                }
                finally
                {
                    Monitor.Exit(_broadcastMessagesLock);
                }
            }
        }

        private void CommandHandler(Peer peer, NMQCommand payload)
        {
            try
            {
                Monitor.Enter(_broadcastMessagesLock);

                var action = OnBeforeMessageReceive?.Invoke(this, payload.Message);

                if (action == PayloadReceiveAction.Discard)
                {
                    return;
                }

                if (payload.CommandType == PayloadCommandType.Enqueue)
                {
                    if (_queues.ContainsKey(payload.Message.QueueName) == false)
                    {
                        _queues.Add(payload.Message.QueueName, new ListQueue<NMQMessageBase>());
                    }

                    var queue = _queues[payload.Message.QueueName];
                    _queues[payload.Message.QueueName].Enqueue(payload.Message);
                }
                else if (payload.CommandType == PayloadCommandType.Clear)
                {
                    if (_queues.ContainsKey(payload.Message.QueueName) == false)
                    {
                        _queues.Add(payload.Message.QueueName, new ListQueue<NMQMessageBase>());
                    }

                    var queue = _queues[payload.Message.QueueName];
                    _queues[payload.Message.QueueName].Clear();
                }
                else if (payload.CommandType == PayloadCommandType.Subscribe)
                {
                    if (_subscriptions.ContainsKey(payload.Message.QueueName) == false)
                    {
                        _subscriptions.Add(payload.Message.QueueName, new List<Socket>());
                    }

                    if (_subscriptions[payload.Message.QueueName].Contains(peer.Socket) == false)
                    {
                        _subscriptions[payload.Message.QueueName].Add(peer.Socket);
                    }
                }
                else if (payload.CommandType == PayloadCommandType.UnSubscribe)
                {
                    if (_subscriptions.ContainsKey(payload.Message.QueueName) == false)
                    {
                        return; //The queue has no subscriptions.
                    }

                    _subscriptions[payload.Message.QueueName].RemoveAll(o => o == peer.Socket);

                    if (_subscriptions[payload.Message.QueueName].Count == 0)
                    {
                        _subscriptions.Remove(payload.Message.QueueName);
                    }
                }
                else
                {
                    throw new Exception("Command type is not implemented.");
                }
            }
            catch (Exception ex)
            {
                LogException(ex);
            }
            finally
            {
                Monitor.Exit(_broadcastMessagesLock);
            }

        }

        #endregion
    }
}