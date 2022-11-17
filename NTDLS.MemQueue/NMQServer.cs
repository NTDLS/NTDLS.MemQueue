using NTDLS.MemQueue.Library;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace NTDLS.MemQueue
{
    /// <summary>
    /// Server class that listens for client connections and routes messages between them.
    /// </summary>
    public class NMQServer : NMQBase
    {
        #region Public Properties.

        /// <summary>
        /// The time in milliseconds to wait for an acknowledgement that a dispatched request has been processed.
        /// </summary>
        public int ProcessTimeout { get; set; } = NMQConstants.DEFAULT_PROCESS_TIMEOUT_MS;

        /// <summary>
        /// The time in milliseconds to wait for an acknowledgement that a dispatched request has been received.
        /// </summary>
        public int AckTimeout { get; set; } = NMQConstants.DEFAULT_ACK_TIMEOUT_MS;

        /// <summary>
        /// User data, use as you will.
        /// </summary>
        public object Tag { get; set; }

        /// <summary>
        /// When true, the server will drop messages sent to a queue which has no subscribers.
        /// </summary>
        public bool DoNotQueueWhenNoConsumers { get; set; } = false;

        /// <summary>
        /// The port that server eas started on.
        /// </summary>
        public object ListenPort { get; private set; }

        /// <summary>
        /// The number of commands that have been dispatched and have not been acknowledged by a client.
        /// </summary>
        public int PresumedDeadCommandCount { get; private set; }

        /// <summary>
        /// The number of seconds to allow an item to remain in the queue after being added. 0 = infinite.
        /// </summary>
        public int StaleItemExpirationSeconds { get; set; } = NMQConstants.DEFAULT_STALE_EXPIRATION_SECONDS;

        /// <summary>
        /// Whether the servers should broadcast the messages as soon as they are received or buffer them for distribution by a pump thread.
        /// </summary>
        public bool BroadcastMessagesImmediately { get; set; } = true;

        /// <summary>
        /// The number of initiated async TCP sends that have not completed.
        /// </summary>
        public int TCPSendQueueDepth { get; private set; }

        private BrodcastScheme _brodcastScheme = BrodcastScheme.NotSet;
        public BrodcastScheme BrodcastScheme
        {
            get
            {
                return _brodcastScheme;
            }
            set
            {
                if (_brodcastScheme != BrodcastScheme.NotSet)
                {
                    throw new Exception("Broadcast scheme can not be changed after being set.");
                }
                _brodcastScheme = value;
            }
        }

        #endregion

        #region Backend Variables.

        private Dictionary<string, NMQACKEvent> _commandAckEvents = new Dictionary<string, NMQACKEvent>();
        private Dictionary<string, NMQACKEvent> _processedEvents = new Dictionary<string, NMQACKEvent>();
        private bool _continueRunning = false;
        private readonly int _listenBacklog = NMQConstants.DEFAULT_TCPIP_LISTEN_SIZE;
        private Socket _listenSocket;
        private readonly List<Peer> _peers = new List<Peer>();
        private AsyncCallback _onDataReceivedCallback;
        private readonly Dictionary<string, List<Peer>> _subscriptions = new Dictionary<string, List<Peer>>();
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
        /// The number commands sent that the server has yet to receive an acknowledgment to receiving.
        /// </summary>
        public int OutstandingCommandAcknowledgments
        {
            get
            {
                lock (_commandAckEvents)
                    return _commandAckEvents.Count();
            }
        }

        /// <summary>
        /// The number of messages sent that a peer has yet to acknowledge processing.
        /// </summary>
        public int OutstandingProcessedAcknowledgments
        {
            get
            {
                lock (_processedEvents)
                    return _processedEvents.Count();
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

            if (_brodcastScheme == BrodcastScheme.NotSet)
            {
                this.BrodcastScheme = BrodcastScheme.Uniform;
            }

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
        /// <summary>
        /// Allows excpetions to be logged.
        /// </summary>
        /// <param name="sender">The instance of the server which sent the event.</param>
        /// <param name="exception">The exception which occured.</param>
        public delegate void ExceptionOccuredEvent(NMQServer sender, Exception exception);

        public event OnClientConnectEvent OnClientConnect;
        /// <summary>
        /// Triggered when a peer is connecting to the server.
        /// </summary>
        /// <param name="sender">The instance of the server which sent the event.</param>
        /// <param name="socket">The socket of the remote peer.</param>
        /// <returns>whether to accept or reject the connection.</returns>
        public delegate ClientConnectAction OnClientConnectEvent(NMQServer sender, Socket socket);

        /// <summary>
        /// Triggered before a message is received by the server
        /// </summary>
        public event OnBeforeCommandReceiveEvent OnBeforeMessageReceive;
        /// <summary>
        /// Triggered before a message is received by the server
        /// </summary>
        /// <param name="sender">The instance of the server which sent the event.</param>
        /// <param name="message">The message which is being recevied.</param>
        /// <returns>Whether the message should be received and enqued or discarded.</returns>
        public delegate PayloadInterceptAction OnBeforeCommandReceiveEvent(NMQServer sender, NMQMessageBase message);

        /// <summary>
        /// Triggered before a message is sent to a client.
        /// </summary>
        public event OnBeforeCommandSendEvent OnBeforeMessageSend;
        /// <summary>
        /// Triggered before a message is sent to a client.
        /// </summary>
        /// <param name="sender">The instance of the server which sent the event.</param>
        /// <param name="message">The message which is being sent.</param>
        /// <returns>Whether the message should be sent or discarded.</returns>
        public delegate PayloadInterceptAction OnBeforeCommandSendEvent(NMQServer sender, NMQMessageBase message);

        /// <summary>
        /// Triggered when an outstanding acknowledgement request expires.
        /// </summary>
        public event OnCommandAcknowledgementExpiredEvent OnCommandAcknowledgementExpired;
        /// <summary>
        /// Triggered when an outstanding acknowledgement request expires.
        /// </summary>
        /// <param name="sender">The instance of the server which sent the event.</param>
        /// <param name="message">The message which was waiting for acknowledgement.</param>
        public delegate void OnCommandAcknowledgementExpiredEvent(NMQServer sender, NMQMessageBase message);

        #endregion

        #region Socket server.

        private bool SendAsync(Socket socket, byte[] data)
        {
            try
            {
                TCPSendQueueDepth++;
                if (socket.Connected)
                {
                    socket.BeginSend(data, 0, data.Length, 0, new AsyncCallback(SendCallback), socket);
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogException(ex);
            }

            return false;
        }

        private bool SendAsync(Peer peer, NMQCommand command)
        {
            var result = SendAsync(peer.Socket, Packetizer.AssembleMessagePacket(this, command));
            if (result)
            {
                if (command.CommandType != PayloadCommandType.CommandAck && command.CommandType != PayloadCommandType.ProcessedAck)
                {
                    AddCommandAckEvent(peer, command);
                }
            }
            return result;
        }

        private void SendCallback(IAsyncResult ar)
        {
            try
            {
                Socket socket = (Socket)ar.AsyncState;
                if (ar.IsCompleted && socket.Connected == true)
                {
                    int bytesSent = socket.EndSend(ar);
                }
            }
            catch (Exception ex)
            {
                LogException(ex);
            }

            TCPSendQueueDepth--;
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

            peer.Socket.BeginReceive(peer.Packet.Buffer, 0, peer.Packet.Buffer.Length, SocketFlags.None, _onDataReceivedCallback, peer);
        }

        private void OnDataReceived(IAsyncResult asyn)
        {
            Socket socket = null;

            try
            {
                Peer peer = (Peer)asyn.AsyncState;
                socket = peer.Socket;
                if (socket.Connected)
                {
                    peer.Packet.BufferLength = peer.Socket.EndReceive(asyn);

                    if (peer.Packet.BufferLength == 0)
                    {
                        CleanupConnection(peer.Socket);
                        return;
                    }

                    Packetizer.DissasemblePacketData(this, peer, peer.Packet, PacketPayloadHandler);

                    if (BroadcastMessagesImmediately)
                    {
                        BroadcastMessages();
                    }

                    WaitForData(peer);
                }
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
                            _subscriptions[queueName].RemoveAll(o => o.Socket == socket);
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

        #region Broadcast Schemes.

        private List<string> GetAllSubscribedQueueNames()
        {
            lock (this) return _subscriptions.Keys.ToList();
        }

        private List<Peer> GetSubscriptionPeers(string queueName)
        {
            lock (this) return _subscriptions[queueName];
        }

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
                            queue.Value.RemoveAll(o => o.CreatedTime < staleTime);
                        }
                        queue.Value.RemoveAll(o => now > o.ExpireTime);
                    }

                    DateTime ackStaleTime = DateTime.UtcNow.AddMilliseconds(-AckTimeout);
                    lock (_commandAckEvents)
                    {
                        var acks = _commandAckEvents.Where(o => o.Value.CreatedDate < ackStaleTime);
                        foreach (var ack in acks)
                        {
                            OnCommandAcknowledgementExpired?.Invoke(this, ack.Value.Command.Message);
                            PresumedDeadCommandCount++;
                            _commandAckEvents.Remove(ack.Key);
                        }
                    }

                    DateTime processedStaleTime = DateTime.UtcNow.AddMilliseconds(-ProcessTimeout);
                    lock (_processedEvents)
                    {
                        var acks = _processedEvents.Where(o => o.Value.CreatedDate < processedStaleTime);
                        foreach (var ack in acks)
                        {
                            OnCommandAcknowledgementExpired?.Invoke(this, ack.Value.Command.Message);
                            PresumedDeadCommandCount++;
                            _processedEvents.Remove(ack.Key);
                        }
                    }

                    BroadcastMessages_Scheme();
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

        private void BroadcastMessages_Scheme()
        {
            foreach (string queueName in GetAllSubscribedQueueNames())
            {
                if (_queues.ContainsKey(queueName) == false)
                {
                    continue; //The queue does not exist.
                }

                var queue = _queues[queueName];
                if ((queue?.Count ?? 0) == 0)
                {
                    continue; //The queue is empty.
                }

                var peers = GetSubscriptionPeers(queueName);
                if ((peers?.Count ?? 0) == 0)
                {
                    if (DoNotQueueWhenNoConsumers)
                    {
                        queue.Clear();
                    }
                    continue; //No consumers for the current queue.
                }

                //If all of the peers have an empty pending message then assign the next one in the queue.
                var topMessage = queue.Peek(); //Get the next queue item and queue it up for all subscribed peers.
                peers.Where(o => o.CurrentMessage == null).ToList().ForEach(o => o.CurrentMessage = new NMQMessageEnvelope() { Message = topMessage });

                foreach (var peer in peers)
                {
                    try
                    {
                        if (peer.CurrentMessage.Sent == false)
                        {
                            var payload = new NMQCommand()
                            {
                                Message = peer.CurrentMessage.Message,
                                CommandType = PayloadCommandType.ProcessMessage
                            };

                            if (peer.Socket.Connected)
                            {
                                var action = OnBeforeMessageSend?.Invoke(this, payload.Message);
                                if (action == PayloadInterceptAction.Discard)
                                {
                                    //Skip sending to this client.
                                    peer.CurrentMessage.Sent = true;
                                    peer.CurrentMessage.Acknowledged = true;
                                }
                                else
                                {
                                    //Create an process event so we can track whether this message was received before we remove it from the queue.
                                    AddProcessedAckEvent(peer, payload);
                                    peer.CurrentMessage.Errored = !SendAsync(peer, payload);
                                    peer.CurrentMessage.Sent = true;

                                    if (BrodcastScheme == BrodcastScheme.FireAndForget)
                                    {
                                        peer.CurrentMessage.Acknowledged = !peer.CurrentMessage.Errored;
                                    }
                                }
                            }
                            else
                            {
                                peer.CurrentMessage.Errored = true;
                            }
                        }
                    }
                    catch (SocketException)
                    {
                        peer.CurrentMessage.Errored = true;
                    }
                }

                //The message has been sent to all peers, remove it from the queue.
                if (peers.Select(o => o.CurrentMessage).Where(o => o.IsComplete == false).Any() == false)
                {
                    lock (this) queue.Dequeue();
                    peers.ForEach(o => o.CurrentMessage = null);
                }
            }
        }

        #endregion

        #region Command Packet Handler.

        private void PacketPayloadHandler(Peer peer, NMQCommand payload)
        {
            try
            {
                Monitor.Enter(_broadcastMessagesLock);

                var action = OnBeforeMessageReceive?.Invoke(this, payload.Message);

                //The custom even handler said to drop the command. Ablige...
                if (action == PayloadInterceptAction.Discard)
                {
                    return;
                }
                //The client just connected, they are lettng us know who they are.
                else if (payload.CommandType == PayloadCommandType.Hello)
                {
                    peer.PeerId = payload.Message.PeerId;

                    //Wave back.
                    SendAsync(peer, new NMQCommand()
                    {
                        Message = new NMQMessageBase(peer.PeerId, Guid.NewGuid()),
                        CommandType = PayloadCommandType.Hello
                    });
                }
                //The client is acknowledging the receipt of a command.
                else if (payload.CommandType == PayloadCommandType.CommandAck)
                {
                    var key = $"{peer.PeerId}-{payload.Message.MessageId}";
                    if (_commandAckEvents.ContainsKey(key))
                    {
                        lock (_commandAckEvents) _commandAckEvents.Remove(key);
                    }
                    else
                    {
                        //Client... what are you ack'ing?
                    }
                }
                //The client is acknowledging the receipt of a command.
                else if (payload.CommandType == PayloadCommandType.ProcessedAck)
                {
                    var key = $"{peer.PeerId}-{payload.Message.MessageId}";
                    if (_processedEvents.ContainsKey(key))
                    {
                        lock (_processedEvents) _processedEvents.Remove(key);
                    }
                    else
                    {
                        //Client... what are you ack'ing?
                    }

                    //Client is letting us know they have processed the message.
                    if (peer.CurrentMessage?.Message?.MessageId == payload.Message.MessageId)
                    {
                        peer.CurrentMessage.Acknowledged = true;
                    }
                    else
                    {
                        //The message has likely timed out and been removed from the queue.
                    }
                }
                //The client is asking that a message be enqueued.
                else if (payload.CommandType == PayloadCommandType.Enqueue)
                {
                    if (_queues.ContainsKey(payload.Message.QueueName) == false)
                    {
                        _queues.Add(payload.Message.QueueName, new ListQueue<NMQMessageBase>());
                    }

                    var queue = _queues[payload.Message.QueueName];
                    _queues[payload.Message.QueueName].Enqueue(payload.Message);
                }
                //The client is asking that the queue be cleared.
                else if (payload.CommandType == PayloadCommandType.Clear)
                {
                    if (_queues.ContainsKey(payload.Message.QueueName) == false)
                    {
                        _queues.Add(payload.Message.QueueName, new ListQueue<NMQMessageBase>());
                    }

                    var queue = _queues[payload.Message.QueueName];
                    _queues[payload.Message.QueueName].Clear();
                }
                //The client is asking to subscribe to a queue and receive all messages sent to it.
                else if (payload.CommandType == PayloadCommandType.Subscribe)
                {
                    if (_subscriptions.ContainsKey(payload.Message.QueueName) == false)
                    {
                        _subscriptions.Add(payload.Message.QueueName, new List<Peer>());
                    }

                    if (_subscriptions[payload.Message.QueueName].Contains(peer) == false)
                    {
                        _subscriptions[payload.Message.QueueName].Add(peer);
                    }
                }
                //The client is asking to unsubscribe from a queue and no longer receive any messages sent to it.
                else if (payload.CommandType == PayloadCommandType.UnSubscribe)
                {
                    if (_subscriptions.ContainsKey(payload.Message.QueueName) == false)
                    {
                        return; //The queue has no subscriptions.
                    }

                    _subscriptions[payload.Message.QueueName].RemoveAll(o => o == peer);

                    if (_subscriptions[payload.Message.QueueName].Count == 0)
                    {
                        _subscriptions.Remove(payload.Message.QueueName);
                    }
                }
                //The client has lost its mind.
                else
                {
                    throw new Exception("Command type is not implemented.");
                }

                //Acknoledge commands. Even if the client doesnt want one, it will safely ignore it.
                if (payload.CommandType != PayloadCommandType.CommandAck && payload.CommandType != PayloadCommandType.ProcessedAck)
                {
                    SendAsync(peer, new NMQCommand()
                    {
                        Message = new NMQMessageBase(payload.Message.PeerId, payload.Message.QueueName, payload.Message.MessageId),
                        CommandType = PayloadCommandType.CommandAck
                    });
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

        NMQACKEvent AddCommandAckEvent(Peer peer, NMQCommand command)
        {
            var ackEvent = new NMQACKEvent(peer, command);
            lock (_commandAckEvents) _commandAckEvents.Add(ackEvent.Key, ackEvent);
            return ackEvent;
        }

        NMQACKEvent AddProcessedAckEvent(Peer peer, NMQCommand command)
        {
            var ackEvent = new NMQACKEvent(peer, command);
            lock (_processedEvents) _processedEvents.Add(ackEvent.Key, ackEvent);
            return ackEvent;
        }

        #endregion
    }
}
