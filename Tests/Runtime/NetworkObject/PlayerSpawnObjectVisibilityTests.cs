using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Netcode.TestHelpers.Runtime;
using UnityEngine;
using UnityEngine.TestTools;


namespace Unity.Netcode.RuntimeTests
{
    [TestFixture(HostOrServer.Server)]
    [TestFixture(HostOrServer.Host)]
    internal class PlayerSpawnObjectVisibilityTests : NetcodeIntegrationTest
    {
        protected override int NumberOfClients => 0;

        public enum PlayerSpawnStages
        {
            OnNetworkSpawn,
            OnNetworkPostSpawn,
        }

        public PlayerSpawnObjectVisibilityTests(HostOrServer hostOrServer) : base(hostOrServer) { }

        public class PlayerVisibilityTestComponent : NetworkBehaviour
        {
            public PlayerSpawnStages Stage;

            private void Awake()
            {
                var networkObject = GetComponent<NetworkObject>();
                // Assure the player prefab will not spawn with observers.
                // This assures that when the server/host spawns the connecting client's
                // player prefab, the spawn object will initially not be spawnd on the client side.
                networkObject.SpawnWithObservers = false;
            }

            public override void OnNetworkSpawn()
            {
                ShowToClient(PlayerSpawnStages.OnNetworkSpawn);
                base.OnNetworkSpawn();
            }

            protected override void OnNetworkPostSpawn()
            {
                ShowToClient(PlayerSpawnStages.OnNetworkPostSpawn);
                base.OnNetworkPostSpawn();
            }

            private void ShowToClient(PlayerSpawnStages currentStage)
            {
                if (!HasAuthority || Stage != currentStage)
                {
                    return;
                }
                NetworkObject.NetworkShow(OwnerClientId);
            }
        }

        protected override void OnCreatePlayerPrefab()
        {
            m_PlayerPrefab.AddComponent<PlayerVisibilityTestComponent>();
            base.OnCreatePlayerPrefab();
        }

        /// <summary>
        /// Tests the scenario where under a client-server network topology if a player prefab
        /// is spawned by the server with no observers but the player prefab itself has server
        /// side script that will network show the spawned object to the owning client.
        ///
        /// Because NetworkShow will defer the CreateObjectMessage until the late update, the
        /// server/host needs to filter out including anything within the synchronization
        /// message that already has pending visibility.
        /// </summary>
        /// <param name="spawnStage">Spawn stages to test</param>
        /// <returns>IEnumerator</returns>
        [UnityTest]
        public IEnumerator NetworkShowOnSpawnTest([Values] PlayerSpawnStages spawnStage)
        {
            m_PlayerPrefab.GetComponent<PlayerVisibilityTestComponent>().Stage = spawnStage;

            yield return CreateAndStartNewClient();

            yield return new WaitForSeconds(0.25f);

            NetcodeLogAssert.LogWasNotReceived(LogType.Warning, new Regex("but it is already in the spawned list!"));
            var client = GetNonAuthorityNetworkManager();
            Assert.True(client.LocalClient.PlayerObject != null, $"Client-{client.LocalClientId} does not have a player object!");
        }
    }
}
