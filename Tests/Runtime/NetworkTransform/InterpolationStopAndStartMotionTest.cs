using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Netcode.Components;
using Unity.Netcode.TestHelpers.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Netcode.RuntimeTests
{
    [TestFixture(HostOrServer.Host, NetworkTransform.InterpolationTypes.Lerp)]
    [TestFixture(HostOrServer.Host, NetworkTransform.InterpolationTypes.SmoothDampening)]
    [TestFixture(HostOrServer.DAHost, NetworkTransform.InterpolationTypes.Lerp)]
    [TestFixture(HostOrServer.DAHost, NetworkTransform.InterpolationTypes.SmoothDampening)]
    internal class InterpolationStopAndStartMotionTest : IntegrationTestWithApproximation
    {
        protected override int NumberOfClients => 2;

        private GameObject m_TestPrefab;

        private TestStartStopTransform m_AuthorityInstance;
        private List<TestStartStopTransform> m_NonAuthorityInstances = new List<TestStartStopTransform>();

        private NetworkTransform.InterpolationTypes m_InterpolationType;
        private List<NetworkManager> m_NetworkManagers = new List<NetworkManager>();
        private NetworkManager m_AuthorityNetworkManager;

        private int m_NumberOfUpdates;
        private Vector3 m_Direction;

        public InterpolationStopAndStartMotionTest(HostOrServer hostOrServer, NetworkTransform.InterpolationTypes interpolationType) : base(hostOrServer)
        {
            m_InterpolationType = interpolationType;
        }

        protected override void OnServerAndClientsCreated()
        {
            m_TestPrefab = CreateNetworkObjectPrefab("TestObj");
            var testStartStopTransform = m_TestPrefab.AddComponent<TestStartStopTransform>();
            testStartStopTransform.PositionInterpolationType = m_InterpolationType;
            base.OnServerAndClientsCreated();
        }

        private bool WaitForInstancesToSpawn()
        {
            foreach (var networkManager in m_NetworkManagers)
            {
                if (networkManager == m_AuthorityNetworkManager)
                {
                    continue;
                }

                if (!networkManager.SpawnManager.SpawnedObjects.ContainsKey(m_AuthorityInstance.NetworkObjectId))
                {
                    return false;
                }
            }
            return true;
        }

        private bool WaitForInstancesToFinishInterpolation()
        {
            m_NonAuthorityInstances.Clear();
            foreach (var networkManager in m_NetworkManagers)
            {
                if (networkManager == m_AuthorityNetworkManager)
                {
                    continue;
                }

                if (!networkManager.SpawnManager.SpawnedObjects.ContainsKey(m_AuthorityInstance.NetworkObjectId))
                {
                    return false;
                }

                var nonAuthority = networkManager.SpawnManager.SpawnedObjects[m_AuthorityInstance.NetworkObjectId].GetComponent<TestStartStopTransform>();

                // Each non-authority instance needs to have reached their final target and reset waiting for the
                // object to start moving again.
                var positionInterpolator = nonAuthority.GetPositionInterpolator();
                if (positionInterpolator.InterpolateState.Target.HasValue)
                {
                    return false;
                }

                if (!Approximately(nonAuthority.transform.position, m_AuthorityInstance.transform.position))
                {
                    return false;
                }

                m_NonAuthorityInstances.Add(nonAuthority);
            }
            return true;
        }

        [UnityTest]
        public IEnumerator StopAndStartMotion()
        {
            m_NetworkManagers.AddRange(m_ClientNetworkManagers);
            if (!UseCMBService())
            {
                m_NetworkManagers.Insert(0, m_ServerNetworkManager);
            }
            m_AuthorityNetworkManager = m_NetworkManagers[0];

            m_AuthorityInstance = SpawnObject(m_TestPrefab, m_AuthorityNetworkManager).GetComponent<TestStartStopTransform>();
            // Wait for all clients to spawn the instance
            yield return WaitForConditionOrTimeOut(WaitForInstancesToSpawn);
            AssertOnTimeout($"Not all clients spawned {m_AuthorityInstance.name}!");

            ////// Start Motion
            // Have authority move in a direction for a short period of time
            m_Direction = GetRandomVector3(-10, 10).normalized;
            m_NumberOfUpdates = 0;
            m_AuthorityNetworkManager.NetworkTickSystem.Tick += NetworkTickSystem_Tick;

            yield return WaitForConditionOrTimeOut(() => m_NumberOfUpdates >= 10);
            AssertOnTimeout($"Timed out waiting for all updates to be applied to the authority instance!");

            ////// Finish interpolating and wait for each interpolator to detect a stop in the motion
            // Wait for all non-authority instances to finish interpolating to the final destination point.
            yield return WaitForConditionOrTimeOut(WaitForInstancesToFinishInterpolation);
            AssertOnTimeout($"Not all clients finished interpolating {m_AuthorityInstance.name}!");

            // Start recording the state updates on the non-authority instances
            foreach (var testTransform in m_NonAuthorityInstances)
            {
                testTransform.CheckStateUpdates = true;
            }

            ////// Stop to Start motion begins here
            m_Direction = GetRandomVector3(-10, 10).normalized;
            m_NumberOfUpdates = 0;
            m_AuthorityNetworkManager.NetworkTickSystem.Tick += NetworkTickSystem_Tick;

            yield return WaitForConditionOrTimeOut(() => m_NumberOfUpdates >= 10);
            AssertOnTimeout($"Timed out waiting for all updates to be applied to the authority instance!");

            // Wait for all non-authority instances to finish interpolating to the final destination point.
            yield return WaitForConditionOrTimeOut(WaitForInstancesToFinishInterpolation);
            AssertOnTimeout($"Not all clients finished interpolating {m_AuthorityInstance.name}!");

            // Checks that the time between the first and second state update is approximately the tick frequency
            foreach (var testTransform in m_NonAuthorityInstances)
            {
                var deltaVariance = testTransform.GetTimeDeltaVarience();
                Assert.True(Approximately(deltaVariance, s_DefaultWaitForTick.waitTime), $"{testTransform.name}'s delta variance was {deltaVariance} when it should have been approximately {s_DefaultWaitForTick.waitTime}!");
            }
        }

        /// <summary>
        /// Moves the authority instance once per tick to simulate a change in transform state that occurs
        /// every tick.
        /// </summary>
        private void NetworkTickSystem_Tick()
        {
            m_NumberOfUpdates++;
            m_AuthorityInstance.transform.position = m_AuthorityInstance.transform.position + m_Direction * 2;
            if (m_NumberOfUpdates >= 10)
            {
                m_AuthorityNetworkManager.NetworkTickSystem.Tick -= NetworkTickSystem_Tick;
            }
        }

        internal class TestStartStopTransform : NetworkTransform
        {

            public bool CheckStateUpdates;

            private BufferedLinearInterpolatorVector3 m_PosInterpolator;

            private Dictionary<int, StateEntry> m_StatesProcessed = new Dictionary<int, StateEntry>();

            public struct StateEntry
            {
                public float TimeAdded;
                public BufferedLinearInterpolator<Vector3>.CurrentState State;
            }

            protected override void Awake()
            {
                base.Awake();
                m_PosInterpolator = GetPositionInterpolator();
            }

            /// <summary>
            /// Checks the time that passed between the first and second state updates.
            /// </summary>
            /// <returns>time passed as a float</returns>
            public float GetTimeDeltaVarience()
            {
                var stateKeys = m_StatesProcessed.Keys.ToList();
                var firstState = m_StatesProcessed[stateKeys[0]];
                var secondState = m_StatesProcessed[stateKeys[1]];

                var firstAndSecondTimeDelta = secondState.TimeAdded - firstState.TimeAdded;

                // Get the delta time between the two times of both the first and second state.
                // Then add the time it should have taken to get to the second state, and this should be the total time to interpolate
                // from the current position to the target position of the second state update.
                var stateDelta = (float)(secondState.State.Target.Value.TimeSent - firstState.State.Target.Value.TimeSent + secondState.State.TimeToTargetValue);
                // Return the time detla between the time that passed and the time that should have passed processing the states.
                return Mathf.Abs(stateDelta - firstAndSecondTimeDelta);
            }

            public override void OnUpdate()
            {
                base.OnUpdate();

                // If we are checking the state updates, then we want to track each unique state update
                if (CheckStateUpdates)
                {
                    // Make sure we have a valid target
                    if (m_PosInterpolator.InterpolateState.Target.HasValue)
                    {
                        // If the state update's identifier is different
                        var itemId = m_PosInterpolator.InterpolateState.Target.Value.ItemId;
                        if (!m_StatesProcessed.ContainsKey(itemId))
                        {
                            // Add it to the table of state updates
                            var stateEntry = new StateEntry()
                            {
                                TimeAdded = Time.realtimeSinceStartup,
                                State = m_PosInterpolator.InterpolateState,
                            };

                            m_StatesProcessed.Add(itemId, stateEntry);
                        }
                    }
                }
            }
        }
    }
}
