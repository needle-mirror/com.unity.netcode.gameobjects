using System.Collections;
using NUnit.Framework;
using Unity.Netcode.TestHelpers.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Netcode.RuntimeTests
{
    public class NetworkBehaviourPrePostSpawnTests : NetcodeIntegrationTest
    {
        protected override int NumberOfClients => 0;

        private bool m_AllowServerToStart;

        private GameObject m_PrePostSpawnObject;

        protected override void OnServerAndClientsCreated()
        {
            m_PrePostSpawnObject = CreateNetworkObjectPrefab("PrePostSpawn");
            // Reverse the order of the components to get inverted spawn sequence
            m_PrePostSpawnObject.AddComponent<NetworkBehaviourPostSpawn>();
            m_PrePostSpawnObject.AddComponent<NetworkBehaviourPreSpawn>();
            base.OnServerAndClientsCreated();
        }

        public class NetworkBehaviourPreSpawn : NetworkBehaviour
        {
            public static int ValueToSet;
            public bool OnNetworkPreSpawnCalled;
            public bool NetworkVarValueMatches;

            public NetworkVariable<int> TestNetworkVariable;

            protected override void OnNetworkPreSpawn(ref NetworkManager networkManager)
            {
                OnNetworkPreSpawnCalled = true;
                // If we are the server, then set the randomly generated value (1-200).
                // Otherwise, just set the value to 0.
                var val = networkManager.IsServer ? ValueToSet : 0;
                // Instantiate the NetworkVariable as everyone read & owner write while also setting the value
                TestNetworkVariable = new NetworkVariable<int>(val, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
                base.OnNetworkPreSpawn(ref networkManager);
            }

            public override void OnNetworkSpawn()
            {
                // For both client and server this should match at this point
                NetworkVarValueMatches = TestNetworkVariable.Value == ValueToSet;
                base.OnNetworkSpawn();
            }
        }

        public class NetworkBehaviourPostSpawn : NetworkBehaviour
        {
            public bool OnNetworkPostSpawnCalled;

            private NetworkBehaviourPreSpawn m_NetworkBehaviourPreSpawn;

            public int ValueSet;

            public override void OnNetworkSpawn()
            {
                // Obtain the NetworkBehaviourPreSpawn component
                // (could also do this during OnNetworkPreSpawn if we wanted)
                m_NetworkBehaviourPreSpawn = GetComponent<NetworkBehaviourPreSpawn>();
                base.OnNetworkSpawn();
            }

            protected override void OnNetworkPostSpawn()
            {
                OnNetworkPostSpawnCalled = true;
                // We should be able to access the component we got during OnNetworkSpawn and all values should be set
                // (i.e. OnNetworkSpawn run on all NetworkObject relative NetworkBehaviours)
                ValueSet = m_NetworkBehaviourPreSpawn.TestNetworkVariable.Value;
                base.OnNetworkPostSpawn();
            }

        }

        protected override bool CanStartServerAndClients()
        {
            return m_AllowServerToStart;
        }

        protected override IEnumerator OnSetup()
        {
            m_AllowServerToStart = false;
            return base.OnSetup();
        }

        protected override void OnNewClientCreated(NetworkManager networkManager)
        {
            networkManager.NetworkConfig.Prefabs = m_ServerNetworkManager.NetworkConfig.Prefabs;
            base.OnNewClientCreated(networkManager);
        }

        /// <summary>
        /// This validates that pre spawn can be used to instantiate and assign a NetworkVariable (or other prespawn tasks)
        /// which can be useful for assigning a NetworkVariable value on the server side when the NetworkVariable has owner write permissions.
        /// This also assures that duruing post spawn all associated NetworkBehaviours have run through the OnNetworkSpawn pass (i.e. OnNetworkSpawn order is not an issue)
        /// </summary>
        [UnityTest]
        public IEnumerator OnNetworkPreAndPostSpawn()
        {
            m_AllowServerToStart = true;
            NetworkBehaviourPreSpawn.ValueToSet = Random.Range(1, 200);
            yield return StartServerAndClients();

            yield return CreateAndStartNewClient();

            // Spawn the object with the newly joined client as the owner
            var serverInstance = SpawnObject(m_PrePostSpawnObject, m_ClientNetworkManagers[0]);
            var serverNetworkObject = serverInstance.GetComponent<NetworkObject>();
            var serverPreSpawn = serverInstance.GetComponent<NetworkBehaviourPreSpawn>();
            var serverPostSpawn = serverInstance.GetComponent<NetworkBehaviourPostSpawn>();

            yield return WaitForConditionOrTimeOut(() => s_GlobalNetworkObjects.ContainsKey(m_ClientNetworkManagers[0].LocalClientId)
            && s_GlobalNetworkObjects[m_ClientNetworkManagers[0].LocalClientId].ContainsKey(serverNetworkObject.NetworkObjectId));
            AssertOnTimeout($"Client-{m_ClientNetworkManagers[0].LocalClientId} failed to spawn {nameof(NetworkObject)} id-{serverNetworkObject.NetworkObjectId}!");

            var clientNetworkObject = s_GlobalNetworkObjects[m_ClientNetworkManagers[0].LocalClientId][serverNetworkObject.NetworkObjectId];
            var clientPreSpawn = clientNetworkObject.GetComponent<NetworkBehaviourPreSpawn>();
            var clientPostSpawn = clientNetworkObject.GetComponent<NetworkBehaviourPostSpawn>();

            Assert.IsTrue(serverPreSpawn.OnNetworkPreSpawnCalled, $"[Server-side] OnNetworkPreSpawn not invoked!");
            Assert.IsTrue(clientPreSpawn.OnNetworkPreSpawnCalled, $"[Client-side] OnNetworkPreSpawn not invoked!");
            Assert.IsTrue(serverPostSpawn.OnNetworkPostSpawnCalled, $"[Server-side] OnNetworkPostSpawn not invoked!");
            Assert.IsTrue(clientPostSpawn.OnNetworkPostSpawnCalled, $"[Client-side] OnNetworkPostSpawn not invoked!");

            Assert.IsTrue(serverPreSpawn.NetworkVarValueMatches, $"[Server-side][PreSpawn] Value {NetworkBehaviourPreSpawn.ValueToSet} does not match {serverPreSpawn.TestNetworkVariable.Value}!");
            Assert.IsTrue(clientPreSpawn.NetworkVarValueMatches, $"[Client-side][PreSpawn] Value {NetworkBehaviourPreSpawn.ValueToSet} does not match {clientPreSpawn.TestNetworkVariable.Value}!");

            Assert.IsTrue(serverPostSpawn.ValueSet == NetworkBehaviourPreSpawn.ValueToSet, $"[Server-side][PostSpawn] Value {NetworkBehaviourPreSpawn.ValueToSet} does not match {serverPostSpawn.ValueSet}!");
            Assert.IsTrue(clientPostSpawn.ValueSet == NetworkBehaviourPreSpawn.ValueToSet, $"[Client-side][PostSpawn] Value {NetworkBehaviourPreSpawn.ValueToSet} does not match {clientPostSpawn.ValueSet}!");
        }
    }
}
