using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode.TestHelpers.Runtime;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Unity.Netcode.RuntimeTests
{
    internal enum NetworkConfigOptions
    {
        EnsureVariableLengthSafety,
        Default,
    }

    /// <summary>
    /// This class tests the NGO message codec between the C# SDK and the Rust runtime.
    /// </summary>
    /// <remarks>
    /// These tests are run against a rust echo-server.
    /// This server decodes incoming messages, and then re-encodes them before sending them back to the client.
    /// Any errors in decoding or encoding messages will not echo the messages back, causing the tests to fail
    /// No message handling logic is tested in these tests. They are only testing the codec.
    /// The tests check if they can bind to a rust echo-server at the given address and port, if all tests are ignored.
    /// The rust echo-server is run using `cargo run --example ngo_echo_server -- --port {port}`
    /// The C# port can be configured using the environment variable "ECHO_SERVER_PORT"
    /// The default behaviour when unity fails to connect to the echo-server is to ignore all tests in this class.
    /// This can be overridden by setting the environment variable "ENSURE_CODEC_TESTS" to any value - then the tests will fail.
    /// </remarks>
    [TestFixture(NetworkConfigOptions.EnsureVariableLengthSafety)]
    [TestFixture(NetworkConfigOptions.Default)]
    internal class DistributedAuthorityCodecTests : NetcodeIntegrationTest
    {
        protected override int NumberOfClients => 0;

        // Use the CMB Service for all tests
        protected override bool UseCMBService() => true;

        // Set the network topology to distributed authority for all tests
        protected override NetworkTopologyTypes OnGetNetworkTopologyType() => NetworkTopologyTypes.DistributedAuthority;

        private CodecTestHooks m_ClientCodecHook;
        private NetworkManager m_Client;

        private string m_TransportHost = Environment.GetEnvironmentVariable("NGO_HOST") ?? "127.0.0.1";
        private static readonly ushort k_TransportPort = GetPortToBind();
        private const int k_ClientId = 0;

        private bool m_EnsureVariableLengthSafety;

        public DistributedAuthorityCodecTests(NetworkConfigOptions configOptions) : base()
        {
            m_EnsureVariableLengthSafety = configOptions == NetworkConfigOptions.EnsureVariableLengthSafety;
        }

        /// <summary>
        /// Configures the port to look for the rust echo-server.
        /// </summary>
        /// <returns>The port from the environment variable "ECHO_SERVER_PORT" if it is set and valid; otherwise uses port 7777</returns>
        private static ushort GetPortToBind()
        {
            var value = Environment.GetEnvironmentVariable("ECHO_SERVER_PORT");
            return ushort.TryParse(value, out var configuredPort) ? configuredPort : (ushort)7777;
        }

        private GameObject m_SpawnObject;

        internal class TestNetworkComponent : NetworkBehaviour
        {
            public NetworkList<int> MyNetworkList = new NetworkList<int>(new List<int> { 1, 2, 3 });
            public NetworkVariable<int> MyNetworkVar = new NetworkVariable<int>(3);

            [Rpc(SendTo.Authority)]
            public void TestAuthorityRpc(byte[] _)
            {
            }
        }

        protected override void OnOneTimeSetup()
        {
            // Prevents the tests from running if no CMB Service is detected
            if (!CanConnectToServer(m_TransportHost, k_TransportPort))
            {
                var shouldFail = Environment.GetEnvironmentVariable("ENSURE_CODEC_TESTS");
                if (string.IsNullOrEmpty(shouldFail) || shouldFail.ToLower() == "false")
                {
                    Assert.Ignore($"ignoring DA codec tests because UTP transport cannot connect to the rust echo-server at {m_TransportHost}:{k_TransportPort}");
                }
                else
                {
                    Assert.Fail($"Failed to connect to the rust echo-server at {m_TransportHost}:{k_TransportPort}");
                }
            }
            base.OnOneTimeSetup();
        }

        /// <summary>
        /// Add any additional components to default player prefab
        /// </summary>
        protected override void OnCreatePlayerPrefab()
        {
            m_PlayerPrefab.AddComponent<TestNetworkComponent>();
            base.OnCreatePlayerPrefab();
        }

        /// <summary>
        /// Modify NetworkManager instances for settings specific to tests
        /// </summary>
        protected override void OnServerAndClientsCreated()
        {
            m_Client = GetAuthorityNetworkManager();

            var utpTransport = m_Client.gameObject.AddComponent<UnityTransport>();
            m_Client.NetworkConfig.NetworkTransport = utpTransport;
            m_Client.NetworkConfig.EnableSceneManagement = false;
            m_Client.NetworkConfig.AutoSpawnPlayerPrefabClientSide = true;
            m_Client.NetworkConfig.EnsureNetworkVariableLengthSafety = m_EnsureVariableLengthSafety;
            utpTransport.ConnectionData.Address = Dns.GetHostAddresses(m_TransportHost).First().ToString();
            utpTransport.ConnectionData.Port = k_TransportPort;
            m_Client.LogLevel = LogLevel.Developer;

            // Validate we are in distributed authority mode with client side spawning and using CMB Service
            Assert.True(m_Client.NetworkConfig.NetworkTopology == NetworkTopologyTypes.DistributedAuthority, "Distributed authority topology is not set!");
            Assert.True(m_Client.AutoSpawnPlayerPrefabClientSide, "Client side spawning is not set!");
            Assert.True(m_Client.CMBServiceConnection, "CMBServiceConnection is not set!");

            // Create a prefab for creating and destroying tests (auto-registers with NetworkManagers)
            m_SpawnObject = CreateNetworkObjectPrefab("TestObject");
            m_SpawnObject.AddComponent<TestNetworkComponent>();
        }

        protected override IEnumerator OnStartedServerAndClients()
        {
            // Validate the NetworkManager are in distributed authority mode
            Assert.True(m_Client.DistributedAuthorityMode, "Distributed authority is not set!");

            yield return base.OnStartedServerAndClients();

            // wait for client to connect since m_BypassConnectionTimeout
            yield return WaitForConditionOrTimeOut(() => m_Client.LocalClient.PlayerObject != null);
            AssertOnTimeout($"Timed out waiting for the client's player to be spanwed!");

            // Register hooks after starting clients and server (in this case just the one client)
            // We do this at this after all the setup has finished in order to ensure our hooks are only catching messages from the tests
            m_ClientCodecHook = new CodecTestHooks();
            m_Client.MessageManager.Hook(m_ClientCodecHook);
        }

        [UnityTest]
        public IEnumerator AuthorityRpc()
        {
            var player = m_Client.LocalClient.PlayerObject;
            player.OwnerClientId = m_Client.LocalClientId + 1;

            var networkComponent = player.GetComponent<TestNetworkComponent>();
            networkComponent.UpdateNetworkProperties();
            networkComponent.TestAuthorityRpc(new byte[] { 1, 2, 3, 4 });

            // Universal Rpcs are sent as a ProxyMessage (which contains an RpcMessage)
            yield return m_ClientCodecHook.WaitForMessageReceived<ProxyMessage>();
        }

        [UnityTest]
        public IEnumerator ChangeOwnership()
        {
            var message = new ChangeOwnershipMessage
            {
                DistributedAuthorityMode = true,
                NetworkObjectId = 100,
                OwnerClientId = 2,
            };

            yield return SendMessage(ref message);
        }

        [UnityTest]
        public IEnumerator ClientConnected()
        {
            var message = new ClientConnectedMessage()
            {
                ClientId = 2,
            };

            yield return SendMessage(ref message);
        }

        [UnityTest]
        public IEnumerator ClientDisconnected()
        {
            var message = new ClientDisconnectedMessage()
            {
                ClientId = 2,
            };

            yield return SendMessage(ref message);
        }

        [UnityTest]
        public IEnumerator CreateObject()
        {
            SpawnObject(m_SpawnObject, m_Client);
            yield return m_ClientCodecHook.WaitForMessageReceived<CreateObjectMessage>();
        }

        [UnityTest]
        public IEnumerator DestroyObject()
        {
            var spawnedObject = SpawnObject(m_SpawnObject, m_Client);
            yield return m_ClientCodecHook.WaitForMessageReceived<CreateObjectMessage>();
            spawnedObject.GetComponent<NetworkObject>().Despawn();
            yield return m_ClientCodecHook.WaitForMessageReceived<DestroyObjectMessage>();
        }

        [UnityTest]
        public IEnumerator Disconnect()
        {
            var message = new DisconnectReasonMessage
            {
                Reason = "test"
            };

            return SendMessage(ref message);
        }

        [UnityTest]
        public IEnumerator NamedMessage()
        {
            var writeBuffer = new FastBufferWriter(sizeof(int), Allocator.Temp);
            writeBuffer.WriteValueSafe(5);

            var message = new NamedMessage
            {
                Hash = 3,
                SendData = writeBuffer,
            };

            yield return SendMessage(ref message);
        }

        [UnityTest]
        public IEnumerator NetworkVariableDelta()
        {
            var component = m_Client.LocalClient.PlayerObject.GetComponent<TestNetworkComponent>();
            var message = new NetworkVariableDeltaMessage
            {
                NetworkObjectId = m_Client.LocalClient.PlayerObject.NetworkObjectId,
                NetworkBehaviourIndex = component.NetworkBehaviourId,
                DeliveryMappedNetworkVariableIndex = new HashSet<int> { 0, 1 },
                TargetClientId = 5,
                NetworkBehaviour = component,
            };

            yield return SendMessage(ref message);
        }

        [UnityTest]
        public IEnumerator NetworkVariableDelta_WithValueUpdate()
        {
            var instance = SpawnObject(m_SpawnObject, m_Client);
            yield return m_ClientCodecHook.WaitForMessageReceived<CreateObjectMessage>();
            var component = instance.GetComponent<TestNetworkComponent>();

            var newValue = 5;
            component.MyNetworkVar.Value = newValue;
            yield return m_ClientCodecHook.WaitForMessageReceived<NetworkVariableDeltaMessage>();
            Assert.AreEqual(newValue, component.MyNetworkVar.Value);
        }

        [UnityTest]
        public IEnumerator NetworkListDelta_WithValueUpdate()
        {
            var instance = SpawnObject(m_SpawnObject, m_Client);
            yield return m_ClientCodecHook.WaitForMessageReceived<CreateObjectMessage>();
            var component = instance.GetComponent<TestNetworkComponent>();

            component.MyNetworkList.Add(5);
            yield return m_ClientCodecHook.WaitForMessageReceived<NetworkVariableDeltaMessage>();
            component.MyNetworkList.Add(6);
            component.MyNetworkList.Add(7);
            yield return m_ClientCodecHook.WaitForMessageReceived<NetworkVariableDeltaMessage>();
            component.MyNetworkList.Insert(1, 8);
            yield return m_ClientCodecHook.WaitForMessageReceived<NetworkVariableDeltaMessage>();
            component.MyNetworkList.Insert(8, 11);
            yield return m_ClientCodecHook.WaitForMessageReceived<NetworkVariableDeltaMessage>();
            component.MyNetworkList.Remove(6);
            yield return m_ClientCodecHook.WaitForMessageReceived<NetworkVariableDeltaMessage>();
            component.MyNetworkList.RemoveAt(2);
            yield return m_ClientCodecHook.WaitForMessageReceived<NetworkVariableDeltaMessage>();
            component.MyNetworkList.Clear();
            yield return m_ClientCodecHook.WaitForMessageReceived<NetworkVariableDeltaMessage>();
        }

        [UnityTest]
        public IEnumerator ParentSync()
        {
            var message = new ParentSyncMessage
            {
                NetworkObjectId = 0,
                WorldPositionStays = true,
                IsLatestParentSet = false,
                Position = new Vector3(1, 2, 3),
                Rotation = new Quaternion(4, 5, 6, 7),
                Scale = new Vector3(8, 9, 10),
            };

            yield return SendMessage(ref message);
        }

        [UnityTest]
        public IEnumerator SessionOwner()
        {
            var message = new SessionOwnerMessage()
            {
                SessionOwner = 2,
            };

            yield return SendMessage(ref message);
        }

        [UnityTest]
        public IEnumerator ServerLog()
        {
            var message = new ServerLogMessage()
            {
                LogType = NetworkLog.LogType.Info,
                Message = "test",
            };

            yield return SendMessage(ref message);
        }

        [UnityTest]
        public IEnumerator UnnamedMessage()
        {
            var writeBuffer = new FastBufferWriter(sizeof(int), Allocator.Temp);
            writeBuffer.WriteValueSafe(5);

            var message = new UnnamedMessage
            {
                SendData = writeBuffer,
            };

            yield return SendMessage(ref message);
        }

        [UnityTest]
        public IEnumerator SceneEventMessageLoad()
        {
            m_Client.SceneManager.SkipSceneHandling = true;
            var eventData = new SceneEventData(m_Client)
            {
                SceneEventType = SceneEventType.Load,
                LoadSceneMode = LoadSceneMode.Single,
                SceneEventProgressId = Guid.NewGuid(),
                SceneHash = XXHash.Hash32("SomeRandomSceneName"),
                SceneHandle = 23456,
            };

            var message = new SceneEventMessage()
            {
                EventData = eventData
            };
            yield return SendMessage(ref message);
        }

        [UnityTest]
        public IEnumerator SceneEventMessageLoadWithObjects()
        {
            m_Client.SceneManager.SkipSceneHandling = true;
            var prefabNetworkObject = m_SpawnObject.GetComponent<NetworkObject>();

            m_Client.SceneManager.ScenePlacedObjects.Add(0, new Dictionary<int, NetworkObject>()
            {
                { 1, prefabNetworkObject }
            });
            var eventData = new SceneEventData(m_Client)
            {
                SceneEventType = SceneEventType.Load,
                LoadSceneMode = LoadSceneMode.Single,
                SceneEventProgressId = Guid.NewGuid(),
                SceneHash = XXHash.Hash32("SomeRandomSceneName"),
                SceneHandle = 23456,
            };

            var message = new SceneEventMessage()
            {
                EventData = eventData
            };
            yield return SendMessage(ref message);
        }

        [UnityTest]
        public IEnumerator SceneEventMessageUnload()
        {
            m_Client.SceneManager.SkipSceneHandling = true;
            var eventData = new SceneEventData(m_Client)
            {
                SceneEventType = SceneEventType.Unload,
                LoadSceneMode = LoadSceneMode.Single,
                SceneEventProgressId = Guid.NewGuid(),
                SceneHash = XXHash.Hash32("SomeRandomSceneName"),
                SceneHandle = 23456,
            };

            var message = new SceneEventMessage()
            {
                EventData = eventData
            };
            yield return SendMessage(ref message);
        }

        [UnityTest]
        public IEnumerator SceneEventMessageLoadComplete()
        {
            m_Client.SceneManager.SkipSceneHandling = true;
            var eventData = new SceneEventData(m_Client)
            {
                SceneEventType = SceneEventType.LoadComplete,
                LoadSceneMode = LoadSceneMode.Single,
                SceneEventProgressId = Guid.NewGuid(),
                SceneHash = XXHash.Hash32("SomeRandomSceneName"),
                SceneHandle = 23456,
            };

            var message = new SceneEventMessage()
            {
                EventData = eventData
            };
            yield return SendMessage(ref message);
        }

        [UnityTest]
        public IEnumerator SceneEventMessageUnloadComplete()
        {
            m_Client.SceneManager.SkipSceneHandling = true;
            var eventData = new SceneEventData(m_Client)
            {
                SceneEventType = SceneEventType.UnloadComplete,
                LoadSceneMode = LoadSceneMode.Single,
                SceneEventProgressId = Guid.NewGuid(),
                SceneHash = XXHash.Hash32("SomeRandomSceneName"),
                SceneHandle = 23456,
            };

            var message = new SceneEventMessage()
            {
                EventData = eventData
            };
            yield return SendMessage(ref message);
        }

        [UnityTest]
        public IEnumerator SceneEventMessageLoadCompleted()
        {
            m_Client.SceneManager.SkipSceneHandling = true;
            var eventData = new SceneEventData(m_Client)
            {
                SceneEventType = SceneEventType.LoadEventCompleted,
                LoadSceneMode = LoadSceneMode.Single,
                SceneEventProgressId = Guid.NewGuid(),
                SceneHash = XXHash.Hash32("SomeRandomSceneName"),
                SceneHandle = 23456,
                ClientsCompleted = new List<ulong>() { k_ClientId },
                ClientsTimedOut = new List<ulong>() { 123456789 },
            };

            var message = new SceneEventMessage()
            {
                EventData = eventData
            };
            yield return SendMessage(ref message);
        }

        [UnityTest]
        public IEnumerator SceneEventMessageUnloadLoadCompleted()
        {
            m_Client.SceneManager.SkipSceneHandling = true;
            var eventData = new SceneEventData(m_Client)
            {
                SceneEventType = SceneEventType.UnloadEventCompleted,
                LoadSceneMode = LoadSceneMode.Single,
                SceneEventProgressId = Guid.NewGuid(),
                SceneHash = XXHash.Hash32("SomeRandomSceneName"),
                SceneHandle = 23456,
                ClientsCompleted = new List<ulong>() { k_ClientId },
                ClientsTimedOut = new List<ulong>() { 123456789 },
            };

            var message = new SceneEventMessage()
            {
                EventData = eventData
            };
            yield return SendMessage(ref message);
        }

        [UnityTest]
        public IEnumerator SceneEventMessageSynchronize()
        {
            m_Client.SceneManager.SkipSceneHandling = true;
            var eventData = new SceneEventData(m_Client)
            {
                SceneEventType = SceneEventType.Synchronize,
                LoadSceneMode = LoadSceneMode.Single,
                ClientSynchronizationMode = LoadSceneMode.Single,
                SceneHash = XXHash.Hash32("SomeRandomSceneName"),
                SceneHandle = 23456,
                ScenesToSynchronize = new Queue<uint>()
            };
            eventData.ScenesToSynchronize.Enqueue(101);
            eventData.SceneHandlesToSynchronize = new Queue<uint>();
            eventData.SceneHandlesToSynchronize.Enqueue(202);


            var message = new SceneEventMessage()
            {
                EventData = eventData
            };
            yield return SendMessage(ref message);
        }

        [UnityTest]
        public IEnumerator SceneEventMessageReSynchronize()
        {
            m_Client.SceneManager.SkipSceneHandling = true;
            var eventData = new SceneEventData(m_Client)
            {
                SceneEventType = SceneEventType.ReSynchronize,
                LoadSceneMode = LoadSceneMode.Single,
                ClientSynchronizationMode = LoadSceneMode.Single,
                SceneHash = XXHash.Hash32("SomeRandomSceneName"),
                SceneHandle = 23456,
            };

            var message = new SceneEventMessage()
            {
                EventData = eventData
            };
            yield return SendMessage(ref message);
        }

        [UnityTest]
        public IEnumerator SceneEventMessageSynchronizeComplete()
        {
            m_Client.SceneManager.SkipSceneHandling = true;
            var eventData = new SceneEventData(m_Client)
            {
                SceneEventType = SceneEventType.ReSynchronize,
                LoadSceneMode = LoadSceneMode.Single,
                ClientSynchronizationMode = LoadSceneMode.Single,
                SceneHash = XXHash.Hash32("SomeRandomSceneName"),
                SceneHandle = 23456,
            };

            var message = new SceneEventMessage()
            {
                EventData = eventData
            };
            yield return SendMessage(ref message);
        }

        [UnityTest]
        public IEnumerator SceneEventMessageActiveSceneChanged()
        {
            m_Client.SceneManager.SkipSceneHandling = true;
            var eventData = new SceneEventData(m_Client)
            {
                SceneEventType = SceneEventType.ActiveSceneChanged,
                ActiveSceneHash = XXHash.Hash32("ActiveScene")
            };

            var message = new SceneEventMessage()
            {
                EventData = eventData
            };
            yield return SendMessage(ref message);
        }

        [UnityTest, Ignore("Serializing twice causes data to disappear in the SceneManager for this event")]
        public IEnumerator SceneEventMessageObjectSceneChanged()
        {
            m_Client.SceneManager.SkipSceneHandling = true;
            var prefabNetworkObject = m_SpawnObject.GetComponent<NetworkObject>();
            m_Client.SceneManager.ObjectsMigratedIntoNewScene = new Dictionary<int, Dictionary<ulong, List<NetworkObject>>>
            {
                { 0, new Dictionary<ulong, List<NetworkObject>>()}
            };

            m_Client.SceneManager.ObjectsMigratedIntoNewScene[0].Add(m_Client.LocalClientId, new List<NetworkObject>() { prefabNetworkObject });
            var eventData = new SceneEventData(m_Client)
            {
                SceneEventType = SceneEventType.ObjectSceneChanged,
            };

            var message = new SceneEventMessage()
            {
                EventData = eventData
            };
            yield return SendMessage(ref message);
        }


        private IEnumerator SendMessage<T>(ref T message) where T : INetworkMessage
        {
            m_Client.MessageManager.SetVersion(k_ClientId, XXHash.Hash32(typeof(T).FullName), message.Version);

            var clientIds = new NativeArray<ulong>(1, Allocator.Temp);
            clientIds[0] = k_ClientId;
            m_Client.MessageManager.SendMessage(ref message, NetworkDelivery.ReliableSequenced, clientIds);
            m_Client.MessageManager.ProcessSendQueues();
            return m_ClientCodecHook.WaitForMessageReceived(message);
        }

        private static bool CanConnectToServer(string host, ushort port, double timeoutMs = 100)
        {
            var address = Dns.GetHostAddresses(host).First();
            var endpoint = NetworkEndpoint.Parse(address.ToString(), port);

            var driver = NetworkDriver.Create();
            var connection = driver.Connect(endpoint);

            var start = DateTime.Now;
            var ev = Networking.Transport.NetworkEvent.Type.Empty;
            while (ev != Networking.Transport.NetworkEvent.Type.Connect)
            {
                driver.ScheduleUpdate().Complete();
                ev = driver.PopEventForConnection(connection, out _, out _);

                if (DateTime.Now - start > TimeSpan.FromMilliseconds(timeoutMs))
                {
                    return false;
                }
            }

            driver.Disconnect(connection);
            return true;
        }
    }

    internal class CodecTestHooks : INetworkHooks
    {
        private Dictionary<string, Queue<TestMessage>> m_ExpectedMessages = new Dictionary<string, Queue<TestMessage>>();
        private Dictionary<string, HashSet<string>> m_ReceivedMessages = new Dictionary<string, HashSet<string>>();

        private struct TestMessage
        {
            public string Name;
            public byte[] Data;
        }

        public void OnBeforeSendMessage<T>(ulong clientId, ref T message, NetworkDelivery delivery) where T : INetworkMessage
        {
            if (message is ConnectionRequestMessage)
            {
                return;
            }

            var writer = new FastBufferWriter(1024, Allocator.Temp);
            // Serialize the message using the known message version
            message.Serialize(writer, message.Version);

            var testName = TestContext.CurrentContext.Test.Name;
            if (!m_ExpectedMessages.ContainsKey(testName))
            {
                m_ExpectedMessages[testName] = new Queue<TestMessage>();
            }

            m_ExpectedMessages[testName].Enqueue(new TestMessage
            {
                Name = typeof(T).ToString(),
                Data = writer.ToArray(),
            });

            writer.Dispose();
        }

        public void OnAfterSendMessage<T>(ulong clientId, ref T message, NetworkDelivery delivery, int messageSizeBytes) where T : INetworkMessage
        {
        }

        public void OnBeforeReceiveMessage(ulong senderId, Type messageType, int messageSizeBytes)
        {
        }


        public void OnAfterReceiveMessage(ulong senderId, Type messageType, int messageSizeBytes)
        {
        }


        public void OnBeforeSendBatch(ulong clientId, int messageCount, int batchSizeInBytes, NetworkDelivery delivery)
        {
        }


        public void OnAfterSendBatch(ulong clientId, int messageCount, int batchSizeInBytes, NetworkDelivery delivery)
        {
        }


        public void OnBeforeReceiveBatch(ulong senderId, int messageCount, int batchSizeInBytes)
        {
        }


        public void OnAfterReceiveBatch(ulong senderId, int messageCount, int batchSizeInBytes)
        {
        }


        public bool OnVerifyCanSend(ulong destinationId, Type messageType, NetworkDelivery delivery)
        {
            return true;
        }

        public bool OnVerifyCanReceive(ulong senderId, Type messageType, FastBufferReader messageContent, ref NetworkContext context)
        {
            if (messageType == typeof(ConnectionApprovedMessage))
            {
                return true;
            }

            var testName = TestContext.CurrentContext.Test.Name;
            Assert.True(m_ExpectedMessages.ContainsKey(testName));
            Assert.IsNotEmpty(m_ExpectedMessages[testName]);

            var nextMessage = m_ExpectedMessages[testName].Dequeue();
            Assert.AreEqual(messageType.ToString(), nextMessage.Name, $"received unexpected message type: {messageType}");

            if (!m_ReceivedMessages.ContainsKey(testName))
            {
                m_ReceivedMessages[testName] = new HashSet<string>();
            }

            m_ReceivedMessages[testName].Add(messageType.ToString());

            // ServerLogMessage is an exception - it gets decoded correctly, but the bytes from the runtime do not directly match those sent by the SDK.
            if (messageType == typeof(ServerLogMessage))
            {
                return true;
            }

            var expectedBytes = nextMessage.Data;
            var receivedBytes = messageContent.ToArray();
            Assert.AreEqual(expectedBytes, receivedBytes);

            return true;
        }

        public void OnBeforeHandleMessage<T>(ref T message, ref NetworkContext context) where T : INetworkMessage
        {
        }

        public void OnAfterHandleMessage<T>(ref T message, ref NetworkContext context) where T : INetworkMessage
        {
        }

        public IEnumerator WaitForMessageReceived<T>(float timeout = 5) where T : INetworkMessage
        {
            var testName = TestContext.CurrentContext.Test.Name;
            var messageType = typeof(T).FullName;
            var startTime = Time.realtimeSinceStartup;

            while ((!m_ReceivedMessages.ContainsKey(testName) || !m_ReceivedMessages[testName].Contains(messageType)) && Time.realtimeSinceStartup - startTime < timeout)
            {
                yield return null;
            }

            Assert.True(m_ReceivedMessages.ContainsKey(testName), "failed to receive any messages");
            Assert.True(m_ReceivedMessages[testName].Contains(messageType), $"failed to receive {messageType} message, received: {string.Join(", ", m_ReceivedMessages[testName])}");

            // Reset received messages
            m_ReceivedMessages[testName] = new HashSet<string>();
        }

        public IEnumerator WaitForMessageReceived<T>(T _, float timeout = 5) where T : INetworkMessage
        {
            return WaitForMessageReceived<T>(timeout: timeout);
        }
    }
}
