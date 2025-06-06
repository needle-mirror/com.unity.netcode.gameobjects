using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport;
using UnityEngine;

namespace Unity.Netcode.RuntimeTests
{
    internal static class UnityTransportTestHelpers
    {
        // Half a second might seem like a very long time to wait for a network event, but in CI
        // many of the machines are underpowered (e.g. old Android devices or Macs) and there are
        // sometimes very high lag spikes. PS4 and Switch are particularly sensitive in this regard
        // so we allow even more time for these platforms.
        public const float MaxNetworkEventWaitTime = 0.5f;

        // Wait for an event to appear in the given event list (must be the very next event).
        public static IEnumerator WaitForNetworkEvent(NetworkEvent type, List<TransportEvent> events, float timeout = MaxNetworkEventWaitTime)
        {
            var initialCount = events.Count;
            var startTime = Time.realtimeSinceStartup + timeout;
            var waitPeriod = new WaitForSeconds(0.01f);
            var conditionMet = false;
            while (startTime > Time.realtimeSinceStartup)
            {
                if (events.Count > initialCount)
                {
                    Assert.AreEqual(type, events[initialCount].Type);
                    conditionMet = true;
                    break;
                }

                yield return waitPeriod;
            }
            if (!conditionMet)
            {
                Assert.Fail("Timed out while waiting for network event.");
            }
        }

        internal static IEnumerator WaitForMultipleNetworkEvents(NetworkEvent type, List<TransportEvent> events, int count, float timeout = MaxNetworkEventWaitTime)
        {
            var initialCount = events.Count;
            var startTime = Time.realtimeSinceStartup + timeout;
            var waitPeriod = new WaitForSeconds(0.01f);
            var conditionMet = false;
            while (startTime > Time.realtimeSinceStartup)
            {
                // Wait until we have received at least (count) number of events
                if ((events.Count - initialCount) >= count)
                {
                    var foundTypes = 0;
                    // Look through all events received to match against the type we
                    // are looking for.
                    for (int i = initialCount; i < initialCount + count; i++)
                    {
                        if (type.Equals(events[i].Type))
                        {
                            foundTypes++;
                        }
                    }
                    // If we reached the number of events we were expecting
                    conditionMet = foundTypes == count;
                    if (conditionMet)
                    {
                        // break from the wait loop
                        break;
                    }
                }

                yield return waitPeriod;
            }
            if (!conditionMet)
            {
                Assert.Fail("Timed out while waiting for network event.");
            }
        }

        // Wait to ensure no event is sent.
        public static IEnumerator EnsureNoNetworkEvent(List<TransportEvent> events, float timeout = MaxNetworkEventWaitTime)
        {
            int initialCount = events.Count;
            float startTime = Time.realtimeSinceStartup;

            while (Time.realtimeSinceStartup - startTime < timeout)
            {
                if (events.Count > initialCount)
                {
                    Assert.Fail("Received unexpected network event.");
                }

                yield return new WaitForSeconds(0.01f);
            }
        }

        // Common code to initialize a UnityTransport that logs its events.
        public static void InitializeTransport(out UnityTransport transport, out List<TransportEvent> events, int maxPayloadSize = UnityTransport.InitialMaxPayloadSize, int maxSendQueueSize = 0, NetworkFamily family = NetworkFamily.Ipv4)
        {
            InitializeTransport(out transport, out events, string.Empty, maxPayloadSize, maxSendQueueSize, family);
        }

        /// <summary>
        /// Interanl version with identifier parameter
        /// </summary>
        internal static void InitializeTransport(out UnityTransport transport, out List<TransportEvent> events, string identifier,
            int maxPayloadSize = UnityTransport.InitialMaxPayloadSize, int maxSendQueueSize = 0, NetworkFamily family = NetworkFamily.Ipv4)
        {
            var logger = new TransportEventLogger()
            {
                Identifier = identifier,
            };
            events = logger.Events;

            transport = new GameObject().AddComponent<UnityTransportTestComponent>();

            transport.OnTransportEvent += logger.HandleEvent;
            transport.MaxPayloadSize = maxPayloadSize;
            transport.MaxSendQueueSize = maxSendQueueSize;

            if (family == NetworkFamily.Ipv6)
            {
                transport.SetConnectionData("::1", 7777);
            }

            transport.Initialize();
        }

        public static bool VerboseDebug = false;

        public static void VerboseLog(string msg)
        {
            if (VerboseDebug)
            {
                Debug.Log($"{msg}");
            }
        }

        // Information about an event generated by a transport (basically just the parameters that
        // are normally passed along to a TransportEventDelegate).
        internal struct TransportEvent
        {
            public NetworkEvent Type;
            public ulong ClientID;
            public ArraySegment<byte> Data;
            public float ReceiveTime;
        }
        // Utility class that logs events generated by a UnityTransport. Set it up by adding the
        // HandleEvent method as an OnTransportEvent delegate of the transport. The list of events
        // (in order in which they were generated) can be accessed through the Events property.
        internal class TransportEventLogger
        {
            private readonly List<TransportEvent> m_Events = new List<TransportEvent>();
            public List<TransportEvent> Events => m_Events;

            public string Identifier;
            public void HandleEvent(NetworkEvent type, ulong clientID, ArraySegment<byte> data, float receiveTime)
            {
                VerboseLog($"[{Identifier}]Tansport Event][{type}][{receiveTime}] Client-{clientID}");

                // Copy the data since the backing array will be reused for future messages.
                if (data != default(ArraySegment<byte>))
                {
                    var dataCopy = new byte[data.Count];
                    Array.Copy(data.Array, data.Offset, dataCopy, 0, data.Count);
                    data = new ArraySegment<byte>(dataCopy);
                }
                m_Events.Add(new TransportEvent
                {
                    Type = type,
                    ClientID = clientID,
                    Data = data,
                    ReceiveTime = receiveTime
                });
            }
        }

        internal class UnityTransportTestComponent : UnityTransport, INetworkUpdateSystem
        {
            private static List<UnityTransportTestComponent> s_Instances = new List<UnityTransportTestComponent>();

            public static void CleanUp()
            {
                for (int i = s_Instances.Count - 1; i >= 0; i--)
                {
                    var instance = s_Instances[i];
                    instance.Shutdown();
                    DestroyImmediate(instance.gameObject);
                }
                s_Instances.Clear();
            }

            /// <summary>
            /// Simulate the <see cref="NetworkManager.NetworkUpdate"/> being invoked so <see cref="UnityTransport.EarlyUpdate"/>
            /// and <see cref="UnityTransport.PostLateUpdate"/> are invoked.
            /// </summary>
            public void NetworkUpdate(NetworkUpdateStage updateStage)
            {
                switch (updateStage)
                {
                    case NetworkUpdateStage.EarlyUpdate:
                        {
                            EarlyUpdate();
                            break;
                        }
                    case NetworkUpdateStage.PostLateUpdate:
                        {
                            PostLateUpdate();
                            break;
                        }
                }
            }

            public override void Shutdown()
            {
                s_Instances.Remove(this);
                base.Shutdown();
                this.UnregisterAllNetworkUpdates();
            }

            public override void Initialize(NetworkManager networkManager = null)
            {
                base.Initialize(networkManager);
                this.RegisterNetworkUpdate(NetworkUpdateStage.EarlyUpdate);
                this.RegisterNetworkUpdate(NetworkUpdateStage.PreUpdate);
                this.RegisterNetworkUpdate(NetworkUpdateStage.PostLateUpdate);
                s_Instances.Add(this);
            }
        }
    }
}
