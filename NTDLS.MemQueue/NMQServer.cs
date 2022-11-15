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
    /// Server class that listens for clients and routes messages between them.
    /// </summary>
    public class NMQServer : NMQBase
    {
        #region Public Properties.

        /// <summary>
        /// User data, use as you will.
        /// </summary>
        public object Tag { get; set; }

        /// <summary>
        /// The port that server eas started on.
        /// </summary>
        public object ListenPort { get; private set; }

        /// <summary>
        /// The number of commands that have been dispatched and have not been acknoledgled by a client.
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

        private Dictionary<string, NMQACKEvent> _ackEvents = new Dictionary<string, NMQACKEvent>();
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
        /// The number mesages send that the server has yet to receive an acknowledgment to.
        /// </summary>
        public int OutstandingAcknowledgments
        {
            get
            {
                lock (_ackEvents)
                    return _ackEvents.Count();
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
        public delegate void ExceptionOccuredEvent(NMQServer sender, Exception exception);

        public event OnClientConnectEvent OnClientConnect;
        public delegate ClientConnectAction OnClientConnectEvent(NMQServer sender, Socket socket);

        public event OnBeforeCommandReceiveEvent OnBeforeMessageReceive;
        public delegate PayloadReceiveAction OnBeforeCommandReceiveEvent(NMQServer sender, NMQMessageBase message);

        public event OnBeforeCommandSendEvent OnBeforeMessageSend;
        public delegate PayloadSendAction OnBeforeCommandSendEvent(NMQServer sender, NMQMessageBase message);

        public event OnCommandAcknowledgementExpiredEvent OnCommandAcknowledgementExpired;
        public delegate void OnCommandAcknowledgementExpiredEvent(NMQServer sender, NMQMessageBase message);

        #endregion

        #region Socket server.

        private void SendAsync(Socket socket, byte[] data)
        {
            try
            {
                TCPSendQueueDepth++;
                socket.BeginSend(data, 0, data.Length, 0, new AsyncCallback(SendCallback), socket);
            }
            catch (Exception ex)
            {
                LogException(ex);
            }
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

            peer.Socket.BeginReceive(peer.Packet.Buffer, 0, peer.Packet.Buffer.Length, SocketFlags.None, _onDataReceivedCallback, peer);
        }

        private void OnDataReceived(IAsyncResult asyn)
        {
            Socket socket = null;

            try
            {
                Peer peer = (Peer)asyn.AsyncState;

                socket = peer.Socket;

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

                    DateTime askStaleTime = DateTime.UtcNow.AddMilliseconds(-NMQConstants.ACK_TIMEOUT_MS);
                    lock (_ackEvents)
                    {
                        var acks = _ackEvents.Where(o => o.Value.CreatedDate < askStaleTime);
                        foreach (var ack in acks)
                        {
                            OnCommandAcknowledgementExpired?.Invoke(this, ack.Value.Command.Message);
                            PresumedDeadCommandCount++;
                            _ackEvents.Remove(ack.Key);
                        }
                    }

                    if (BrodcastScheme == BrodcastScheme.Uniform)
                    {
                        BroadcastMessages_Uniform();
                    }
                    else if (BrodcastScheme == BrodcastScheme.FireAndForget)
                    {
                        BroadcastMessages_FireAndForget();
                    }
                    else
                    {
                        throw new Exception("Unknown broadcast scheme.");
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

        /// <summary>
        /// Send the next message to each subscribed peer. Wait until all of them have acknowledged the
        /// processing of the message before moving to the next message in the queue. This is basically
        /// the opposite of round-robin because every subscriber gets a copy.
        /// </summary>
        private void BroadcastMessages_Uniform()
        {
            foreach (string queueName in GetAllSubscribedQueueNames())
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

                var peers = GetSubscriptionPeers(queueName);
                if (peers == null || peers.Count == 0)
                {
                    continue;
                }

                //If all of the peers have an empty pending message then assign the next one in the queue.
                NMQMessageBase message = queue.Peek(); //Get the next queue item and queue it up for all subscribed peers.
                peers.Where(o => o.CurrentMessage == null).ToList().ForEach(o => o.CurrentMessage = new NMQMessageEnvelope() { Message = message });

                foreach (var peer in peers)
                {
                    if (peer.CurrentMessage.Sent == false)
                    {
                        var payload = new NMQCommand()
                        {
                            Message = peer.CurrentMessage.Message,
                            CommandType = PayloadCommandType.ProcessMessage
                        };

                        byte[] messagePacket = Packetizer.AssembleMessagePacket(this, payload);

                        if (peer.Socket.Connected && messagePacket != null)
                        {
                            var action = OnBeforeMessageSend?.Invoke(this, payload.Message);

                            if (action == PayloadSendAction.Skip)
                            {
                                //Skip sending to this client.
                                peer.CurrentMessage.Sent = true;
                            }
                            else
                            {
                                //Create an ACK event so we can track whether this message was received.
                                NMQACKEvent ackEvent = new NMQACKEvent(peer, payload);
                                lock (_ackEvents) _ackEvents.Add(ackEvent.Key, ackEvent);
                                SendAsync(peer.Socket, messagePacket);

                                peer.CurrentMessage.Sent = true;
                            }
                        }
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

        /// <summary>
        /// Send each message to each subscribed peer as fast as possible. Don't even wait on the peer to acknowledged the receipt or processing of the message.
        /// This isn't necessarily unreliable, but could overwhelm a peer if it gets behind in processing - which would lead to unreliability.
        /// </summary>
        private void BroadcastMessages_FireAndForget()
        {
            var erroredSockets = new List<Socket>();

            foreach (string queueName in GetAllSubscribedQueueNames())
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

                var peers = GetSubscriptionPeers(queueName);
                if (peers == null || peers.Count == 0)
                {
                    continue;
                }

                int messagesSent = -1;

                while (queue.Count > 0 && messagesSent != 0)
                {
                    NMQMessageBase queueItem = queue.Peek();

                    messagesSent = 0;
                    foreach (Peer peer in peers)
                    {
                        try
                        {
                            var payload = new NMQCommand()
                            {
                                Message = queueItem,
                                CommandType = PayloadCommandType.ProcessMessage
                            };

                            byte[] messagePacket = Packetizer.AssembleMessagePacket(this, payload);

                            if (peer.Socket.Connected && messagePacket != null)
                            {
                                var action = OnBeforeMessageSend?.Invoke(this, payload.Message);

                                if (action == PayloadSendAction.Skip)
                                {
                                    continue; //Skip sending to this client.
                                }

                                //Create an ACK event so we can track whether this message was received.
                                NMQACKEvent ackEvent = new NMQACKEvent(peer, payload);
                                lock (_ackEvents) _ackEvents.Add(ackEvent.Key, ackEvent);

                                SendAsync(peer.Socket, messagePacket);

                                messagesSent++;
                            }
                        }
                        catch (SocketException)
                        {
                            erroredSockets.Add(peer.Socket);
                        }
                        catch (Exception ex)
                        {
                            LogException(ex);
                        }
                    }

                    if (messagesSent > 0)
                    {
                        lock (this) queue.Dequeue();
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

        #endregion

        #region Command Packet Handler.

        private void PacketPayloadHandler(Peer peer, Packet packet, NMQCommand payload)
        {
            try
            {
                Monitor.Enter(_broadcastMessagesLock);

                var action = OnBeforeMessageReceive?.Invoke(this, payload.Message);

                //The custom even henalder said to drop the command. Ablige...
                if (action == PayloadReceiveAction.Discard)
                {
                    return;
                }
                //The client just connected, they are lettng us know who they are.
                else if (payload.CommandType == PayloadCommandType.Hello)
                {
                    peer.PeerId = payload.Message.PeerId;

                    var replyPayload = new NMQCommand()
                    {
                        Message = new NMQMessageBase(peer.PeerId, Guid.NewGuid()),
                        CommandType = PayloadCommandType.Hello
                    };

                    //Wave back.
                    byte[] messagePacket = Packetizer.AssembleMessagePacket(this, replyPayload);
                    SendAsync(peer.Socket, messagePacket);
                }
                //The client is acknowledging the receipt of a command.
                else if (payload.CommandType == PayloadCommandType.CommandAck)
                {
                    var key = $"{peer.PeerId}-{payload.Message.MessageId}";
                    if (_ackEvents.ContainsKey(key))
                    {
                        lock (_ackEvents) _ackEvents.Remove(key);
                    }
                    else
                    {
                        //Client... what are you ack'ing?
                    }
                }
                //The client is acknowledging the receipt of a command.
                else if (payload.CommandType == PayloadCommandType.ProcessedAck)
                {
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
                if (payload.CommandType != PayloadCommandType.CommandAck)
                {
                    var ackPayload = new NMQCommand()
                    {
                        Message = new NMQMessageBase(payload.Message.PeerId, payload.Message.QueueName, payload.Message.MessageId),
                        CommandType = PayloadCommandType.CommandAck
                    };
                    SendAsync(peer.Socket, Packetizer.AssembleMessagePacket(this, ackPayload));
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
