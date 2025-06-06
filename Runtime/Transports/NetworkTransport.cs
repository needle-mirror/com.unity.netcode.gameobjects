using System;
using UnityEngine;

namespace Unity.Netcode
{
    /// <summary>
    /// The generic transport class all Netcode for GameObjects network transport implementations
    /// derive from.  Use this class to add a custom transport.
    /// <see cref="Transports.UTP.UnityTransport"/> for an example of how a transport is integrated
    /// </summary>
    public abstract class NetworkTransport : MonoBehaviour
    {
        /// <summary>
        /// A constant `clientId` that represents the server
        /// When this value is found in methods such as `Send`, it should be treated as a placeholder that means "the server"
        /// </summary>
        public abstract ulong ServerClientId { get; }

        /// <summary>
        /// Gets a value indicating whether this <see cref="T:NetworkTransport"/> is supported in the current runtime context
        /// This is used by multiplex adapters
        /// </summary>
        /// <value><c>true</c> if is supported; otherwise, <c>false</c>.</value>
        public virtual bool IsSupported => true;

        internal INetworkMetrics NetworkMetrics;

        /// <summary>
        /// Delegate for transport network events
        /// </summary>
        /// <param name="eventType">The type of network event that occurred</param>
        /// <param name="clientId">The ID of the client associated with this event</param>
        /// <param name="payload">The data payload received with this event</param>
        /// <param name="receiveTime">The time when this event was received</param>
        public delegate void TransportEventDelegate(NetworkEvent eventType, ulong clientId, ArraySegment<byte> payload, float receiveTime);

        /// <summary>
        /// Occurs when the transport has a new transport network event.
        /// Can be used to make an event based transport instead of a poll based.
        /// Invocation has to occur on the Unity thread in the Update loop.
        /// </summary>
        public event TransportEventDelegate OnTransportEvent;

        /// <summary>
        /// Invokes the <see cref="OnTransportEvent"/>. Invokation has to occur on the Unity thread in the Update loop.
        /// </summary>
        /// <param name="eventType">The event type</param>
        /// <param name="clientId">The clientId this event is for</param>
        /// <param name="payload">The incoming data payload</param>
        /// <param name="receiveTime">The time the event was received, as reported by Time.realtimeSinceStartup.</param>
        protected void InvokeOnTransportEvent(NetworkEvent eventType, ulong clientId, ArraySegment<byte> payload, float receiveTime)
        {
            OnTransportEvent?.Invoke(eventType, clientId, payload, receiveTime);
        }

        /// <summary>
        /// Send a payload to the specified clientId, data and networkDelivery.
        /// </summary>
        /// <param name="clientId">The clientId to send to</param>
        /// <param name="payload">The data to send</param>
        /// <param name="networkDelivery">The delivery type (QoS) to send data with</param>
        public abstract void Send(ulong clientId, ArraySegment<byte> payload, NetworkDelivery networkDelivery);

        /// <summary>
        /// Polls for incoming events, with an extra output parameter to report the precise time the event was received.
        /// </summary>
        /// <param name="clientId">The clientId this event is for</param>
        /// <param name="payload">The incoming data payload</param>
        /// <param name="receiveTime">The time the event was received, as reported by Time.realtimeSinceStartup.</param>
        /// <returns>Returns the event type</returns>
        public abstract NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime);

        /// <summary>
        /// Connects client to the server
        /// </summary>
        /// <returns>Returns success or failure</returns>
        public abstract bool StartClient();

        /// <summary>
        /// Starts to listening for incoming clients
        /// </summary>
        /// <returns>Returns success or failure</returns>
        public abstract bool StartServer();

        /// <summary>
        /// Disconnects a client from the server
        /// </summary>
        /// <param name="clientId">The clientId to disconnect</param>
        public abstract void DisconnectRemoteClient(ulong clientId);

        /// <summary>
        /// Disconnects the local client from the server
        /// </summary>
        public abstract void DisconnectLocalClient();

        /// <summary>
        /// Gets the round trip time for a specific client. This method is optional
        /// </summary>
        /// <param name="clientId">The clientId to get the RTT from</param>
        /// <returns>Returns the round trip time in milliseconds </returns>
        public abstract ulong GetCurrentRtt(ulong clientId);

        /// <summary>
        /// Shuts down the transport
        /// </summary>
        public abstract void Shutdown();

        /// <summary>
        /// Initializes the transport
        /// </summary>
        /// <param name="networkManager">optionally pass in NetworkManager</param>
        public abstract void Initialize(NetworkManager networkManager = null);

        /// <summary>
        /// Invoked by NetworkManager at the beginning of its EarlyUpdate.
        /// For order of operations see: <see cref="NetworkManager.NetworkUpdate(NetworkUpdateStage)"/>
        /// </summary>
        /// <remarks>
        /// Useful to handle processing any transport-layer events such as processing inbound messages or changes in connection state(s).
        /// </remarks>
        protected virtual void OnEarlyUpdate()
        {

        }

        /// <summary>
        /// Invoked by NetworkManager at the beginning of its EarlyUpdate
        /// </summary>
        internal void EarlyUpdate()
        {
            OnEarlyUpdate();
        }

        /// <summary>
        /// Invoked by NetworkManager towards the end of the PostLateUpdate.
        /// For order of operations see: <see cref="NetworkManager.NetworkUpdate(NetworkUpdateStage)"/>
        /// </summary>
        /// <remarks>
        /// Useful to handle any end of frame transport tasks such as sending queued transport messages.
        /// </remarks>
        protected virtual void OnPostLateUpdate()
        {

        }

        /// <summary>
        /// Invoked by NetworkManager towards the end of the PostLateUpdate
        /// </summary>
        internal void PostLateUpdate()
        {
            OnPostLateUpdate();
        }

        /// <summary>
        /// Invoked to acquire the network topology for the current network session.
        /// </summary>
        /// <returns><see cref="NetworkTopologyTypes"/></returns>
        protected virtual NetworkTopologyTypes OnCurrentTopology()
        {
            return NetworkTopologyTypes.ClientServer;
        }

        internal NetworkTopologyTypes CurrentTopology()
        {
            return OnCurrentTopology();
        }
    }

    /// <summary>
    /// The two network topology types supported by Netcode for GameObjects.
    /// </summary>
    /// <remarks>
    /// <see cref="DistributedAuthority"/> is only supported using <see cref="Transports.UTP.UnityTransport"/>.
    /// </remarks>
    public enum NetworkTopologyTypes
    {
        /// <summary>
        /// The traditional client-server network topology.
        /// </summary>
        ClientServer,
        /// <summary>
        /// The distributed authorityy network topology only supported by <see cref="Transports.UTP.UnityTransport"/>.
        /// </summary>
        DistributedAuthority
    }

#if UNITY_INCLUDE_TESTS
    public abstract class TestingNetworkTransport : NetworkTransport
    {

    }
#endif
}
