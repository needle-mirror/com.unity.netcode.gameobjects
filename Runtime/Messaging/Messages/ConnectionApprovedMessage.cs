using System.Collections.Generic;
using Unity.Collections;

namespace Unity.Netcode
{
    internal struct ServiceConfig : INetworkSerializable
    {
        public uint SessionVersion;
        public bool IsRestoredSession;
        public ulong CurrentSessionOwner;
        public bool ServerRedistribution;
        public ulong SessionStateToken;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            if (serializer.IsWriter)
            {
                BytePacker.WriteValueBitPacked(serializer.GetFastBufferWriter(), SessionVersion);
                serializer.SerializeValue(ref IsRestoredSession);
                BytePacker.WriteValueBitPacked(serializer.GetFastBufferWriter(), CurrentSessionOwner);

                if (SessionVersion >= SessionConfig.ServerDistributionCompatible)
                {
                    serializer.SerializeValue(ref ServerRedistribution);
                }

                if (SessionVersion >= SessionConfig.SessionStateToken)
                {
                    serializer.SerializeValue(ref SessionStateToken);
                }
            }
            else
            {
                ByteUnpacker.ReadValueBitPacked(serializer.GetFastBufferReader(), out SessionVersion);
                serializer.SerializeValue(ref IsRestoredSession);
                ByteUnpacker.ReadValueBitPacked(serializer.GetFastBufferReader(), out CurrentSessionOwner);

                if (SessionVersion >= SessionConfig.ServerDistributionCompatible)
                {
                    serializer.SerializeValue(ref ServerRedistribution);
                }
                else
                {
                    ServerRedistribution = false;
                }

                if (SessionVersion >= SessionConfig.SessionStateToken)
                {
                    serializer.SerializeValue(ref SessionStateToken);
                }
                else
                {
                    SessionStateToken = 0;
                }
            }
        }
    }

    internal struct ConnectionApprovedMessage : INetworkMessage
    {
        private const int k_AddCMBServiceConfig = 2;
        private const int k_VersionAddClientIds = 1;
        public int Version => k_AddCMBServiceConfig;

        public ulong OwnerClientId;
        public int NetworkTick;
        // The cloud state service should set this if we are restoring a session
        public ServiceConfig ServiceConfig;
        public bool IsRestoredSession;
        public ulong CurrentSessionOwner;
        // Not serialized
        public bool IsDistributedAuthority;

        // Not serialized, held as references to serialize NetworkVariable data
        public HashSet<NetworkObject> SpawnedObjectsList;

        private FastBufferReader m_ReceivedSceneObjectData;

        public NativeArray<MessageVersionData> MessageVersions;

        public NativeArray<ulong> ConnectedClientIds;

        private int m_ReceiveMessageVersion;

        private ulong GetSessionOwner()
        {
            if (m_ReceiveMessageVersion >= k_AddCMBServiceConfig)
            {
                return ServiceConfig.CurrentSessionOwner;
            }
            else
            {
                return CurrentSessionOwner;
            }
        }

        private bool GetIsSessionRestor()
        {
            if (m_ReceiveMessageVersion >= k_AddCMBServiceConfig)
            {
                return ServiceConfig.IsRestoredSession;
            }
            else
            {
                return IsRestoredSession;
            }
        }

        public void Serialize(FastBufferWriter writer, int targetVersion)
        {
            // ============================================================
            // BEGIN FORBIDDEN SEGMENT
            // DO NOT CHANGE THIS HEADER. Everything added to this message
            // must go AFTER the message version header.
            // ============================================================
            BytePacker.WriteValueBitPacked(writer, MessageVersions.Length);
            foreach (var messageVersion in MessageVersions)
            {
                messageVersion.Serialize(writer);
            }
            // ============================================================
            // END FORBIDDEN SEGMENT
            // ============================================================

            BytePacker.WriteValueBitPacked(writer, OwnerClientId);
            BytePacker.WriteValueBitPacked(writer, NetworkTick);
            if (IsDistributedAuthority)
            {
                if (targetVersion >= k_AddCMBServiceConfig)
                {
                    ServiceConfig.IsRestoredSession = false;
                    ServiceConfig.CurrentSessionOwner = CurrentSessionOwner;
                    writer.WriteNetworkSerializable(ServiceConfig);
                }
                else
                {
                    writer.WriteValueSafe(IsRestoredSession);
                    BytePacker.WriteValueBitPacked(writer, CurrentSessionOwner);
                }
            }

            if (targetVersion >= k_VersionAddClientIds)
            {
                writer.WriteValueSafe(ConnectedClientIds);
            }

            uint sceneObjectCount = 0;

            // When SpawnedObjectsList is not null then scene management is disabled. Provide a list of
            // all observed and spawned NetworkObjects that the approved client needs to synchronize.
            if (SpawnedObjectsList != null)
            {
                var pos = writer.Position;
                writer.Seek(writer.Position + FastBufferWriter.GetWriteSize(sceneObjectCount));

                // Serialize NetworkVariable data
                foreach (var sobj in SpawnedObjectsList)
                {
                    if (sobj.SpawnWithObservers && (sobj.CheckObjectVisibility == null || sobj.CheckObjectVisibility(OwnerClientId)))
                    {
                        sobj.Observers.Add(OwnerClientId);
                        // In distributed authority mode, we send the currently known observers of each NetworkObject to the client being synchronized.
                        var sceneObject = sobj.GetMessageSceneObject(OwnerClientId, IsDistributedAuthority);
                        sceneObject.Serialize(writer);
                        ++sceneObjectCount;
                    }
                }

                writer.Seek(pos);
                // Can't pack this value because its space is reserved, so it needs to always use all the reserved space.
                writer.WriteValueSafe(sceneObjectCount);
                writer.Seek(writer.Length);
            }
            else
            {
                writer.WriteValueSafe(sceneObjectCount);
            }
        }

        public bool Deserialize(FastBufferReader reader, ref NetworkContext context, int receivedMessageVersion)
        {
            var networkManager = (NetworkManager)context.SystemOwner;
            if (!networkManager.IsClient)
            {
                return false;
            }

            // ============================================================
            // BEGIN FORBIDDEN SEGMENT
            // DO NOT CHANGE THIS HEADER. Everything added to this message
            // must go AFTER the message version header.
            // ============================================================
            ByteUnpacker.ReadValueBitPacked(reader, out int length);
            var messageHashesInOrder = new NativeArray<uint>(length, Allocator.Temp);
            for (var i = 0; i < length; ++i)
            {
                var messageVersion = new MessageVersionData();
                messageVersion.Deserialize(reader);
                networkManager.ConnectionManager.MessageManager.SetVersion(context.SenderId, messageVersion.Hash, messageVersion.Version);
                messageHashesInOrder[i] = messageVersion.Hash;

                // Update the received version since this message will always be passed version 0, due to the map not
                // being initialized until just now.
                var messageType = networkManager.ConnectionManager.MessageManager.GetMessageForHash(messageVersion.Hash);
                if (messageType == typeof(ConnectionApprovedMessage))
                {
                    receivedMessageVersion = messageVersion.Version;
                }
            }
            networkManager.ConnectionManager.MessageManager.SetServerMessageOrder(messageHashesInOrder);
            messageHashesInOrder.Dispose();
            // ============================================================
            // END FORBIDDEN SEGMENT
            // ============================================================
            m_ReceiveMessageVersion = receivedMessageVersion;
            ByteUnpacker.ReadValueBitPacked(reader, out OwnerClientId);
            ByteUnpacker.ReadValueBitPacked(reader, out NetworkTick);
            if (networkManager.DistributedAuthorityMode)
            {
                if (receivedMessageVersion >= k_AddCMBServiceConfig)
                {
                    reader.ReadNetworkSerializable(out ServiceConfig);
                    networkManager.SessionConfig = new SessionConfig(ServiceConfig);
                }
                else
                {
                    reader.ReadValueSafe(out IsRestoredSession);
                    ByteUnpacker.ReadValueBitPacked(reader, out CurrentSessionOwner);
                    networkManager.SessionConfig = new SessionConfig(SessionConfig.NoFeatureCompatibility);
                }
            }

            if (receivedMessageVersion >= k_VersionAddClientIds)
            {
                reader.ReadValueSafe(out ConnectedClientIds, Allocator.TempJob);
            }
            else
            {
                ConnectedClientIds = new NativeArray<ulong>(0, Allocator.TempJob);
            }

            m_ReceivedSceneObjectData = reader;
            return true;
        }

        public void Handle(ref NetworkContext context)
        {
            var networkManager = (NetworkManager)context.SystemOwner;

            if (networkManager.CMBServiceConnection && networkManager.LocalClient.IsSessionOwner && networkManager.NetworkConfig.EnableSceneManagement)
            {
                if (networkManager.LocalClientId != OwnerClientId)
                {
                    if (NetworkLog.CurrentLogLevel <= LogLevel.Developer)
                    {
                        NetworkLog.LogInfo($"[Session Owner] Received connection approved for Client-{OwnerClientId}! Synchronizing...");
                    }

                    networkManager.SceneManager.SynchronizeNetworkObjects(OwnerClientId);
                }
                else
                {
                    NetworkLog.LogWarning($"[Client-{OwnerClientId}] Receiving duplicate connection approved. Client is already connected!");
                }
                ConnectedClientIds.Dispose();
                return;
            }

            if (NetworkLog.CurrentLogLevel <= LogLevel.Developer)
            {
                NetworkLog.LogInfo($"[Client-{OwnerClientId}] Connection approved! Synchronizing...");
            }

            networkManager.LocalClientId = OwnerClientId;
            networkManager.MessageManager.SetLocalClientId(networkManager.LocalClientId);
            networkManager.NetworkMetrics.SetConnectionId(networkManager.LocalClientId);

            if (networkManager.DistributedAuthorityMode)
            {
                networkManager.SetSessionOwner(GetSessionOwner());
                if (networkManager.LocalClient.IsSessionOwner && networkManager.NetworkConfig.EnableSceneManagement)
                {
                    networkManager.SceneManager.InitializeScenesLoaded();
                }
            }

            var time = new NetworkTime(networkManager.NetworkTickSystem.TickRate, NetworkTick);
            networkManager.NetworkTimeSystem.Reset(time.Time, 0.15f); // Start with a constant RTT of 150 until we receive values from the transport.
            networkManager.NetworkTickSystem.Reset(networkManager.NetworkTimeSystem.LocalTime, networkManager.NetworkTimeSystem.ServerTime);

            networkManager.ConnectionManager.LocalClient.SetRole(false, true, networkManager);
            networkManager.ConnectionManager.LocalClient.IsApproved = true;
            networkManager.ConnectionManager.LocalClient.ClientId = OwnerClientId;
            // Stop the client-side approval timeout coroutine since we are approved.
            networkManager.ConnectionManager.StopClientApprovalCoroutine();

            foreach (var clientId in ConnectedClientIds)
            {
                // DANGO-TODO: Revisit the entire connection sequence and determine why we would need to check both cases as we shouldn't have to =or= we could
                // try removing this after the Rust server connection sequence stuff is resolved. (Might be only needed if scene management is disabled)
                // If there is any disconnect between the connection sequence of Ids vs ConnectedClients, then add the client.
                if (!networkManager.ConnectionManager.ConnectedClientIds.Contains(clientId) || !networkManager.ConnectionManager.ConnectedClients.ContainsKey(clientId))
                {
                    networkManager.ConnectionManager.AddClient(clientId);
                }
            }

            // Dispose after it has been used.
            ConnectedClientIds.Dispose();

            // Only if scene management is disabled do we handle NetworkObject synchronization at this point
            if (!networkManager.NetworkConfig.EnableSceneManagement)
            {
                /// Mark the client being connected before running through the spawning synchronization so we
                /// can assure that if a user attempts to spawn something when an already spawned NetworkObject
                /// is spawned (during the initial synchronization just below) it will not error out complaining
                /// about the player not being connected.
                /// The check for this is done within <see cref="NetworkObject.SpawnInternal(bool, ulong, bool)"/>
                networkManager.IsConnectedClient = true;

                // DANGO-TODO: This is a temporary fix for no DA CMB scene event handling.
                // We will either use this same concept or provide some way for the CMB state plugin to handle it.
                if (networkManager.DistributedAuthorityMode && networkManager.LocalClient.IsSessionOwner)
                {
                    networkManager.SpawnManager.ServerSpawnSceneObjectsOnStartSweep();
                }
                else
                {
                    networkManager.SpawnManager.DestroySceneObjects();
                }

                m_ReceivedSceneObjectData.ReadValueSafe(out uint sceneObjectCount);

                // Deserializing NetworkVariable data is deferred from Receive() to Handle to avoid needing
                // to create a list to hold the data. This is a breach of convention for performance reasons.
                for (ushort i = 0; i < sceneObjectCount; i++)
                {
                    var sceneObject = new NetworkObject.SceneObject();
                    sceneObject.Deserialize(m_ReceivedSceneObjectData);
                    NetworkObject.AddSceneObject(sceneObject, m_ReceivedSceneObjectData, networkManager);
                }

                if (networkManager.AutoSpawnPlayerPrefabClientSide)
                {
                    networkManager.ConnectionManager.CreateAndSpawnPlayer(OwnerClientId);
                }

                if (NetworkLog.CurrentLogLevel <= LogLevel.Developer)
                {
                    NetworkLog.LogInfo($"[Client-{OwnerClientId}][Scene Management Disabled] Synchronization complete!");
                }
                // When scene management is disabled we notify after everything is synchronized
                networkManager.ConnectionManager.InvokeOnClientConnectedCallback(OwnerClientId);

                // For convenience, notify all NetworkBehaviours that synchronization is complete.
                networkManager.SpawnManager.NotifyNetworkObjectsSynchronized();
            }
            else
            {
                if (networkManager.DistributedAuthorityMode && networkManager.CMBServiceConnection && networkManager.LocalClient.IsSessionOwner && networkManager.NetworkConfig.EnableSceneManagement)
                {
                    // Mark the client being connected
                    networkManager.IsConnectedClient = networkManager.ConnectionManager.LocalClient.IsApproved;

                    networkManager.SceneManager.IsRestoringSession = GetIsSessionRestor();

                    if (!networkManager.SceneManager.IsRestoringSession)
                    {
                        // Synchronize the service with the initial session owner's loaded scenes and spawned objects
                        networkManager.SceneManager.SynchronizeNetworkObjects(NetworkManager.ServerClientId, true);

                        // Spawn any in-scene placed NetworkObjects
                        networkManager.SpawnManager.ServerSpawnSceneObjectsOnStartSweep();

                        // Spawn the local player of the session owner
                        if (networkManager.AutoSpawnPlayerPrefabClientSide)
                        {
                            networkManager.ConnectionManager.CreateAndSpawnPlayer(OwnerClientId);
                        }

                        // Synchronize the service with the initial session owner's loaded scenes and spawned objects
                        networkManager.SceneManager.SynchronizeNetworkObjects(NetworkManager.ServerClientId, true);

                        // With scene management enabled and since the session owner doesn't send a scene event synchronize to itself,
                        // we need to notify the session owner that everything should be synchronized/spawned at this time.
                        networkManager.SpawnManager.NotifyNetworkObjectsSynchronized();

                        // When scene management is enabled and since the session owner is synchronizing the service (i.e. acting like  host),
                        // we need to locallyh invoke the OnClientConnected callback at this point in time.
                        networkManager.ConnectionManager.InvokeOnClientConnectedCallback(OwnerClientId);
                    }
                }
            }
        }
    }
}
