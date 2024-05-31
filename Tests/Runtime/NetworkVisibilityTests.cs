using System.Collections;
using System.Linq;
using NUnit.Framework;
using Unity.Netcode.TestHelpers.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Netcode.RuntimeTests
{
    [TestFixture(SceneManagementState.SceneManagementEnabled, NetworkTopologyTypes.DistributedAuthority)]
    [TestFixture(SceneManagementState.SceneManagementDisabled, NetworkTopologyTypes.DistributedAuthority)]
    [TestFixture(SceneManagementState.SceneManagementEnabled, NetworkTopologyTypes.ClientServer)]
    [TestFixture(SceneManagementState.SceneManagementDisabled, NetworkTopologyTypes.ClientServer)]
    internal class NetworkVisibilityTests : NetcodeIntegrationTest
    {

        protected override int NumberOfClients => 1;
        private GameObject m_TestNetworkPrefab;
        private bool m_SceneManagementEnabled;

        public NetworkVisibilityTests(SceneManagementState sceneManagementState, NetworkTopologyTypes networkTopologyType) : base(networkTopologyType)
        {
            m_SceneManagementEnabled = sceneManagementState == SceneManagementState.SceneManagementEnabled;
        }

        protected override void OnServerAndClientsCreated()
        {
            m_TestNetworkPrefab = CreateNetworkObjectPrefab("Object");
            m_TestNetworkPrefab.AddComponent<NetworkVisibilityComponent>();
            m_ServerNetworkManager.NetworkConfig.EnableSceneManagement = m_SceneManagementEnabled;
            foreach (var clientNetworkManager in m_ClientNetworkManagers)
            {
                clientNetworkManager.NetworkConfig.EnableSceneManagement = m_SceneManagementEnabled;
            }
            base.OnServerAndClientsCreated();
        }


        protected override IEnumerator OnServerAndClientsConnected()
        {
            SpawnObject(m_TestNetworkPrefab, m_ServerNetworkManager);

            yield return base.OnServerAndClientsConnected();
        }

        [UnityTest]
        public IEnumerator HiddenObjectsTest()
        {
#if UNITY_2023_1_OR_NEWER
            yield return WaitForConditionOrTimeOut(() => Object.FindObjectsByType<NetworkVisibilityComponent>(FindObjectsSortMode.None).Where((c) => c.IsSpawned).Count() == 2);
#else
            yield return WaitForConditionOrTimeOut(() => Object.FindObjectsOfType<NetworkVisibilityComponent>().Where((c) => c.IsSpawned).Count() == 2);
#endif

            Assert.IsFalse(s_GlobalTimeoutHelper.TimedOut, "Timed out waiting for the visible object count to equal 2!");
        }
    }
}
