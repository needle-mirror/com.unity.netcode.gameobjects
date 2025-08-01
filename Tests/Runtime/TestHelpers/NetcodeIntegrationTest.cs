using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using NUnit.Framework;
using Unity.Netcode.RuntimeTests;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Unity.Netcode.TestHelpers.Runtime
{
    /// <summary>
    /// The default Netcode for GameObjects integration test helper class
    /// </summary>
    public abstract class NetcodeIntegrationTest
    {
        /// <summary>
        /// Used to determine if a NetcodeIntegrationTest is currently running to
        /// determine how clients will load scenes
        /// </summary>
        protected const float k_DefaultTimeoutPeriod = 8.0f;

        /// <summary>
        /// Returns the default tick rate divided into one in order to get the tick frequency.
        /// </summary>
        protected const float k_TickFrequency = 1.0f / k_DefaultTickRate;
        internal static bool IsRunning { get; private set; }

        /// <summary>
        /// A generic time out helper used with wait conditions.
        /// </summary>
        protected static TimeoutHelper s_GlobalTimeoutHelper = new TimeoutHelper(k_DefaultTimeoutPeriod);

        /// <summary>
        /// A generic default <see cref="WaitForSecondsRealtime"/> calculated from the <see cref="k_TickFrequency"/>.
        /// </summary>
        protected static WaitForSecondsRealtime s_DefaultWaitForTick = new WaitForSecondsRealtime(k_TickFrequency);

        /// <summary>
        /// Can be used to handle log assertions.
        /// </summary>
        public NetcodeLogAssert NetcodeLogAssert;

        /// <summary>
        /// Used with <see cref="ValuesAttribute"/> to describe whether scene management is enabled or disabled.
        /// </summary>
        public enum SceneManagementState
        {
            /// <summary>
            /// Scene management is enabled.
            /// </summary>
            SceneManagementEnabled,
            /// <summary>
            /// Scene management is disabled.
            /// </summary>
            SceneManagementDisabled
        }

        private StringBuilder m_InternalErrorLog = new StringBuilder();
        internal StringBuilder VerboseDebugLog = new StringBuilder();

        /// <summary>
        /// Registered list of all NetworkObjects spawned.
        /// Format is as follows:
        /// [ClientId-side where this NetworkObject instance resides][NetworkObjectId][NetworkObject]
        /// Where finding the NetworkObject with a NetworkObjectId of 10 on ClientId of 2 would be:
        /// s_GlobalNetworkObjects[2][10]
        /// To find the client or server player objects please see:
        /// <see cref="m_PlayerNetworkObjects"/>
        /// </summary>
        protected static Dictionary<ulong, Dictionary<ulong, NetworkObject>> s_GlobalNetworkObjects = new Dictionary<ulong, Dictionary<ulong, NetworkObject>>();

        /// <summary>
        /// Used by <see cref="ObjectNameIdentifier"/> to register spawned <see cref="NetworkObject"/> instances.
        /// </summary>
        /// <param name="networkObject">The <see cref="NetworkObject"/> being registered for a test in progress.</param>
        public static void RegisterNetworkObject(NetworkObject networkObject)
        {
            if (!s_GlobalNetworkObjects.ContainsKey(networkObject.NetworkManager.LocalClientId))
            {
                s_GlobalNetworkObjects.Add(networkObject.NetworkManager.LocalClientId, new Dictionary<ulong, NetworkObject>());
            }

            if (s_GlobalNetworkObjects[networkObject.NetworkManager.LocalClientId].ContainsKey(networkObject.NetworkObjectId))
            {
                if (s_GlobalNetworkObjects[networkObject.NetworkManager.LocalClientId] == null)
                {
                    Assert.False(s_GlobalNetworkObjects[networkObject.NetworkManager.LocalClientId][networkObject.NetworkObjectId] != null,
                        $"Duplicate NetworkObjectId {networkObject.NetworkObjectId} found in {nameof(s_GlobalNetworkObjects)} for client id {networkObject.NetworkManager.LocalClientId}!");
                }
                else
                {
                    s_GlobalNetworkObjects[networkObject.NetworkManager.LocalClientId][networkObject.NetworkObjectId] = networkObject;
                }
            }
            else
            {
                s_GlobalNetworkObjects[networkObject.NetworkManager.LocalClientId].Add(networkObject.NetworkObjectId, networkObject);
            }
        }

        /// <summary>
        /// Used by <see cref="ObjectNameIdentifier"/> to de-register a spawned <see cref="NetworkObject"/> instance.
        /// </summary>
        /// <param name="networkObject">The <see cref="NetworkObject"/> being de-registered for a test in progress.</param>
        public static void DeregisterNetworkObject(NetworkObject networkObject)
        {
            if (networkObject.IsSpawned && networkObject.NetworkManager != null)
            {
                DeregisterNetworkObject(networkObject.NetworkManager.LocalClientId, networkObject.NetworkObjectId);
            }
        }

        /// <summary>
        /// Overloaded version of <see cref="DeregisterNetworkObject"/>.<br />
        /// Used by <see cref="ObjectNameIdentifier"/> to de-register a spawned <see cref="NetworkObject"/> instance.
        /// </summary>
        /// <param name="localClientId">The client instance identifier of the spawned <see cref="NetworkObject"/> instance.</param>
        /// <param name="networkObjectId">The <see cref="NetworkObject.NetworkObjectId"/> of the spawned instance.</param>
        public static void DeregisterNetworkObject(ulong localClientId, ulong networkObjectId)
        {
            if (s_GlobalNetworkObjects.ContainsKey(localClientId) && s_GlobalNetworkObjects[localClientId].ContainsKey(networkObjectId))
            {
                s_GlobalNetworkObjects[localClientId].Remove(networkObjectId);
                if (s_GlobalNetworkObjects[localClientId].Count == 0)
                {
                    s_GlobalNetworkObjects.Remove(localClientId);
                }
            }
        }

        private int GetTotalClients()
        {
            if (m_DistributedAuthority)
            {
                // If not connecting to a CMB service then we are using a DAHost and we add 1 to this count.
                return !UseCMBService() && m_UseHost ? m_NumberOfClients + 1 : m_NumberOfClients;
            }
            else
            {
                // If using a host then we add one to this count.
                return m_UseHost ? m_NumberOfClients + 1 : m_NumberOfClients;
            }
        }

        /// <summary>
        /// Total number of clients that should be connected at any point during a test.
        /// </summary>
        /// <remarks>
        /// When using the CMB Service, we ignore if <see cref="m_UseHost"/> is true.
        /// </remarks>
        protected int TotalClients => GetTotalClients();

        /// <summary>
        /// Defines the default tick rate to use.
        /// </summary>
        protected const uint k_DefaultTickRate = 30;

        /// <summary>
        /// Specifies the number of client instances to be created for the integration test.
        /// </summary>
        /// <remarks>
        /// Client-Server network topology:<br />
        /// When running as a host the total number of clients will be NumberOfClients + 1.<br />
        /// See the calculation for <see cref="TotalClients"/>.
        /// Distributed Authority network topology:<br />
        /// When connecting to a CMB server, if the <see cref="NumberOfClients"/> == 0 then a session owner client will
        /// be automatically added in order to start the session and the private internal m_NumberOfClients value, which
        /// is initialized as <see cref="NumberOfClients"/>, will be incremented by 1 making <see cref="TotalClients"/> yield the
        /// same results as if we were running a Host where it will effectively be <see cref="NumberOfClients"/>
        /// + 1.
        /// </remarks>
        protected abstract int NumberOfClients { get; }
        private int m_NumberOfClients;

        /// <summary>
        /// Set this to false to create the clients first.<br />
        /// Note: If you are using scene placed NetworkObjects or doing any form of scene testing and
        /// get prefab hash id "soft synchronization" errors, then set this to false and run your test
        /// again.  This is a work-around until we can resolve some issues with NetworkManagerOwner and
        /// NetworkManager.Singleton.
        /// </summary>
        protected bool m_CreateServerFirst = true;

        /// <summary>
        /// Used to define how long <see cref="NetworkManager"/> instances persist between tests or if any should be created at all.
        /// </summary>
        public enum NetworkManagerInstatiationMode
        {
            /// <summary>
            /// This will create and destroy new NetworkManagers for each test within a child derived class
            /// </summary>
            PerTest,
            /// <summary>
            /// This will create one set of NetworkManagers used for all tests within a child derived class (destroyed once all tests are finished)
            /// </summary>
            AllTests,
            /// <summary>
            /// This will not create any NetworkManagers, it is up to the derived class to manage.
            /// </summary>
            DoNotCreate
        }

        /// <summary>
        /// Typically used with <see cref="TestFixtureAttribute"/> to define what kind of authority <see cref="NetworkManager"/> to use.
        /// </summary>
        public enum HostOrServer
        {
            /// <summary>
            /// Denotes to use a Host.
            /// </summary>
            Host,
            /// <summary>
            /// Denotes to use a Server.
            /// </summary>
            Server,
            /// <summary>
            /// Denotes that distributed authority is being used.
            /// </summary>
            DAHost
        }

        /// <summary>
        /// The default player prefab that is automatically generated and assigned to <see cref="NetworkManager"/> instances. <br />
        /// See also: The virtual method <see cref="OnCreatePlayerPrefab"/> that is invoked after the player prefab has been created.
        /// </summary>
        protected GameObject m_PlayerPrefab;

        /// <summary>
        /// The Server <see cref="NetworkManager"/> instance instantiated and tracked within the current test
        /// </summary>
        protected NetworkManager m_ServerNetworkManager;

        /// <summary>
        /// All the client <see cref="NetworkManager"/> instances instantiated and tracked within the current test
        /// </summary>
        protected NetworkManager[] m_ClientNetworkManagers;

        /// <summary>
        /// All the <see cref="NetworkManager"/> instances instantiated and tracked within the current test
        /// </summary>
        protected NetworkManager[] m_NetworkManagers;

        /// <summary>
        /// Gets the current authority of the network session.
        /// When using the hosted CMB service this will be the client who is the session owner.
        /// Otherwise, returns the server <see cref="NetworkManager"/>
        /// </summary>
        /// <returns>The <see cref="NetworkManager"/> instance that is the current authority</returns>
        protected NetworkManager GetAuthorityNetworkManager()
        {
            if (m_UseCmbService)
            {
                // If we haven't even started any NetworkManager, then return the first instance
                // since it will be the session owner.
                if (!NetcodeIntegrationTestHelpers.IsStarted)
                {
                    return m_NetworkManagers[0];
                }

                foreach (var client in m_NetworkManagers)
                {
                    if (client.LocalClient.IsSessionOwner)
                    {
                        return client;
                    }
                }
                Assert.Fail("No DA session owner found!");
            }

            return m_ServerNetworkManager;
        }

        /// <summary>
        /// Gets a non-session owner <see cref="NetworkManager"/>.
        /// </summary>
        /// <returns>A <see cref="NetworkManager"/> instance that will not be the session owner</returns>
        protected NetworkManager GetNonAuthorityNetworkManager()
        {
            return m_ClientNetworkManagers.First(client => !client.LocalClient.IsSessionOwner);
        }

        /// <summary>
        /// Contains each client relative set of player NetworkObject instances
        /// [Client Relative set of player instances][The player instance ClientId][The player instance's NetworkObject]
        /// Example:
        /// To get the player instance with a ClientId of 3 that was instantiated (relative) on the player instance with a ClientId of 2
        /// m_PlayerNetworkObjects[2][3]
        /// </summary>
        protected Dictionary<ulong, Dictionary<ulong, NetworkObject>> m_PlayerNetworkObjects = new Dictionary<ulong, Dictionary<ulong, NetworkObject>>();

        /// <summary>
        /// Determines if a host will be used (default is <see cref="true"/>).
        /// </summary>
        protected bool m_UseHost = true;

        /// <summary>
        /// Returns <see cref="true"/> if using a distributed authority network topology for the test.
        /// </summary>
        protected bool m_DistributedAuthority => m_NetworkTopologyType == NetworkTopologyTypes.DistributedAuthority;

        /// <summary>
        /// The network topology type being used by the test.
        /// </summary>
        protected NetworkTopologyTypes m_NetworkTopologyType = NetworkTopologyTypes.ClientServer;

        /// <summary>
        /// Indicates whether the currently running tests are targeting the hosted CMB Service
        /// </summary>
        /// <remarks>Can only be true if <see cref="UseCMBService"/> returns true.</remarks>
        protected bool m_UseCmbService { get; private set; }

        private string m_UseCmbServiceEnvString = null;
        private bool m_UseCmbServiceEnv;

        /// <summary>
        /// Will check the environment variable once and then always return the results
        /// of the first check.
        /// </summary>
        /// <remarks>
        /// This resets its properties during <see cref="OnOneTimeTearDown"/>, so it will
        /// check the environment variable once per test set.
        /// </remarks>
        /// <returns><see cref="true"/> or <see cref="false"/></returns>
        private bool GetServiceEnvironmentVariable()
        {
            if (!m_UseCmbServiceEnv && m_UseCmbServiceEnvString == null)
            {
                m_UseCmbServiceEnvString = NetcodeIntegrationTestHelpers.GetCMBServiceEnvironentVariable();
                if (bool.TryParse(m_UseCmbServiceEnvString.ToLower(), out bool isTrue))
                {
                    m_UseCmbServiceEnv = isTrue;
                }
                else
                {
                    Debug.LogWarning($"The USE_CMB_SERVICE ({m_UseCmbServiceEnvString}) value is an invalid bool string. {m_UseCmbService} is being set to false.");
                    m_UseCmbServiceEnv = false;
                }
            }
            return m_UseCmbServiceEnv;
        }

        /// <summary>
        /// Indicates whether a hosted CMB service is available.
        /// </summary>
        /// <remarks>Override to return false to ensure a set of tests never runs against the hosted service</remarks>
        /// <returns><see cref="true"/> if a DAHost test should run against a hosted CMB service instance; otherwise it returns <see cref="false"/>.</returns>
        protected virtual bool UseCMBService()
        {
            return m_UseCmbService;
        }

        /// <summary>
        /// Override this virtual method to control what kind of <see cref="NetworkTopologyTypes"/> to use.
        /// </summary>
        /// <returns><see cref="NetworkTopologyTypes"/></returns>
        protected virtual NetworkTopologyTypes OnGetNetworkTopologyType()
        {
            return m_NetworkTopologyType;
        }

        /// <summary>
        /// Can be used to set the distributed authority properties for a test.
        /// </summary>
        /// <param name="networkManager">The <see cref="NetworkManager"/> to configure.</param>
        protected void SetDistributedAuthorityProperties(NetworkManager networkManager)
        {
            networkManager.NetworkConfig.NetworkTopology = m_NetworkTopologyType;
            networkManager.NetworkConfig.AutoSpawnPlayerPrefabClientSide = m_DistributedAuthority;
            networkManager.NetworkConfig.UseCMBService = m_UseCmbService;
        }

        /// <summary>
        /// Defines the target frame rate to use during a test.
        /// </summary>
        protected int m_TargetFrameRate = 60;

        private NetworkManagerInstatiationMode m_NetworkManagerInstatiationMode;

        /// <summary>
        /// Determines if <see cref="VerboseDebug(string)"/> will generate a console log message.
        /// </summary>
        protected bool m_EnableVerboseDebug { get; set; }

        /// <summary>
        /// When set to true, this will bypass the entire
        /// wait for clients to connect process.
        /// </summary>
        /// <remarks>
        /// CAUTION:
        /// Setting this to true will bypass other helper
        /// identification related code, so this should only
        /// be used for connection failure oriented testing
        /// </remarks>
        protected bool m_BypassConnectionTimeout { get; set; }

        /// <summary>
        /// Enables "Time Travel" within the test, which swaps the time provider for the SDK from Unity's
        /// <see cref="Time"/> class to <see cref="MockTimeProvider"/>, and also swaps the transport implementation
        /// from <see cref="UnityTransport"/> to <see cref="MockTransport"/>.
        ///
        /// This enables five important things that help with both performance and determinism of tests that involve a
        /// lot of time and waiting:
        /// 1) It allows time to move in a completely deterministic way (testing that something happens after n seconds,
        /// the test will always move exactly n seconds with no chance of any variability in the timing),
        /// 2) It allows skipping periods of time without actually waiting that amount of time, while still simulating
        /// SDK frames as if that time were passing,
        /// 3) It dissociates the SDK's update loop from Unity's update loop, allowing us to simulate SDK frame updates
        /// without waiting for Unity to process things like physics, animation, and rendering that aren't relevant to
        /// the test,
        /// 4) It dissociates the SDK's messaging system from the networking hardware, meaning there's no delay between
        /// a message being sent and it being received, allowing us to deterministically rely on the message being
        /// received within specific time frames for the test, and
        /// 5) It allows tests to be written without the use of coroutines, which not only improves the test's runtime,
        /// but also results in easier-to-read callstacks and removes the possibility for an assertion to result in the
        /// test hanging.
        ///
        /// When time travel is enabled, the following methods become available:
        ///
        /// <see cref="TimeTravel"/>: Simulates a specific number of frames passing over a specific time period
        /// <see cref="TimeTravelToNextTick"/>: Skips forward to the next tick, siumlating at the current application frame rate
        /// <see cref="WaitForConditionOrTimeOutWithTimeTravel(Func{bool},int)"/>: Simulates frames at the application frame rate until the given condition is true
        /// <see cref="WaitForMessageReceivedWithTimeTravel{T}"/>: Simulates frames at the application frame rate until the required message is received
        /// <see cref="WaitForMessagesReceivedWithTimeTravel"/>: Simulates frames at the application frame rate until the required messages are received
        /// <see cref="StartServerAndClientsWithTimeTravel"/>: Starts a server and client and allows them to connect via simulated frames
        /// <see cref="CreateAndStartNewClientWithTimeTravel"/>: Creates a client and waits for it to connect via simulated frames
        /// <see cref="WaitForClientsConnectedOrTimeOutWithTimeTravel(Unity.Netcode.NetworkManager[])"/> Simulates frames at the application frame rate until the given clients are connected
        /// <see cref="StopOneClientWithTimeTravel"/>: Stops a client and simulates frames until it's fully disconnected.
        ///
        /// When time travel is enabled, <see cref="NetcodeIntegrationTest"/> will automatically use these in its methods
        /// when doing things like automatically connecting clients during SetUp.
        ///
        /// Additionally, the following methods replace their non-time-travel equivalents with variants that are not coroutines:
        /// <see cref="OnTimeTravelStartedServerAndClients"/> - called when server and clients are started
        /// <see cref="OnTimeTravelServerAndClientsConnected"/> - called when server and clients are connected
        ///
        /// Note that all of the non-time travel functions can still be used even when time travel is enabled - this is
        /// sometimes needed for, e.g., testing NetworkAnimator, where the unity update loop needs to run to process animations.
        /// However, it's VERY important to note here that, because the SDK will not be operating based on real-world time
        /// but based on the frozen time that's locked in from MockTimeProvider, actions that pass 10 seconds apart by
        /// real-world clock time will be perceived by the SDK as having happened simultaneously if you don't call
        /// <see cref="MockTimeProvider.TimeTravel"/> to cover the equivalent time span in the mock time provider.
        /// (Calling <see cref="MockTimeProvider.TimeTravel"/> instead of <see cref="TimeTravel"/>
        /// will move time forward without simulating any frames, which, in the case where real-world time has passed,
        /// is likely more desirable). In most cases, this desynch won't affect anything, but it is worth noting that
        /// it happens just in case a tested system depends on both the unity update loop happening *and* time moving forward.
        /// </summary>
        protected virtual bool m_EnableTimeTravel => false;

        /// <summary>
        /// If this is false, SetUp will call OnInlineSetUp instead of OnSetUp.
        /// This is a performance advantage when not using the coroutine functionality, as a coroutine that
        /// has no yield instructions in it will nonetheless still result in delaying the continuation of the
        /// method that called it for a full frame after it returns.
        /// </summary>
        protected virtual bool m_SetupIsACoroutine => true;

        /// <summary>
        /// If this is false, TearDown will call OnInlineTearDown instead of OnTearDown.
        /// This is a performance advantage when not using the coroutine functionality, as a coroutine that
        /// has no yield instructions in it will nonetheless still result in delaying the continuation of the
        /// method that called it for a full frame after it returns.
        /// </summary>
        protected virtual bool m_TearDownIsACoroutine => true;

        /// <summary>
        /// Used to display the various integration test
        /// stages and can be used to log verbose information
        /// for troubleshooting an integration test.
        /// </summary>
        /// <param name="msg">The debug message to be logged when verbose debugging is enabled</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void VerboseDebug(string msg)
        {
            if (m_EnableVerboseDebug)
            {
                VerboseDebugLog.AppendLine(msg);
                Debug.Log(msg);
            }
        }

        /// <summary>
        /// Override this and return true if you need
        /// to troubleshoot a hard to track bug within an
        /// integration test.
        /// </summary>
        /// <returns><see cref="true"/> or <see cref="false"/></returns>
        protected virtual bool OnSetVerboseDebug()
        {
            return false;
        }

        /// <summary>
        /// The very first thing invoked during the <see cref="OneTimeSetup"/> that
        /// determines how this integration test handles NetworkManager instantiation
        /// and destruction.  <see cref="NetworkManagerInstatiationMode"/>
        /// Override this method to change the default mode:
        /// <see cref="NetworkManagerInstatiationMode.AllTests"/>
        /// </summary>
        /// <returns><see cref="NetworkManagerInstatiationMode"/></returns>
        protected virtual NetworkManagerInstatiationMode OnSetIntegrationTestMode()
        {
            return NetworkManagerInstatiationMode.PerTest;
        }

        /// <summary>
        /// Override this method to do any one time setup configurations.
        /// </summary>
        protected virtual void OnOneTimeSetup()
        {
        }

        /// <summary>
        /// The <see cref="OneTimeSetUpAttribute"/> decorated method that is invoked once per derived <see cref="NetcodeIntegrationTest"/> instance.
        /// </summary>
        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            // Only For CMB Server Tests:
            // If the environment variable is set (i.e. doing a CMB server run) but UseCMBservice returns false, then ignore the test.
            // Note: This will prevent us from re-running all of the non-DA integration tests that have already run multiple times on
            // multiple platforms
            if (GetServiceEnvironmentVariable() && !UseCMBService())
            {
                Assert.Ignore("[CMB-Server Test Run] Skipping non-distributed authority test.");
                return;
            }
            else
            {
                // Otherwise, continue with the test
                InternalOnOneTimeSetup();
            }
        }

        private void InternalOnOneTimeSetup()
        {
            Application.runInBackground = true;
            m_NumberOfClients = NumberOfClients;
            IsRunning = true;
            m_EnableVerboseDebug = OnSetVerboseDebug();
            IntegrationTestSceneHandler.VerboseDebugMode = m_EnableVerboseDebug;
            VerboseDebug($"Entering {nameof(OneTimeSetup)}");

            m_NetworkManagerInstatiationMode = OnSetIntegrationTestMode();

            // Enable NetcodeIntegrationTest auto-label feature
            NetcodeIntegrationTestHelpers.RegisterNetcodeIntegrationTest(true);

#if UNITY_INCLUDE_TESTS
            // Provide an external hook to be able to make adjustments to netcode classes prior to running any tests
            NetworkManager.OnOneTimeSetup();
#endif

            OnOneTimeSetup();

            VerboseDebug($"Exiting {nameof(OneTimeSetup)}");

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            // Default to not log the serialized type not optimized warning message when testing.
            NetworkManager.DisableNotOptimizedSerializedType = true;
#endif
        }

        /// <summary>
        /// Called before creating and starting the server and clients
        /// Note: For <see cref="NetworkManagerInstatiationMode.AllTests"/> and
        /// <see cref="NetworkManagerInstatiationMode.PerTest"/> mode integration tests.
        /// For those two modes, if you want to have access to the server or client
        /// <see cref="NetworkManager"/>s then override <see cref="OnServerAndClientsCreated"/>.
        /// <see cref="m_ServerNetworkManager"/> and <see cref="m_ClientNetworkManagers"/>
        /// </summary>
        /// <returns><see cref="IEnumerator"/></returns>
        protected virtual IEnumerator OnSetup()
        {
            yield return null;
        }

        /// <summary>
        /// Called before creating and starting the server and clients
        /// Note: For <see cref="NetworkManagerInstatiationMode.AllTests"/> and
        /// <see cref="NetworkManagerInstatiationMode.PerTest"/> mode integration tests.
        /// For those two modes, if you want to have access to the server or client
        /// <see cref="NetworkManager"/>s then override <see cref="OnServerAndClientsCreated"/>.
        /// <see cref="m_ServerNetworkManager"/> and <see cref="m_ClientNetworkManagers"/>
        /// </summary>
        protected virtual void OnInlineSetup()
        {
        }

        /// <summary>
        /// The <see cref="UnitySetUpAttribute"/> decorated method that is invoked once per <see cref="TestFixtureAttribute"/> instance or just once if none.
        /// </summary>
        /// <returns><see cref="IEnumerator"/></returns>
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // In addition to setting the number of clients in the OneTimeSetup, we need to re-apply the number of clients for each unique test
            // in the event that a previous test stopped a client.
            m_NumberOfClients = NumberOfClients;
            VerboseDebugLog.Clear();
            VerboseDebug($"Entering {nameof(SetUp)}");
            NetcodeLogAssert = new NetcodeLogAssert();
            if (m_EnableTimeTravel)
            {
                if (m_NetworkManagerInstatiationMode == NetworkManagerInstatiationMode.AllTests)
                {
                    MockTransport.ClearQueues();
                }
                else
                {
                    MockTransport.Reset();
                }

                // Setup the frames per tick for time travel advance to next tick
                ConfigureFramesPerTick();
            }

            if (m_SetupIsACoroutine)
            {
                yield return OnSetup();
            }
            else
            {
                OnInlineSetup();
            }

            if (m_EnableTimeTravel)
            {
                MockTimeProvider.Reset();
                ComponentFactory.Register<IRealTimeProvider>(manager => new MockTimeProvider());
            }

            if (m_NetworkManagerInstatiationMode == NetworkManagerInstatiationMode.AllTests && m_ServerNetworkManager == null ||
                m_NetworkManagerInstatiationMode == NetworkManagerInstatiationMode.PerTest)
            {
                CreateServerAndClients();

                if (m_EnableTimeTravel)
                {
                    StartServerAndClientsWithTimeTravel();
                }
                else
                {
                    yield return StartServerAndClients();

                }
            }
            VerboseDebug($"Exiting {nameof(SetUp)}");
        }

        /// <summary>
        /// Override this to add components or adjustments to the default player prefab
        /// <see cref="m_PlayerPrefab"/>
        /// </summary>
        protected virtual void OnCreatePlayerPrefab()
        {
        }

        /// <summary>
        /// Invoked immediately after the player prefab GameObject is created
        /// prior to adding a NetworkObject component
        /// </summary>
        protected virtual void OnPlayerPrefabGameObjectCreated()
        {
        }

        private void CreatePlayerPrefab()
        {
            VerboseDebug($"Entering {nameof(CreatePlayerPrefab)}");
            // Create playerPrefab
            m_PlayerPrefab = new GameObject("Player");
            OnPlayerPrefabGameObjectCreated();
            NetworkObject networkObject = m_PlayerPrefab.AddComponent<NetworkObject>();
            networkObject.IsSceneObject = false;

            // Make it a prefab
            NetcodeIntegrationTestHelpers.MakeNetworkObjectTestPrefab(networkObject);

            OnCreatePlayerPrefab();

            VerboseDebug($"Exiting {nameof(CreatePlayerPrefab)}");
        }

        private void AddRemoveNetworkManager(NetworkManager networkManager, bool addNetworkManager)
        {
            var clientNetworkManagersList = new List<NetworkManager>(m_ClientNetworkManagers);
            if (addNetworkManager)
            {
                clientNetworkManagersList.Add(networkManager);
            }
            else
            {
                clientNetworkManagersList.Remove(networkManager);
            }

            m_ClientNetworkManagers = clientNetworkManagersList.ToArray();
            m_NumberOfClients = clientNetworkManagersList.Count;

            if (!m_UseCmbService)
            {
                clientNetworkManagersList.Insert(0, m_ServerNetworkManager);
            }
            m_NetworkManagers = clientNetworkManagersList.ToArray();
        }

        /// <summary>
        /// This is invoked before the server and client(s) are started.
        /// Override this method if you want to make any adjustments to their
        /// NetworkManager instances.
        /// </summary>
        protected virtual void OnServerAndClientsCreated()
        {
        }

        /// <summary>
        /// Will create <see cref="NumberOfClients"/> number of clients.
        /// To create a specific number of clients <see cref="CreateServerAndClients(int)"/>
        /// </summary>
        protected void CreateServerAndClients()
        {
            CreateServerAndClients(NumberOfClients);
        }

        /// <summary>
        /// Creates the server and clients
        /// </summary>
        /// <param name="numberOfClients">The number of client instances to create</param>
        protected void CreateServerAndClients(int numberOfClients)
        {
            VerboseDebug($"Entering {nameof(CreateServerAndClients)}");

            CreatePlayerPrefab();

            if (m_EnableTimeTravel)
            {
                m_TargetFrameRate = -1;
            }

            // If we are connecting to a CMB server we add +1 for the session owner
            if (m_UseCmbService)
            {
                numberOfClients++;
            }

            // Create multiple NetworkManager instances
            if (!NetcodeIntegrationTestHelpers.Create(numberOfClients, out NetworkManager server, out NetworkManager[] clients, m_TargetFrameRate, m_CreateServerFirst, m_EnableTimeTravel, m_UseCmbService))
            {
                Debug.LogError("Failed to create instances");
                Assert.Fail("Failed to create instances");
            }
            m_NumberOfClients = numberOfClients;
            m_ClientNetworkManagers = clients;
            m_ServerNetworkManager = server;

            var managers = clients.ToList();
            if (!m_UseCmbService)
            {
                managers.Insert(0, m_ServerNetworkManager);
            }
            m_NetworkManagers = managers.ToArray();

            s_DefaultWaitForTick = new WaitForSecondsRealtime(1.0f / GetAuthorityNetworkManager().NetworkConfig.TickRate);

            // Set the player prefab for the server and clients
            foreach (var manager in m_NetworkManagers)
            {
                manager.NetworkConfig.PlayerPrefab = m_PlayerPrefab;
                SetDistributedAuthorityProperties(manager);
            }

            // Provides opportunity to allow child derived classes to
            // modify the NetworkManager's configuration before starting.
            OnServerAndClientsCreated();

            VerboseDebug($"Exiting {nameof(CreateServerAndClients)}");
        }

        /// <summary>
        /// CreateAndStartNewClient Only
        /// Invoked when the newly created client has been created
        /// </summary>
        /// <param name="networkManager">The NetworkManager instance of the client.</param>
        protected virtual void OnNewClientCreated(NetworkManager networkManager)
        {
            // Ensure any late joining client has all NetworkPrefabs required to connect.
            foreach (var networkPrefab in GetAuthorityNetworkManager().NetworkConfig.Prefabs.Prefabs)
            {
                if (!networkManager.NetworkConfig.Prefabs.Contains(networkPrefab.Prefab))
                {
                    networkManager.NetworkConfig.Prefabs.Add(networkPrefab);
                }
            }
        }

        /// <summary>
        /// CreateAndStartNewClient Only
        /// Invoked when the newly created client has been created and started
        /// </summary>
        /// <param name="networkManager">The NetworkManager instance of the client.</param>
        protected virtual void OnNewClientStarted(NetworkManager networkManager)
        {
        }

        /// <summary>
        /// CreateAndStartNewClient Only
        /// Invoked when the newly created client has been created, started, and connected
        /// to the server-host.
        /// </summary>
        /// <param name="networkManager">The NetworkManager instance of the client.</param>
        protected virtual void OnNewClientStartedAndConnected(NetworkManager networkManager)
        {
        }

        /// <summary>
        /// CreateAndStartNewClient Only
        /// Override this method to bypass the waiting for a client to connect.
        /// </summary>
        /// <remarks>
        /// Use this for testing connection and disconnection scenarios
        /// </remarks>
        /// <param name="networkManager">The NetworkManager instance of the client.</param>
        /// <returns><see cref="true"/> if the test should wait for the client to connect; otherwise, it returns <see cref="false"/>.</returns>
        protected virtual bool ShouldWaitForNewClientToConnect(NetworkManager networkManager)
        {
            return true;
        }

        /// <summary>
        /// This will create, start, and connect a new client while in the middle of an
        /// integration test.
        /// </summary>
        /// <returns>An <see cref="IEnumerator"/> to be used in a coroutine for asynchronous execution.</returns>
        protected IEnumerator CreateAndStartNewClient()
        {
            var networkManager = NetcodeIntegrationTestHelpers.CreateNewClient(m_ClientNetworkManagers.Length, m_EnableTimeTravel, m_UseCmbService);
            networkManager.NetworkConfig.PlayerPrefab = m_PlayerPrefab;
            SetDistributedAuthorityProperties(networkManager);

            // Notification that the new client (NetworkManager) has been created
            // in the event any modifications need to be made before starting the client
            OnNewClientCreated(networkManager);

            yield return StartClient(networkManager);
        }

        /// <summary>
        /// Starts and connects the given networkManager as a client while in the middle of an
        /// integration test.
        /// </summary>
        /// <param name="networkManager">The network manager to start and connect</param>
        /// <returns>An <see cref="IEnumerator"/> to be used in a coroutine for asynchronous execution.</returns>
        protected IEnumerator StartClient(NetworkManager networkManager)
        {
            NetcodeIntegrationTestHelpers.StartOneClient(networkManager);

            if (LogAllMessages)
            {
                networkManager.ConnectionManager.MessageManager.Hook(new DebugNetworkHooks());
            }

            AddRemoveNetworkManager(networkManager, true);

            OnNewClientStarted(networkManager);

            if (ShouldWaitForNewClientToConnect(networkManager))
            {
                // Wait for the new client to connect
                yield return WaitForClientsConnectedOrTimeOut();
                if (s_GlobalTimeoutHelper.TimedOut)
                {
                    AddRemoveNetworkManager(networkManager, false);
                    Object.DestroyImmediate(networkManager.gameObject);
                    AssertOnTimeout($"{nameof(CreateAndStartNewClient)} timed out waiting for the new client to be connected!\n {m_InternalErrorLog}");
                    yield break;
                }
                else
                {
                    OnNewClientStartedAndConnected(networkManager);
                }

                ClientNetworkManagerPostStart(networkManager);
                if (networkManager.DistributedAuthorityMode)
                {
                    yield return WaitForConditionOrTimeOut(() => AllPlayerObjectClonesSpawned(networkManager));
                    AssertOnTimeout($"{nameof(CreateAndStartNewClient)} timed out waiting for all sessions to spawn Client-{networkManager.LocalClientId}'s player object!");
                }

                VerboseDebug($"[{networkManager.name}] Created and connected!");
            }
        }

        private bool AllPlayerObjectClonesSpawned(NetworkManager joinedClient)
        {
            m_InternalErrorLog.Clear();
            // If we are not checking for spawned players then exit early with a success
            if (!ShouldCheckForSpawnedPlayers())
            {
                return true;
            }

            // Continue to populate the PlayerObjects list until all player object (local and clone) are found
            ClientNetworkManagerPostStart(joinedClient);

            foreach (var networkManager in m_NetworkManagers)
            {
                if (networkManager.LocalClientId == joinedClient.LocalClientId)
                {
                    continue;
                }

                var playerObjectRelative = networkManager.SpawnManager.PlayerObjects.FirstOrDefault(c => c.OwnerClientId == joinedClient.LocalClientId);
                if (playerObjectRelative == null)
                {
                    m_InternalErrorLog.Append($"[AllPlayerObjectClonesSpawned][Client-{networkManager.LocalClientId}] Client-{joinedClient.LocalClientId} was not populated in the {nameof(NetworkSpawnManager.PlayerObjects)} list!");
                    return false;
                }

                // Go ahead and create an entry for this new client
                if (!m_PlayerNetworkObjects[networkManager.LocalClientId].ContainsKey(joinedClient.LocalClientId))
                {
                    m_PlayerNetworkObjects[networkManager.LocalClientId].Add(joinedClient.LocalClientId, playerObjectRelative);
                }
            }
            return true;
        }

        /// <summary>
        /// This will create, start, and connect a new client while in the middle of an
        /// integration test.
        /// </summary>
        protected void CreateAndStartNewClientWithTimeTravel()
        {
            var networkManager = NetcodeIntegrationTestHelpers.CreateNewClient(m_ClientNetworkManagers.Length, m_EnableTimeTravel);
            networkManager.NetworkConfig.PlayerPrefab = m_PlayerPrefab;
            SetDistributedAuthorityProperties(networkManager);

            // Notification that the new client (NetworkManager) has been created
            // in the event any modifications need to be made before starting the client
            OnNewClientCreated(networkManager);

            NetcodeIntegrationTestHelpers.StartOneClient(networkManager);

            if (LogAllMessages)
            {
                networkManager.ConnectionManager.MessageManager.Hook(new DebugNetworkHooks());
            }

            AddRemoveNetworkManager(networkManager, true);

            OnNewClientStarted(networkManager);

            // Wait for the new client to connect
            var connected = WaitForClientsConnectedOrTimeOutWithTimeTravel();
            AssertOnTimeout($"{nameof(CreateAndStartNewClientWithTimeTravel)} timed out waiting for all clients to be connected!\n {m_InternalErrorLog}");

            OnNewClientStartedAndConnected(networkManager);
            if (!connected)
            {
                AddRemoveNetworkManager(networkManager, false);
                Object.DestroyImmediate(networkManager.gameObject);
            }

            Assert.IsTrue(connected, $"{nameof(CreateAndStartNewClient)} timed out waiting for the new client to be connected!");
            ClientNetworkManagerPostStart(networkManager);
            VerboseDebug($"[{networkManager.name}] Created and connected!");
        }

        /// <summary>
        /// This will stop the given <see cref="NetworkManager"/> instance while in the middle of an integration test.
        /// The instance is then removed from the lists of managed instances (<see cref="m_NetworkManagers"/>, <see cref="m_ClientNetworkManagers"/>).
        /// </summary>
        /// <remarks>
        /// If there are no other references to the managed instance, it will be destroyed regardless of the destroy parameter.
        /// To avoid this, save a reference to the <see cref="NetworkManager"/> instance before calling this method.
        /// </remarks>
        /// <param name="networkManager">The <see cref="NetworkManager"/> instance of the client to stop.</param>
        /// <param name="destroy">Whether the <see cref="NetworkManager"/> instance should be destroyed after stopping. Defaults to false.</param>
        /// <returns>An <see cref="IEnumerator"/> to be used in a coroutine for asynchronous execution.</returns>
        protected IEnumerator StopOneClient(NetworkManager networkManager, bool destroy = false)
        {
            NetcodeIntegrationTestHelpers.StopOneClient(networkManager, destroy);
            AddRemoveNetworkManager(networkManager, false);
            yield return WaitForConditionOrTimeOut(() => !networkManager.IsConnectedClient);
        }

        /// <summary>
        /// This will stop the given <see cref="NetworkManager"/> instance while in the middle of a time travel integration test.
        /// The instance is then removed from the lists of managed instances (<see cref="m_NetworkManagers"/>, <see cref="m_ClientNetworkManagers"/>).
        /// </summary>
        /// <remarks>
        /// If there are no other references to the managed instance, it will be destroyed regardless of the destroy parameter.
        /// To avoid this, save a reference to the <see cref="NetworkManager"/> instance before calling this method.
        /// </remarks>
        /// <param name="networkManager">The <see cref="NetworkManager"/> instance of the client to stop.</param>
        /// <param name="destroy">Whether the <see cref="NetworkManager"/> instance should be destroyed after stopping. Defaults to false.</param>
        protected void StopOneClientWithTimeTravel(NetworkManager networkManager, bool destroy = false)
        {
            NetcodeIntegrationTestHelpers.StopOneClient(networkManager, destroy);
            AddRemoveNetworkManager(networkManager, false);
            Assert.True(WaitForConditionOrTimeOutWithTimeTravel(() => !networkManager.IsConnectedClient));
        }

        /// <summary>
        /// When using time travel, you can use this method to simulate latency conditions.
        /// </summary>
        /// <param name="latencySeconds">The amount of latency to be applied prior to invoking <see cref="TimeTravel(double, int)"/></param>
        protected void SetTimeTravelSimulatedLatency(float latencySeconds)
        {
            ((MockTransport)GetAuthorityNetworkManager().NetworkConfig.NetworkTransport).SimulatedLatencySeconds = latencySeconds;
            foreach (var client in m_ClientNetworkManagers)
            {
                ((MockTransport)client.NetworkConfig.NetworkTransport).SimulatedLatencySeconds = latencySeconds;
            }
        }

        /// <summary>
        /// When using time travel, you can use this method to simulate packet loss conditions.
        /// </summary>
        /// <param name="dropRatePercent">The percentage of packets to be dropped while time traveling.</param>
        protected void SetTimeTravelSimulatedDropRate(float dropRatePercent)
        {
            ((MockTransport)GetAuthorityNetworkManager().NetworkConfig.NetworkTransport).PacketDropRate = dropRatePercent;
            foreach (var client in m_ClientNetworkManagers)
            {
                ((MockTransport)client.NetworkConfig.NetworkTransport).PacketDropRate = dropRatePercent;
            }
        }

        /// <summary>
        /// When using time travel, you can use this method to simulate packet jitter conditions.
        /// </summary>
        /// <param name="jitterSeconds">The amount of packet jitter to be applied while time traveling.</param>
        protected void SetTimeTravelSimulatedLatencyJitter(float jitterSeconds)
        {
            ((MockTransport)GetAuthorityNetworkManager().NetworkConfig.NetworkTransport).LatencyJitter = jitterSeconds;
            foreach (var client in m_ClientNetworkManagers)
            {
                ((MockTransport)client.NetworkConfig.NetworkTransport).LatencyJitter = jitterSeconds;
            }
        }

        /// <summary>
        /// Override this method and return false in order to be able
        /// to manually control when the server and clients are started.
        /// </summary>
        /// <returns><see cref="true"/> or <see cref="false"/></returns>
        protected virtual bool CanStartServerAndClients()
        {
            return true;
        }

        /// <summary>
        /// Invoked after the server and clients have started.
        /// Note: No connection verification has been done at this point
        /// </summary>
        /// <returns><see cref="IEnumerator"/></returns>
        protected virtual IEnumerator OnStartedServerAndClients()
        {
            yield return null;
        }

        /// <summary>
        /// Invoked after the server and clients have started.
        /// Note: No connection verification has been done at this point
        /// </summary>
        protected virtual void OnTimeTravelStartedServerAndClients()
        {
        }

        /// <summary>
        /// Invoked after the server and clients have started and verified
        /// their connections with each other.
        /// </summary>
        /// <returns><see cref="IEnumerator"/></returns>
        protected virtual IEnumerator OnServerAndClientsConnected()
        {
            yield return null;
        }

        /// <summary>
        /// Invoked after the server and clients have started and verified
        /// their connections with each other.
        /// </summary>
        protected virtual void OnTimeTravelServerAndClientsConnected()
        {
        }

        private void ClientNetworkManagerPostStart(NetworkManager networkManager)
        {
            networkManager.name = $"NetworkManager - Client - {networkManager.LocalClientId}";
            Assert.NotNull(networkManager.LocalClient.PlayerObject, $"{nameof(StartServerAndClients)} detected that client {networkManager.LocalClientId} does not have an assigned player NetworkObject!");

            // Go ahead and create an entry for this new client
            if (!m_PlayerNetworkObjects.ContainsKey(networkManager.LocalClientId))
            {
                m_PlayerNetworkObjects.Add(networkManager.LocalClientId, new Dictionary<ulong, NetworkObject>());
            }

            // Get all player instances for the current client NetworkManager instance
            var clientPlayerClones = Object.FindObjectsByType<NetworkObject>(FindObjectsSortMode.None).Where((c) => c.IsPlayerObject && c.OwnerClientId == networkManager.LocalClientId).ToList();
            // Add this player instance to each client player entry
            foreach (var playerNetworkObject in clientPlayerClones)
            {
                // When the server is not the host this needs to be done
                if (!m_PlayerNetworkObjects.ContainsKey(playerNetworkObject.NetworkManager.LocalClientId))
                {
                    m_PlayerNetworkObjects.Add(playerNetworkObject.NetworkManager.LocalClientId, new Dictionary<ulong, NetworkObject>());
                }

                if (!m_PlayerNetworkObjects[playerNetworkObject.NetworkManager.LocalClientId].ContainsKey(networkManager.LocalClientId))
                {
                    m_PlayerNetworkObjects[playerNetworkObject.NetworkManager.LocalClientId].Add(networkManager.LocalClientId, playerNetworkObject);
                }
            }
            // For late joining clients, add the remaining (if any) cloned versions of each client's player
            clientPlayerClones = Object.FindObjectsByType<NetworkObject>(FindObjectsSortMode.None).Where((c) => c.IsPlayerObject && c.NetworkManager == networkManager).ToList();
            foreach (var playerNetworkObject in clientPlayerClones)
            {
                if (!m_PlayerNetworkObjects[networkManager.LocalClientId].ContainsKey(playerNetworkObject.OwnerClientId))
                {
                    m_PlayerNetworkObjects[networkManager.LocalClientId].Add(playerNetworkObject.OwnerClientId, playerNetworkObject);
                }
            }
        }

        /// <summary>
        /// Invoked after the initial start sequence.
        /// </summary>
        protected void ClientNetworkManagerPostStartInit()
        {
            // Creates a dictionary for all player instances client and server relative
            // This provides a simpler way to get a specific player instance relative to a client instance
            foreach (var networkManager in m_ClientNetworkManagers)
            {
                ClientNetworkManagerPostStart(networkManager);
            }

            if (m_UseHost)
            {
                var clientSideServerPlayerClones = Object.FindObjectsByType<NetworkObject>(FindObjectsSortMode.None).Where((c) => c.IsPlayerObject && c.OwnerClientId == NetworkManager.ServerClientId);
                foreach (var playerNetworkObject in clientSideServerPlayerClones)
                {
                    // When the server is not the host this needs to be done
                    if (!m_PlayerNetworkObjects.ContainsKey(playerNetworkObject.NetworkManager.LocalClientId))
                    {
                        m_PlayerNetworkObjects.Add(playerNetworkObject.NetworkManager.LocalClientId, new Dictionary<ulong, NetworkObject>());
                    }

                    if (!m_PlayerNetworkObjects[playerNetworkObject.NetworkManager.LocalClientId].ContainsKey(NetworkManager.ServerClientId))
                    {
                        m_PlayerNetworkObjects[playerNetworkObject.NetworkManager.LocalClientId].Add(NetworkManager.ServerClientId, playerNetworkObject);
                    }
                }
            }
        }

        /// <summary>
        /// Determines if all <see cref="NetcodeIntegrationTest"/> related messages should be logged or not.
        /// </summary>
        protected virtual bool LogAllMessages => false;

        /// <summary>
        /// A virtal method that, when overriden, provides control over whether to spawn a player or not.
        /// </summary>
        /// <returns><see cref="true"/> if players should be spawned and <see cref="false"/> if they should not.</returns>
        protected virtual bool ShouldCheckForSpawnedPlayers()
        {
            return true;
        }

        /// <summary>
        /// Starts the session owner and awaits for it to connect before starting the remaining clients.
        /// </summary>
        /// <remarks>
        /// DANGO-TODO: Renove this when the Rust server connection sequence is fixed and we don't have to pre-start
        /// the session owner.
        /// </remarks>
        /// <returns><see cref="IEnumerator"/></returns>
        private IEnumerator StartSessionOwner()
        {
            VerboseDebug("Starting session owner...");
            NetcodeIntegrationTestHelpers.StartOneClient(m_ClientNetworkManagers[0]);
            yield return WaitForConditionOrTimeOut(() => m_ClientNetworkManagers[0].IsConnectedClient);
            AssertOnTimeout($"Timed out waiting for the session owner to connect to CMB Server!");
            Assert.True(m_ClientNetworkManagers[0].LocalClient.IsSessionOwner, $"Client-{m_ClientNetworkManagers[0].LocalClientId} started session but was not set to be the session owner!");
            VerboseDebug("Session owner connected and approved.");
        }

        /// <summary>
        /// This starts the server and clients as long as <see cref="CanStartServerAndClients"/>
        /// returns true.
        /// </summary>
        /// <returns><see cref="IEnumerator"/></returns>
        protected IEnumerator StartServerAndClients()
        {
            if (CanStartServerAndClients())
            {
                VerboseDebug($"Entering {nameof(StartServerAndClients)}");

                // DANGO-TODO: Renove this when the Rust server connection sequence is fixed and we don't have to pre-start
                // the session owner.
                if (m_UseCmbService)
                {
                    VerboseDebug("Using a distributed authority CMB Server for connection.");
                    yield return StartSessionOwner();
                }

                // Start the instances and pass in our SceneManagerInitialization action that is invoked immediately after host-server
                // is started and after each client is started.
                if (!NetcodeIntegrationTestHelpers.Start(m_UseHost, !m_UseCmbService, m_ServerNetworkManager, m_ClientNetworkManagers))
                {
                    Debug.LogError("Failed to start instances");
                    Assert.Fail("Failed to start instances");
                }

                // Get the authority NetworkMananger (Server, Host, or Session Owner)
                var authorityManager = GetAuthorityNetworkManager();

                // When scene management is enabled, we need to re-apply the scenes populated list since we have overriden the ISceneManagerHandler
                // imeplementation at this point. This assures any pre-loaded scenes will be automatically assigned to the server and force clients
                // to load their own scenes.
                if (authorityManager.NetworkConfig.EnableSceneManagement)
                {
                    var scenesLoaded = authorityManager.SceneManager.ScenesLoaded;
                    authorityManager.SceneManager.SceneManagerHandler.PopulateLoadedScenes(ref scenesLoaded, authorityManager);
                }

                if (LogAllMessages)
                {
                    EnableMessageLogging();
                }

                RegisterSceneManagerHandler();

                // Notification that the server and clients have been started
                yield return OnStartedServerAndClients();

                // When true, we skip everything else (most likely a connection oriented test)
                if (!m_BypassConnectionTimeout)
                {
                    // Wait for all clients to connect
                    yield return WaitForClientsConnectedOrTimeOut();

                    AssertOnTimeout($"{nameof(StartServerAndClients)} timed out waiting for all clients to be connected!\n {m_InternalErrorLog}");

                    if (m_UseHost || authorityManager.IsHost)
                    {
                        // Add the server player instance to all m_ClientSidePlayerNetworkObjects entries
                        var serverPlayerClones = Object.FindObjectsByType<NetworkObject>(FindObjectsSortMode.None).Where((c) => c.IsPlayerObject && c.OwnerClientId == authorityManager.LocalClientId);
                        foreach (var playerNetworkObject in serverPlayerClones)
                        {
                            if (!m_PlayerNetworkObjects.ContainsKey(playerNetworkObject.NetworkManager.LocalClientId))
                            {
                                m_PlayerNetworkObjects.Add(playerNetworkObject.NetworkManager.LocalClientId, new Dictionary<ulong, NetworkObject>());
                            }

                            if (!m_UseCmbService)
                            {
                                m_PlayerNetworkObjects[playerNetworkObject.NetworkManager.LocalClientId].Add(authorityManager.LocalClientId, playerNetworkObject);
                            }
                        }
                    }

                    // With distributed authority, we check that all players have spawned on all NetworkManager instances
                    if (m_DistributedAuthority)
                    {
                        foreach (var networkManager in m_NetworkManagers)
                        {
                            yield return WaitForConditionOrTimeOut(() => AllPlayerObjectClonesSpawned(networkManager));
                            AssertOnTimeout($"{nameof(CreateAndStartNewClient)} timed out waiting for all sessions to spawn Client-{networkManager.LocalClientId}'s player object!\n {m_InternalErrorLog}");
                        }
                    }

                    // Client-Server or DAHost
                    if (ShouldCheckForSpawnedPlayers() && !m_UseCmbService)
                    {
                        // Check for players being spawned on server instance
                        ClientNetworkManagerPostStartInit();
                    }

                    // Notification that at this time the server and client(s) are instantiated,
                    // started, and connected on both sides.
                    yield return OnServerAndClientsConnected();

                    VerboseDebug($"Exiting {nameof(StartServerAndClients)}");
                }
            }
        }

        /// <summary>
        /// This starts the server and clients as long as <see cref="CanStartServerAndClients"/>
        /// returns true.
        /// </summary>
        protected void StartServerAndClientsWithTimeTravel()
        {
            if (CanStartServerAndClients())
            {
                VerboseDebug($"Entering {nameof(StartServerAndClientsWithTimeTravel)}");

                // Start the instances and pass in our SceneManagerInitialization action that is invoked immediately after host-server
                // is started and after each client is started.
                // When using the CMBService, don't start the server.
                if (!NetcodeIntegrationTestHelpers.Start(m_UseHost, !m_UseCmbService, m_ServerNetworkManager, m_ClientNetworkManagers))
                {
                    Debug.LogError("Failed to start instances");
                    Assert.Fail("Failed to start instances");
                }

                var authorityManager = GetAuthorityNetworkManager();

                // Time travel does not play nice with scene loading, clear out server side pre-loaded scenes.
                if (authorityManager.NetworkConfig.EnableSceneManagement)
                {
                    authorityManager.SceneManager.ScenesLoaded.Clear();
                }

                if (LogAllMessages)
                {
                    EnableMessageLogging();
                }

                RegisterSceneManagerHandler();

                // Notification that the server and clients have been started
                OnTimeTravelStartedServerAndClients();

                // When true, we skip everything else (most likely a connection oriented test)
                if (!m_BypassConnectionTimeout)
                {
                    // Wait for all clients to connect
                    WaitForClientsConnectedOrTimeOutWithTimeTravel();

                    AssertOnTimeout($"{nameof(StartServerAndClients)} timed out waiting for all clients to be connected!\n {m_InternalErrorLog}");

                    if (m_UseHost || authorityManager.IsHost)
                    {
#if UNITY_2023_1_OR_NEWER
                        // Add the server player instance to all m_ClientSidePlayerNetworkObjects entries
                        var serverPlayerClones = Object.FindObjectsByType<NetworkObject>(FindObjectsSortMode.None).Where((c) => c.IsPlayerObject && c.OwnerClientId == authorityManager.LocalClientId);
#else
                        // Add the server player instance to all m_ClientSidePlayerNetworkObjects entries
                        var serverPlayerClones = Object.FindObjectsOfType<NetworkObject>().Where((c) => c.IsPlayerObject && c.OwnerClientId == authorityManager.LocalClientId);
#endif
                        foreach (var playerNetworkObject in serverPlayerClones)
                        {
                            if (!m_PlayerNetworkObjects.ContainsKey(playerNetworkObject.NetworkManager.LocalClientId))
                            {
                                m_PlayerNetworkObjects.Add(playerNetworkObject.NetworkManager.LocalClientId, new Dictionary<ulong, NetworkObject>());
                            }

                            if (!m_UseCmbService)
                            {
                                m_PlayerNetworkObjects[playerNetworkObject.NetworkManager.LocalClientId].Add(authorityManager.LocalClientId, playerNetworkObject);
                            }
                        }
                    }

                    if (m_DistributedAuthority)
                    {
                        foreach (var networkManager in m_NetworkManagers)
                        {
                            WaitForConditionOrTimeOutWithTimeTravel(() => AllPlayerObjectClonesSpawned(networkManager));
                            AssertOnTimeout($"{nameof(CreateAndStartNewClient)} timed out waiting for all sessions to spawn Client-{networkManager.LocalClientId}'s player object!");
                        }
                    }

                    if (ShouldCheckForSpawnedPlayers())
                    {
                        ClientNetworkManagerPostStartInit();
                    }

                    // Notification that at this time the server and client(s) are instantiated,
                    // started, and connected on both sides.
                    OnTimeTravelServerAndClientsConnected();

                    VerboseDebug($"Exiting {nameof(StartServerAndClients)}");
                }
            }
        }

        /// <summary>
        /// Override this method to control when clients
        /// can fake-load a scene.
        /// </summary>
        /// <returns><see cref="true"/> or <see cref="false"/></returns>
        protected virtual bool CanClientsLoad()
        {
            return true;
        }

        /// <summary>
        /// Override this method to control when clients
        /// can fake-unload a scene.
        /// </summary>
        /// <returns><see cref="true"/> or <see cref="false"/></returns>
        protected virtual bool CanClientsUnload()
        {
            return true;
        }

        /// <summary>
        /// De-Registers from the CanClientsLoad and CanClientsUnload events of the
        /// ClientSceneHandler (default is IntegrationTestSceneHandler).
        /// </summary>
        protected void DeRegisterSceneManagerHandler()
        {
            IntegrationTestSceneHandler.CanClientsLoad -= ClientSceneHandler_CanClientsLoad;
            IntegrationTestSceneHandler.CanClientsUnload -= ClientSceneHandler_CanClientsUnload;
            IntegrationTestSceneHandler.NetworkManagers.Clear();
        }

        /// <summary>
        /// Registers the CanClientsLoad and CanClientsUnload events of the
        /// ClientSceneHandler.
        /// The default is: <see cref="IntegrationTestSceneHandler"/>.
        /// </summary>
        protected void RegisterSceneManagerHandler()
        {
            IntegrationTestSceneHandler.CanClientsLoad += ClientSceneHandler_CanClientsLoad;
            IntegrationTestSceneHandler.CanClientsUnload += ClientSceneHandler_CanClientsUnload;
        }

        private bool ClientSceneHandler_CanClientsUnload()
        {
            return CanClientsUnload();
        }

        private bool ClientSceneHandler_CanClientsLoad()
        {
            return CanClientsLoad();
        }

        /// <summary>
        /// Detemines if a scene can be cleaned and unloaded during the tear down phase (<see cref="UnloadRemainingScenes"/>).
        /// </summary>
        /// <param name="scene">The <see cref="Scene"/>.</param>
        /// <returns><see cref="true"/> or <see cref="false"/></returns>
        protected bool OnCanSceneCleanUpUnload(Scene scene)
        {
            return true;
        }

        /// <summary>
        /// This shuts down all NetworkManager instances registered via the
        /// <see cref="NetcodeIntegrationTestHelpers"/> class and cleans up
        /// the test runner scene of any left over NetworkObjects.
        /// <see cref="DestroySceneNetworkObjects"/>
        /// </summary>
        protected void ShutdownAndCleanUp()
        {
            VerboseDebug($"Entering {nameof(ShutdownAndCleanUp)}");
            // Shutdown and clean up both of our NetworkManager instances
            try
            {
                DeRegisterSceneManagerHandler();

                NetcodeIntegrationTestHelpers.Destroy();

                m_PlayerNetworkObjects.Clear();
                s_GlobalNetworkObjects.Clear();
            }
            catch (Exception e)
            {
                throw e;
            }
            finally
            {
                if (m_PlayerPrefab != null)
                {
                    Object.DestroyImmediate(m_PlayerPrefab);
                    m_PlayerPrefab = null;
                }
            }

            // Cleanup any remaining NetworkObjects
            DestroySceneNetworkObjects();

            UnloadRemainingScenes();

            // reset the m_ServerWaitForTick for the next test to initialize
            s_DefaultWaitForTick = new WaitForSecondsRealtime(1.0f / k_DefaultTickRate);
            VerboseDebug($"Exiting {nameof(ShutdownAndCleanUp)}");

            // Assure any remaining NetworkManagers are destroyed
            DestroyNetworkManagers();
        }

        /// <summary>
        /// Internally used <see cref="Coroutine"/> that handles cleaning up during tear down.
        /// </summary>
        /// <returns><see cref="IEnumerator"/></returns>
        protected IEnumerator CoroutineShutdownAndCleanUp()
        {
            VerboseDebug($"Entering {nameof(ShutdownAndCleanUp)}");
            // Shutdown and clean up both of our NetworkManager instances
            try
            {
                DeRegisterSceneManagerHandler();

                NetcodeIntegrationTestHelpers.Destroy();

                m_PlayerNetworkObjects.Clear();
                s_GlobalNetworkObjects.Clear();
            }
            catch (Exception e)
            {
                throw e;
            }
            finally
            {
                if (m_PlayerPrefab != null)
                {
                    Object.DestroyImmediate(m_PlayerPrefab);
                    m_PlayerPrefab = null;
                }
            }

            // Allow time for NetworkManagers to fully shutdown
            yield return k_DefaultTickRate;

            // Cleanup any remaining NetworkObjects
            DestroySceneNetworkObjects();

            UnloadRemainingScenes();

            // reset the m_ServerWaitForTick for the next test to initialize
            s_DefaultWaitForTick = new WaitForSecondsRealtime(1.0f / k_DefaultTickRate);
            VerboseDebug($"Exiting {nameof(ShutdownAndCleanUp)}");

            // Assure any remaining NetworkManagers are destroyed
            DestroyNetworkManagers();
        }

        /// <summary>
        /// Note: For <see cref="NetworkManagerInstatiationMode.PerTest"/> mode
        /// this is called before ShutdownAndCleanUp.
        /// </summary>
        /// <returns><see cref="IEnumerator"/></returns>
        protected virtual IEnumerator OnTearDown()
        {
            yield return null;
        }

        /// <summary>
        /// The inline version of tear down that is used if <see cref="m_TearDownIsACoroutine"/> is <see cref="false"/>.
        /// </summary>
        protected virtual void OnInlineTearDown()
        {
        }

        /// <summary>
        /// The <see cref="UnityTearDownAttribute"/> decorated method that is invoked during an integration test's tear down.
        /// </summary>
        /// <returns><see cref="IEnumerator"/></returns>
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            IntegrationTestSceneHandler.SceneNameToSceneHandles.Clear();
            VerboseDebug($"Entering {nameof(TearDown)}");
            if (m_TearDownIsACoroutine)
            {
                yield return OnTearDown();
            }
            else
            {
                OnInlineTearDown();
            }

            if (m_NetworkManagerInstatiationMode == NetworkManagerInstatiationMode.PerTest)
            {
                if (m_TearDownIsACoroutine)
                {
                    yield return CoroutineShutdownAndCleanUp();
                }
                else
                {
                    ShutdownAndCleanUp();
                }
            }

            if (m_EnableTimeTravel)
            {
                ComponentFactory.Deregister<IRealTimeProvider>();
            }

            VerboseDebug($"Exiting {nameof(TearDown)}");
            LogWaitForMessages();
            NetcodeLogAssert.Dispose();

        }

        /// <summary>
        /// Destroys any remaining NetworkManager instances
        /// </summary>
        private void DestroyNetworkManagers()
        {
            var networkManagers = Object.FindObjectsByType<NetworkManager>(FindObjectsSortMode.None);
            foreach (var networkManager in networkManagers)
            {
                Object.DestroyImmediate(networkManager.gameObject);
            }
            m_NetworkManagers = null;
            m_ClientNetworkManagers = null;
            m_ServerNetworkManager = null;
        }

        /// <summary>
        /// Override this method to do handle cleaning up once the test(s)
        /// within the child derived class have completed
        /// Note: For <see cref="NetworkManagerInstatiationMode.AllTests"/> mode
        /// this is called before ShutdownAndCleanUp.
        /// </summary>
        protected virtual void OnOneTimeTearDown()
        {
        }

        /// <summary>
        /// The <see cref="OneTimeTearDownAttribute"/> decorated method that is invoked once upon all tests finishing.
        /// </summary>
        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            IntegrationTestSceneHandler.VerboseDebugMode = false;
            VerboseDebug($"Entering {nameof(OneTimeTearDown)}");
            OnOneTimeTearDown();

            if (m_NetworkManagerInstatiationMode == NetworkManagerInstatiationMode.AllTests)
            {
                ShutdownAndCleanUp();
            }

            // Disable NetcodeIntegrationTest auto-label feature
            NetcodeIntegrationTestHelpers.RegisterNetcodeIntegrationTest(false);

            UnloadRemainingScenes();

            VerboseDebug($"Exiting {nameof(OneTimeTearDown)}");
#if UNITY_INCLUDE_TESTS
            // Provide an external hook to be able to make adjustments to netcode classes after running tests
            NetworkManager.OnOneTimeTearDown();
#endif

            IsRunning = false;
            m_UseCmbServiceEnvString = null;
            m_UseCmbServiceEnv = false;
        }

        /// <summary>
        /// Override this to filter out the <see cref="NetworkObject"/>s that you
        /// want to allow to persist between integration tests.
        /// <see cref="DestroySceneNetworkObjects"/>
        /// <see cref="ShutdownAndCleanUp"/>
        /// </summary>
        /// <param name="networkObject">the network object in question to be destroyed</param>
        /// <returns><see cref="true"/> or <see cref="false"/></returns>
        protected virtual bool CanDestroyNetworkObject(NetworkObject networkObject)
        {
            return true;
        }

        /// <summary>
        /// Destroys all NetworkObjects at the end of a test cycle.
        /// </summary>
        protected void DestroySceneNetworkObjects()
        {
#if UNITY_2023_1_OR_NEWER
            var networkObjects = Object.FindObjectsByType<NetworkObject>(FindObjectsSortMode.InstanceID);
#else
            var networkObjects = Object.FindObjectsOfType<NetworkObject>();
#endif
            foreach (var networkObject in networkObjects)
            {
                // This can sometimes be null depending upon order of operations
                // when dealing with parented NetworkObjects.  If NetworkObjectB
                // is a child of NetworkObjectA and NetworkObjectA comes before
                // NetworkObjectB in the list of NeworkObjects found, then when
                // NetworkObjectA's GameObject is destroyed it will also destroy
                // NetworkObjectB's GameObject which will destroy NetworkObjectB.
                // If there is a null entry in the list, this is the most likely
                // scenario and so we just skip over it.
                if (networkObject == null)
                {
                    continue;
                }

                if (CanDestroyNetworkObject(networkObject))
                {
                    networkObject.NetworkManagerOwner = m_ServerNetworkManager;
                    // Destroy the GameObject that holds the NetworkObject component
                    Object.DestroyImmediate(networkObject.gameObject);
                }
            }
        }

        /// <summary>
        /// For debugging purposes, this will turn on verbose logging of all messages and batches sent and received
        /// </summary>
        protected void EnableMessageLogging()
        {
            foreach (var networkManager in m_NetworkManagers)
            {
                networkManager.ConnectionManager.MessageManager.Hook(new DebugNetworkHooks());
            }
        }

        /// <summary>
        /// Waits for the function condition to return true or it will time out.
        /// This will operate at the current m_ServerNetworkManager.NetworkConfig.TickRate
        /// and allow for a unique TimeoutHelper handler (if none then it uses the default)
        /// Notes: This provides more stability when running integration tests that could be
        /// impacted by:
        ///     -how the integration test is being executed (i.e. in editor or in a stand alone build)
        ///     -potential platform performance issues (i.e. VM is throttled or maxed)
        /// Note: For more complex tests, <see cref="ConditionalPredicateBase"/> and the overloaded
        /// version of this method
        /// </summary>
        /// <param name="checkForCondition">the conditional function to determine if the condition has been reached.</param>
        /// <param name="timeOutHelper">the <see cref="TimeoutHelper"/> used to handle timing out the wait condition.</param>
        /// <returns><see cref="IEnumerator"/></returns>
        public static IEnumerator WaitForConditionOrTimeOut(Func<bool> checkForCondition, TimeoutHelper timeOutHelper = null)
        {
            if (checkForCondition == null)
            {
                throw new ArgumentNullException($"checkForCondition cannot be null!");
            }

            // If none is provided we use the default global time out helper
            if (timeOutHelper == null)
            {
                timeOutHelper = s_GlobalTimeoutHelper;
            }

            // Start checking for a timeout
            timeOutHelper.Start();
            while (!timeOutHelper.HasTimedOut())
            {
                // Update and check to see if the condition has been met
                if (checkForCondition.Invoke())
                {
                    break;
                }

                // Otherwise wait for 1 tick interval
                yield return s_DefaultWaitForTick;
            }

            // Stop checking for a timeout
            timeOutHelper.Stop();
        }


        /// <summary>
        /// Waits for the function condition to return true or it will time out. Uses time travel to simulate this
        /// for the given number of frames, simulating delta times at the application frame rate.
        /// </summary>
        /// <param name="checkForCondition">the conditional function to determine if the condition has been reached.</param>
        /// <param name="maxTries">the maximum times to check for the condition (default is 60).</param>
        /// <returns><see cref="true"/> or <see cref="false"/></returns>
        public bool WaitForConditionOrTimeOutWithTimeTravel(Func<bool> checkForCondition, int maxTries = 60)
        {
            if (checkForCondition == null)
            {
                throw new ArgumentNullException($"checkForCondition cannot be null!");
            }

            if (!m_EnableTimeTravel)
            {
                throw new ArgumentException($"Time travel must be enabled to use {nameof(WaitForConditionOrTimeOutWithTimeTravel)}!");
            }

            var frameRate = Application.targetFrameRate;
            if (frameRate <= 0)
            {
                frameRate = 60;
            }

            var updateInterval = 1f / frameRate;
            for (var i = 0; i < maxTries; ++i)
            {
                // Simulate a frame passing on all network managers
                TimeTravel(updateInterval, 1);
                // Update and check to see if the condition has been met
                if (checkForCondition.Invoke())
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// This version accepts an IConditionalPredicate implementation to provide
        /// more flexibility for checking complex conditional cases.
        /// </summary>
        /// <param name="conditionalPredicate">An <see cref="IConditionalPredicate"/> implementation used to determine if the condition(s) has/have been met.</param>
        /// <param name="timeOutHelper">the <see cref="TimeoutHelper"/> used to handle timing out the wait condition.</param>
        /// <returns><see cref="IEnumerator"/></returns>
        public static IEnumerator WaitForConditionOrTimeOut(IConditionalPredicate conditionalPredicate, TimeoutHelper timeOutHelper = null)
        {
            if (conditionalPredicate == null)
            {
                throw new ArgumentNullException($"checkForCondition cannot be null!");
            }

            // If none is provided we use the default global time out helper
            if (timeOutHelper == null)
            {
                timeOutHelper = s_GlobalTimeoutHelper;
            }

            conditionalPredicate.Started();
            yield return WaitForConditionOrTimeOut(conditionalPredicate.HasConditionBeenReached, timeOutHelper);
            conditionalPredicate.Finished(timeOutHelper.TimedOut);
        }

        /// <summary>
        /// This version accepts an IConditionalPredicate implementation to provide
        /// more flexibility for checking complex conditional cases. Uses time travel to simulate this
        /// for the given number of frames, simulating delta times at the application frame rate.
        /// </summary>
        /// <param name="conditionalPredicate">An <see cref="IConditionalPredicate"/> implementation used to determine if the condition(s) has/have been met.</param>
        /// <param name="maxTries">the maximum times to check for the condition (default is 60).</param>
        /// <returns><see cref="true"/> or <see cref="false"/></returns>
        public bool WaitForConditionOrTimeOutWithTimeTravel(IConditionalPredicate conditionalPredicate, int maxTries = 60)
        {
            if (conditionalPredicate == null)
            {
                throw new ArgumentNullException($"checkForCondition cannot be null!");
            }

            if (!m_EnableTimeTravel)
            {
                throw new ArgumentException($"Time travel must be enabled to use {nameof(WaitForConditionOrTimeOutWithTimeTravel)}!");
            }

            conditionalPredicate.Started();
            var success = WaitForConditionOrTimeOutWithTimeTravel(conditionalPredicate.HasConditionBeenReached, maxTries);
            conditionalPredicate.Finished(!success);
            return success;
        }

        /// <summary>
        /// Waits until the specified condition returns true or a timeout occurs, then asserts if the timeout was reached.
        /// This overload allows the condition to provide additional error details via a <see cref="StringBuilder"/>.
        /// </summary>
        /// <param name="checkForCondition">A delegate that takes a <see cref="StringBuilder"/> for error details and returns true when the desired condition is met.</param>
        /// <param name="timeOutHelper">An optional <see cref="TimeoutHelper"/> to control the timeout period. If null, the default timeout is used.</param>
        /// <returns>An <see cref="IEnumerator"/> for use in Unity coroutines.</returns>
        protected IEnumerator WaitForConditionOrTimeOut(Func<StringBuilder, bool> checkForCondition, TimeoutHelper timeOutHelper = null)
        {
            yield return WaitForConditionOrTimeOut(() =>
            {
                // Clear errorBuilder before each check to ensure the errorBuilder only contains information from the lastest run
                m_InternalErrorLog.Clear();
                return checkForCondition(m_InternalErrorLog);
            }, timeOutHelper);
        }

        /// <summary>
        /// Validates that all remote clients (i.e. non-server) detect they are connected
        /// to the server and that the server reflects the appropriate number of clients
        /// have connected or it will time out.
        /// </summary>
        /// <param name="clientsToCheck">An array of clients to be checked</param>
        /// <returns><see cref="IEnumerator"/></returns>
        protected IEnumerator WaitForClientsConnectedOrTimeOut(NetworkManager[] clientsToCheck)
        {
            yield return WaitForConditionOrTimeOut(() => CheckClientsConnected(clientsToCheck));
        }

        /// <summary>
        /// Validation for clients connected that includes additional information for easier troubleshooting purposes.
        /// </summary>
        /// <returns><see cref="true"/> or <see cref="false"/></returns>
        private bool CheckClientsConnected(NetworkManager[] clientsToCheck)
        {
            m_InternalErrorLog.Clear();
            var allClientsConnected = true;

            for (int i = 0; i < clientsToCheck.Length; i++)
            {
                if (!clientsToCheck[i].IsConnectedClient)
                {
                    allClientsConnected = false;
                    m_InternalErrorLog.AppendLine($"[Client-{i + 1}] Client is not connected!");
                }
            }

            var manager = GetAuthorityNetworkManager();
            var currentCount = manager.ConnectedClients.Count;

            if (currentCount != TotalClients)
            {
                allClientsConnected = false;
                m_InternalErrorLog.AppendLine($"[Server-Side] Expected {TotalClients} clients to connect but only {currentCount} connected!");
            }

            return allClientsConnected;
        }

        /// <summary>
        /// Validates that all remote clients (i.e. non-server) detect they are connected
        /// to the server and that the server reflects the appropriate number of clients
        /// have connected or it will time out. Uses time travel to simulate this
        /// for the given number of frames, simulating delta times at the application frame rate.
        /// </summary>
        /// <param name="clientsToCheck">An array of clients to be checked</param>
        /// <returns><see cref="true"/> or <see cref="false"/></returns>
        protected bool WaitForClientsConnectedOrTimeOutWithTimeTravel(NetworkManager[] clientsToCheck)
        {
            return WaitForConditionOrTimeOutWithTimeTravel(() => CheckClientsConnected(clientsToCheck));
        }

        /// <summary>
        /// Overloaded method that just passes in all clients to
        /// <see cref="WaitForClientsConnectedOrTimeOut(NetworkManager[])"/>
        /// </summary>
        /// <returns><see cref="IEnumerator"/></returns>
        protected IEnumerator WaitForClientsConnectedOrTimeOut()
        {
            yield return WaitForClientsConnectedOrTimeOut(m_ClientNetworkManagers);
        }

        /// <summary>
        /// Overloaded method that just passes in all clients to
        /// <see cref="WaitForClientsConnectedOrTimeOut(NetworkManager[])"/> Uses time travel to simulate this
        /// for the given number of frames, simulating delta times at the application frame rate.
        /// </summary>
        /// <returns><see cref="true"/> or <see cref="false"/></returns>
        protected bool WaitForClientsConnectedOrTimeOutWithTimeTravel()
        {
            return WaitForClientsConnectedOrTimeOutWithTimeTravel(m_ClientNetworkManagers);
        }

        internal IEnumerator WaitForMessageReceived<T>(List<NetworkManager> wiatForReceivedBy, ReceiptType type = ReceiptType.Handled) where T : INetworkMessage
        {
            // Build our message hook entries tables so we can determine if all clients received spawn or ownership messages
            var messageHookEntriesForSpawn = new List<MessageHookEntry>();
            foreach (var clientNetworkManager in wiatForReceivedBy)
            {
                var messageHook = new MessageHookEntry(clientNetworkManager, type);
                messageHook.AssignMessageType<T>();
                messageHookEntriesForSpawn.Add(messageHook);
            }

            // Used to determine if all clients received the CreateObjectMessage
            var hooks = new MessageHooksConditional(messageHookEntriesForSpawn);
            yield return WaitForConditionOrTimeOut(hooks);
            AssertOnTimeout($"Timed out waiting for message type {typeof(T).Name}!");
        }

        internal IEnumerator WaitForMessagesReceived(List<Type> messagesInOrder, List<NetworkManager> waitForReceivedBy, ReceiptType type = ReceiptType.Handled)
        {
            // Build our message hook entries tables so we can determine if all clients received spawn or ownership messages
            var messageHookEntriesForSpawn = new List<MessageHookEntry>();
            foreach (var clientNetworkManager in waitForReceivedBy)
            {
                foreach (var message in messagesInOrder)
                {
                    var messageHook = new MessageHookEntry(clientNetworkManager, type);
                    messageHook.AssignMessageType(message);
                    messageHookEntriesForSpawn.Add(messageHook);
                }
            }

            // Used to determine if all clients received the CreateObjectMessage
            var hooks = new MessageHooksConditional(messageHookEntriesForSpawn);
            yield return WaitForConditionOrTimeOut(hooks);
            var stringBuilder = new StringBuilder();
            foreach (var messageType in messagesInOrder)
            {
                stringBuilder.Append($"{messageType.Name},");
            }
            AssertOnTimeout($"Timed out waiting for message types: {stringBuilder}!");
        }


        internal void WaitForMessageReceivedWithTimeTravel<T>(List<NetworkManager> waitForReceivedBy, ReceiptType type = ReceiptType.Handled) where T : INetworkMessage
        {
            // Build our message hook entries tables so we can determine if all clients received spawn or ownership messages
            var messageHookEntriesForSpawn = new List<MessageHookEntry>();
            foreach (var clientNetworkManager in waitForReceivedBy)
            {
                var messageHook = new MessageHookEntry(clientNetworkManager, type);
                messageHook.AssignMessageType<T>();
                messageHookEntriesForSpawn.Add(messageHook);
            }

            // Used to determine if all clients received the CreateObjectMessage
            var hooks = new MessageHooksConditional(messageHookEntriesForSpawn);
            Assert.True(WaitForConditionOrTimeOutWithTimeTravel(hooks), $"[Message Not Recieved] {hooks.GetHooksStillWaiting()}");
        }

        internal void WaitForMessagesReceivedWithTimeTravel(List<Type> messagesInOrder, List<NetworkManager> waitForReceivedBy, ReceiptType type = ReceiptType.Handled)
        {
            // Build our message hook entries tables so we can determine if all clients received spawn or ownership messages
            var messageHookEntriesForSpawn = new List<MessageHookEntry>();
            foreach (var clientNetworkManager in waitForReceivedBy)
            {
                foreach (var message in messagesInOrder)
                {
                    var messageHook = new MessageHookEntry(clientNetworkManager, type);
                    messageHook.AssignMessageType(message);
                    messageHookEntriesForSpawn.Add(messageHook);
                }
            }

            // Used to determine if all clients received the CreateObjectMessage
            var hooks = new MessageHooksConditional(messageHookEntriesForSpawn);
            Assert.True(WaitForConditionOrTimeOutWithTimeTravel(hooks), $"[Messages Not Recieved] {hooks.GetHooksStillWaiting()}");
        }

        /// <summary>
        /// Creates a basic NetworkObject test prefab, assigns it to a new
        /// NetworkPrefab entry, and then adds it to the server and client(s)
        /// NetworkManagers' NetworkConfig.NetworkPrefab lists.
        /// </summary>
        /// <param name="baseName">the basic name to be used for each instance</param>
        /// <returns>The <see cref="GameObject"/> assigned to the new NetworkPrefab entry</returns>
        protected GameObject CreateNetworkObjectPrefab(string baseName)
        {
            var prefabCreateAssertError = $"You can only invoke this method during {nameof(OnServerAndClientsCreated)} " +
                                          $"but before {nameof(OnStartedServerAndClients)}!";
            var authorityNetworkManager = GetAuthorityNetworkManager();
            Assert.IsNotNull(authorityNetworkManager, prefabCreateAssertError);
            Assert.IsFalse(authorityNetworkManager.IsListening, prefabCreateAssertError);
            var prefabObject = NetcodeIntegrationTestHelpers.CreateNetworkObjectPrefab(baseName, authorityNetworkManager, m_ClientNetworkManagers);
            // DANGO-TODO: Ownership flags could require us to change this
            // For testing purposes, we default to true for the distribute ownership property when in a distirbuted authority network topology.
            prefabObject.GetComponent<NetworkObject>().Ownership |= NetworkObject.OwnershipStatus.Distributable;
            return prefabObject;
        }

        /// <summary>
        /// Overloaded method <see cref="SpawnObject(NetworkObject, NetworkManager, bool)"/>
        /// </summary>
        /// <param name="prefabGameObject">the prefab <see cref="GameObject"/> to spawn</param>
        /// <param name="owner">the owner of the instance</param>
        /// <param name="destroyWithScene">default is false</param>
        /// <returns>The <see cref="GameObject"/> of the newly spawned <see cref="NetworkObject"/>.</returns>
        protected GameObject SpawnObject(GameObject prefabGameObject, NetworkManager owner, bool destroyWithScene = false)
        {
            var prefabNetworkObject = prefabGameObject.GetComponent<NetworkObject>();
            Assert.IsNotNull(prefabNetworkObject, $"{nameof(GameObject)} {prefabGameObject.name} does not have a {nameof(NetworkObject)} component!");
            return SpawnObject(prefabNetworkObject, owner, destroyWithScene);
        }

        /// <summary>
        /// Overloaded method <see cref="SpawnObject(NetworkObject, NetworkManager, bool)"/>
        /// </summary>
        /// <param name="prefabGameObject">the prefab <see cref="GameObject"/> to spawn</param>
        /// <param name="owner">the owner of the instance</param>
        /// <param name="destroyWithScene">default is false</param>
        /// <returns>The <see cref="GameObject"/> of the newly spawned player's <see cref="NetworkObject"/>.</returns>
        protected GameObject SpawnPlayerObject(GameObject prefabGameObject, NetworkManager owner, bool destroyWithScene = false)
        {
            var prefabNetworkObject = prefabGameObject.GetComponent<NetworkObject>();
            Assert.IsNotNull(prefabNetworkObject, $"{nameof(GameObject)} {prefabGameObject.name} does not have a {nameof(NetworkObject)} component!");
            return SpawnObject(prefabNetworkObject, owner, destroyWithScene, true);
        }

        /// <summary>
        /// Spawn a NetworkObject prefab instance
        /// </summary>
        /// <param name="prefabNetworkObject">the prefab <see cref="NetworkObject"/> to spawn</param>
        /// <param name="owner">the owner of the instance</param>
        /// <param name="destroyWithScene">default is false</param>
        /// <param name="isPlayerObject">when <see cref="true"/>, the object will be spawned as the <see cref="NetworkManager.LocalClientId"/> owned player.</param>
        /// <returns>GameObject instance spawned</returns>
        private GameObject SpawnObject(NetworkObject prefabNetworkObject, NetworkManager owner, bool destroyWithScene = false, bool isPlayerObject = false)
        {
            Assert.IsTrue(prefabNetworkObject.GlobalObjectIdHash > 0, $"{nameof(GameObject)} {prefabNetworkObject.name} has a {nameof(NetworkObject.GlobalObjectIdHash)} value of 0! Make sure to make it a valid prefab before trying to spawn!");
            var newInstance = Object.Instantiate(prefabNetworkObject.gameObject);
            var networkObjectToSpawn = newInstance.GetComponent<NetworkObject>();

            if (owner.NetworkConfig.NetworkTopology == NetworkTopologyTypes.DistributedAuthority)
            {
                networkObjectToSpawn.NetworkManagerOwner = owner; // Required to assure the client does the spawning
                if (isPlayerObject)
                {
                    networkObjectToSpawn.SpawnAsPlayerObject(owner.LocalClientId, destroyWithScene);
                }
                else
                {
                    networkObjectToSpawn.SpawnWithOwnership(owner.LocalClientId, destroyWithScene);
                }
            }
            else
            {
                networkObjectToSpawn.NetworkManagerOwner = m_ServerNetworkManager; // Required to assure the server does the spawning
                if (owner == m_ServerNetworkManager)
                {
                    if (m_UseHost)
                    {
                        if (isPlayerObject)
                        {
                            networkObjectToSpawn.SpawnAsPlayerObject(owner.LocalClientId, destroyWithScene);
                        }
                        else
                        {
                            networkObjectToSpawn.SpawnWithOwnership(owner.LocalClientId, destroyWithScene);
                        }
                    }
                    else
                    {
                        networkObjectToSpawn.Spawn(destroyWithScene);
                    }
                }
                else
                {
                    if (isPlayerObject)
                    {
                        networkObjectToSpawn.SpawnAsPlayerObject(owner.LocalClientId, destroyWithScene);
                    }
                    else
                    {
                        networkObjectToSpawn.SpawnWithOwnership(owner.LocalClientId, destroyWithScene);
                    }
                }
            }
            return newInstance;
        }

        /// <summary>
        /// Overloaded method <see cref="SpawnObjects(NetworkObject, NetworkManager, int, bool)"/>.
        /// </summary>
        /// <param name="prefabGameObject">the prefab <see cref="GameObject"/> to spawn</param>
        /// <param name="owner">the owner of the instance</param>
        /// <param name="count">number of instances to create and spawn</param>
        /// <param name="destroyWithScene">default is false</param>
        /// <returns>A <see cref="List{T}"/> of <see cref="GameObject"/>s spawned.</returns>
        protected List<GameObject> SpawnObjects(GameObject prefabGameObject, NetworkManager owner, int count, bool destroyWithScene = false)
        {
            var prefabNetworkObject = prefabGameObject.GetComponent<NetworkObject>();
            Assert.IsNotNull(prefabNetworkObject, $"{nameof(GameObject)} {prefabGameObject.name} does not have a {nameof(NetworkObject)} component!");
            return SpawnObjects(prefabNetworkObject, owner, count, destroyWithScene);
        }

        /// <summary>
        /// Will spawn (x) number of prefab NetworkObjects
        /// <see cref="SpawnObject(NetworkObject, NetworkManager, bool)"/>
        /// </summary>
        /// <param name="prefabNetworkObject">the prefab <see cref="NetworkObject"/> to spawn</param>
        /// <param name="owner">the owner of the instance</param>
        /// <param name="count">number of instances to create and spawn</param>
        /// <param name="destroyWithScene">default is false</param>
        /// <returns>A <see cref="List{T}"/> of <see cref="GameObject"/>s spawned.</returns>
        private List<GameObject> SpawnObjects(NetworkObject prefabNetworkObject, NetworkManager owner, int count, bool destroyWithScene = false)
        {
            var gameObjectsSpawned = new List<GameObject>();
            for (int i = 0; i < count; i++)
            {
                gameObjectsSpawned.Add(SpawnObject(prefabNetworkObject, owner, destroyWithScene));
            }

            return gameObjectsSpawned;
        }

        /// <summary>
        /// Default constructor
        /// </summary>
        public NetcodeIntegrationTest()
        {
            var topologyType = OnGetNetworkTopologyType();
            InitializeTestConfiguration(topologyType, null);
        }

        /// <summary>
        /// Overloaded constructor taking <see cref="NetworkTopologyTypes"/> as a parameter.
        /// </summary>
        /// <param name="networkTopologyType"><see cref="NetworkTopologyTypes"/></param>
        public NetcodeIntegrationTest(NetworkTopologyTypes networkTopologyType)
        {
            InitializeTestConfiguration(networkTopologyType, null);
        }

        /// <summary>
        /// Overloaded constructor taking <see cref="NetworkTopologyTypes"/> and <see cref="HostOrServer"/> as parameters.
        /// </summary>
        /// <param name="networkTopologyType"><see cref="NetworkTopologyTypes"/></param>
        /// <param name="hostOrServer"><see cref="HostOrServer"/></param>
        public NetcodeIntegrationTest(NetworkTopologyTypes networkTopologyType, HostOrServer hostOrServer)
        {
            InitializeTestConfiguration(networkTopologyType, hostOrServer);
        }

        /// <summary>
        /// Optional Host or Server integration tests
        /// Constructor that allows you To break tests up as a host
        /// and a server.
        /// Example: Decorate your child derived class with TestFixture
        /// and then create a constructor at the child level.
        /// Don't forget to set your constructor public, else Unity will
        /// give you a hard to decipher error
        /// [TestFixture(HostOrServer.Host)]
        /// [TestFixture(HostOrServer.Server)]
        /// public class MyChildClass : NetcodeIntegrationTest
        /// {
        ///     public MyChildClass(HostOrServer hostOrServer) : base(hostOrServer) { }
        /// }
        /// </summary>
        /// <param name="hostOrServer">Specifies whether to run the test as a Host or Server configuration</param>
        public NetcodeIntegrationTest(HostOrServer hostOrServer)
        {
            m_NetworkTopologyType = hostOrServer == HostOrServer.DAHost ? NetworkTopologyTypes.DistributedAuthority : NetworkTopologyTypes.ClientServer;
            InitializeTestConfiguration(m_NetworkTopologyType, hostOrServer);
        }

        private void InitializeTestConfiguration(NetworkTopologyTypes networkTopologyType, HostOrServer? hostOrServer)
        {
            NetworkMessageManager.EnableMessageOrderConsoleLog = false;

            // Set m_NetworkTopologyType first because m_DistributedAuthority is calculated from it.
            m_NetworkTopologyType = networkTopologyType;

            if (!hostOrServer.HasValue)
            {
                // Always default to hosting, set the type of host based on the topology type.
                // Note: For m_DistributedAuthority to be true, the m_NetworkTopologyType must be set to NetworkTopologyTypes.DistributedAuthority
                hostOrServer = m_DistributedAuthority ? HostOrServer.DAHost : HostOrServer.Host;
            }
            m_UseHost = hostOrServer == HostOrServer.Host || hostOrServer == HostOrServer.DAHost;

            // If we are using a distributed authority network topology and the environment variable
            // to use the CMBService is set, then perform the m_UseCmbService check.
            if (m_DistributedAuthority && GetServiceEnvironmentVariable())
            {
                m_UseCmbService = hostOrServer == HostOrServer.DAHost;
                // In the event UseCMBService is overridden, we apply the value returned.
                // If it is, then whatever UseCMBService returns is the setting for m_UseCmbService.
                // If it is not, then it will return whatever m_UseCmbService's setting is from the above check.
                m_UseCmbService = UseCMBService();
            }
        }

        /// <summary>
        /// Just a helper function to avoid having to write the entire assert just to check if you
        /// timed out.
        /// </summary>
        /// <remarks>
        /// If no <see cref="TimeoutHelper"/> is provided, then the <see cref="s_GlobalTimeoutHelper"/> will be used.
        /// </remarks>
        /// <param name="timeOutErrorMessage">The error message to log if a time out has occurred.</param>
        /// <param name="assignedTimeoutHelper">Optional <see cref="TimeoutHelper"/> instance used during a conditional wait.</param>
        protected void AssertOnTimeout(string timeOutErrorMessage, TimeoutHelper assignedTimeoutHelper = null)
        {
            var timeoutHelper = assignedTimeoutHelper ?? s_GlobalTimeoutHelper;

            if (m_InternalErrorLog.Length > 0)
            {
                Assert.False(timeoutHelper.TimedOut, $"{timeOutErrorMessage}\n{m_InternalErrorLog}");
                m_InternalErrorLog.Clear();
                return;
            }

            Assert.False(timeoutHelper.TimedOut, $"{timeOutErrorMessage}");
        }


        private void UnloadRemainingScenes()
        {
            // Unload any remaining scenes loaded but the test runner scene
            // Note: Some tests only unload the server-side instance, and this
            // just assures no currently loaded scenes will impact the next test
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded || scene.name.Contains(NetcodeIntegrationTestHelpers.FirstPartOfTestRunnerSceneName) || !OnCanSceneCleanUpUnload(scene))
                {
                    continue;
                }

                VerboseDebug($"Unloading scene {scene.name}-{scene.handle}");
                var asyncOperation = SceneManager.UnloadSceneAsync(scene);
            }
        }

        private StringBuilder m_WaitForLog = new StringBuilder();

        private void LogWaitForMessages()
        {
            // If there is nothing to log, then don't log anything
            if (m_WaitForLog.Length > 0)
            {
                VerboseDebug(m_WaitForLog.ToString());
                m_WaitForLog.Clear();
            }
        }

        private IEnumerator WaitForTickAndFrames(NetworkManager networkManager, int tickCount, float targetFrames)
        {
            var tickAndFramesConditionMet = false;
            var frameCount = 0;
            var waitForFixedUpdate = new WaitForFixedUpdate();
            m_WaitForLog.Append($"[NetworkManager-{networkManager.LocalClientId}][WaitForTicks-Begin] Waiting for ({tickCount}) network ticks and ({targetFrames}) frames to pass.\n");
            var tickStart = networkManager.NetworkTickSystem.LocalTime.Tick;
            while (!tickAndFramesConditionMet)
            {
                // Wait until both tick and frame counts have reached their targeted values
                if ((networkManager.NetworkTickSystem.LocalTime.Tick - tickStart) >= tickCount && frameCount >= targetFrames)
                {
                    tickAndFramesConditionMet = true;
                }
                else
                {
                    yield return waitForFixedUpdate;
                    frameCount++;
                    // In the event something is broken with time systems (or the like)
                    // Exit if we have exceeded 1000 frames
                    if (frameCount >= 1000.0f)
                    {
                        tickAndFramesConditionMet = true;
                    }
                }
            }

            m_WaitForLog.Append($"[NetworkManager-{networkManager.LocalClientId}][WaitForTicks-End] Waited for ({networkManager.NetworkTickSystem.LocalTime.Tick - tickStart}) network ticks and ({frameCount}) frames to pass.\n");
            yield break;
        }

        /// <summary>
        /// Yields until specified amount of network ticks and the expected number of frames has been passed.
        /// </summary>
        /// <param name="networkManager">The relative <see cref="NetworkManager"/> waiting for a specific tick.</param>
        /// <param name="count">How many ticks to wait for.</param>
        /// <returns><see cref="IEnumerator"/></returns>
        protected IEnumerator WaitForTicks(NetworkManager networkManager, int count)
        {
            var targetTick = networkManager.NetworkTickSystem.LocalTime.Tick + count;

            // Calculate the expected number of frame updates that should occur during the tick count wait period
            var frameFrequency = 1.0f / (Application.targetFrameRate >= 60 && Application.targetFrameRate <= 100 ? Application.targetFrameRate : 60.0f);
            var tickFrequency = 1.0f / networkManager.NetworkConfig.TickRate;
            var framesPerTick = tickFrequency / frameFrequency;

            // Total number of frames to occur over the specified number of ticks
            var totalFrameCount = framesPerTick * count;
            m_WaitForLog.Append($"[NetworkManager-{networkManager.LocalClientId}][WaitForTicks] TickRate ({networkManager.NetworkConfig.TickRate}) | Tick Wait ({count}) | TargetFrameRate ({Application.targetFrameRate}) | Target Frames ({framesPerTick * count})\n");
            yield return WaitForTickAndFrames(networkManager, count, totalFrameCount);
        }

        /// <summary>
        /// Simulate a number of frames passing over a specific amount of time.
        /// The delta time simulated for each frame will be evenly divided as time/numFrames
        /// This will only simulate the netcode update loop, as well as update events on
        /// NetworkBehaviour instances, and will not simulate any Unity update processes (physics, etc)
        /// </summary>
        /// <param name="amountOfTimeInSeconds">The total amount of time to simulate, in seconds</param>
        /// <param name="numFramesToSimulate">The number of frames to distribute the time across</param>
        protected static void TimeTravel(double amountOfTimeInSeconds, int numFramesToSimulate)
        {
            var interval = amountOfTimeInSeconds / numFramesToSimulate;
            for (var i = 0; i < numFramesToSimulate; ++i)
            {
                MockTimeProvider.TimeTravel(interval);
                SimulateOneFrame();
            }
        }

        /// <summary>
        /// A virtual method that can be overriden to adjust the tick rate used when starting <see cref="NetworkManager"/> instances.
        /// </summary>
        /// <returns>The tick rate to use.</returns>
        protected virtual uint GetTickRate()
        {
            return k_DefaultTickRate;
        }

        /// <summary>
        /// A virtual method that can be overriden to adjust the frame rate used when starting <see cref="NetworkManager"/> instances.
        /// </summary>
        /// <returns>The frame rate to use.</returns>
        protected virtual int GetFrameRate()
        {
            return Application.targetFrameRate == 0 ? 60 : Application.targetFrameRate;
        }

        private int m_FramesPerTick = 0;
        private float m_TickFrequency = 0;

        /// <summary>
        /// Recalculates the <see cref="m_TickFrequency"/> and <see cref="m_FramesPerTick"/> that is
        /// used in <see cref="TimeTravelAdvanceTick"/>.
        /// </summary>
        protected void ConfigureFramesPerTick()
        {
            m_TickFrequency = 1.0f / GetTickRate();
            m_FramesPerTick = Math.Max((int)(m_TickFrequency / GetFrameRate()), 1);
        }

        /// <summary>
        /// Helper function to time travel exactly one tick's worth of time at the current frame and tick rates.
        /// This is NetcodeIntegrationTest instance relative and will automatically adjust based on <see cref="GetFrameRate"/>
        /// and <see cref="GetTickRate"/>.
        /// </summary>
        protected void TimeTravelAdvanceTick()
        {
            TimeTravel(m_TickFrequency, m_FramesPerTick);
        }

        /// <summary>
        /// Helper function to time travel exactly one tick's worth of time at the current frame and tick rates.
        /// ** Is based on the global k_DefaultTickRate and is not local to each NetcodeIntegrationTest instance **
        /// </summary>
        public static void TimeTravelToNextTick()
        {
            var timePassed = 1.0f / k_DefaultTickRate;
            var frameRate = Application.targetFrameRate;
            if (frameRate <= 0)
            {
                frameRate = 60;
            }
            var frames = Math.Max((int)(timePassed / frameRate), 1);
            TimeTravel(timePassed, frames);
        }

        /// <summary>
        /// Simulates one SDK frame. This can be used even without TimeTravel, though it's of somewhat less use
        /// without TimeTravel, as, without the mock transport, it will likely not provide enough time for any
        /// sent messages to be received even if called dozens of times.
        /// </summary>
        public static void SimulateOneFrame()
        {
            foreach (NetworkUpdateStage updateStage in Enum.GetValues(typeof(NetworkUpdateStage)))
            {
                var stage = updateStage;
                // These two are out of order numerically due to backward compatibility
                // requirements. We have to swap them to maintain correct execution
                // order.
                if (stage == NetworkUpdateStage.PostScriptLateUpdate)
                {
                    stage = NetworkUpdateStage.PostLateUpdate;
                }
                else if (stage == NetworkUpdateStage.PostLateUpdate)
                {
                    stage = NetworkUpdateStage.PostScriptLateUpdate;
                }

                NetworkUpdateLoop.RunNetworkUpdateStage(stage);
                string methodName = string.Empty;
                switch (stage)
                {
                    case NetworkUpdateStage.FixedUpdate:
                        methodName = "FixedUpdate"; // mapping NetworkUpdateStage.FixedUpdate to MonoBehaviour.FixedUpdate
                        break;
                    case NetworkUpdateStage.Update:
                        methodName = "Update"; // mapping NetworkUpdateStage.Update to MonoBehaviour.Update
                        break;
                    case NetworkUpdateStage.PreLateUpdate:
                        methodName = "LateUpdate"; // mapping NetworkUpdateStage.PreLateUpdate to MonoBehaviour.LateUpdate
                        break;
                }

                if (!string.IsNullOrEmpty(methodName))
                {
#if UNITY_2023_1_OR_NEWER
                    foreach (var obj in Object.FindObjectsByType<NetworkObject>(FindObjectsSortMode.InstanceID))
#else
                    foreach (var obj in Object.FindObjectsOfType<NetworkObject>())
#endif
                    {
                        var method = obj.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        method?.Invoke(obj, new object[] { });
                        foreach (var behaviour in obj.ChildNetworkBehaviours)
                        {
                            var behaviourMethod = behaviour.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            behaviourMethod?.Invoke(behaviour, new object[] { });
                        }
                    }
                }
            }
        }
    }
}
