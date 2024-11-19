using System.Collections.Generic;
using Unity.Collections;

namespace Unity.Netcode
{
    /// <summary>
    /// Configuration for the default method by which an RPC is communicated across the network
    /// </summary>
    public enum SendTo
    {
        /// <summary>
        /// Send to the NetworkObject's current owner.
        /// Will execute locally if the local process is the owner.
        /// </summary>
        Owner,
        /// <summary>
        /// Send to everyone but the current owner, filtered to the current observer list.
        /// Will execute locally if the local process is not the owner.
        /// </summary>
        NotOwner,
        /// <summary>
        /// Send to the server, regardless of ownership.
        /// Will execute locally if invoked on the server.
        /// </summary>
        Server,
        /// <summary>
        /// Send to everyone but the server, filtered to the current observer list.
        /// Will NOT send to a server running in host mode - it is still treated as a server.
        /// If you want to send to servers when they are host, but not when they are dedicated server, use
        /// <see cref="ClientsAndHost"/>.
        /// <br />
        /// <br />
        /// Will execute locally if invoked on a client.
        /// Will NOT execute locally if invoked on a server running in host mode.
        /// </summary>
        NotServer,
        /// <summary>
        /// Execute this RPC locally.
        /// <br />
        /// <br />
        /// Normally this is no different from a standard function call.
        /// <br />
        /// <br />
        /// Using the DeferLocal parameter of the attribute or the LocalDeferMode override in RpcSendParams,
        /// this can allow an RPC to be processed on localhost with a one-frame delay as if it were sent over
        /// the network.
        /// </summary>
        Me,
        /// <summary>
        /// Send this RPC to everyone but the local machine, filtered to the current observer list.
        /// </summary>
        NotMe,
        /// <summary>
        /// Send this RPC to everone, filtered to the current observer list.
        /// Will execute locally.
        /// </summary>
        Everyone,
        /// <summary>
        /// Send this RPC to all clients, including the host, if a host exists.
        /// If the server is running in host mode, this is the same as <see cref="Everyone" />.
        /// If the server is running in dedicated server mode, this is the same as <see cref="NotServer" />.
        /// </summary>
        ClientsAndHost,
        /// <summary>
        /// This RPC cannot be sent without passing in a target in RpcSendParams.
        /// </summary>
        SpecifiedInParams
    }

    /// <summary>
    /// This parameter configures a performance optimization. This optimization is not valid in all situations.<br />
    /// Because BaseRpcTarget is a managed type, allocating a new one is expensive, as it puts pressure on the garbage collector.
    /// </summary>
    /// <remarks>
    /// When using a <see cref="Temp"/> allocation type for the RPC target(s):<br />
    /// You typically don't need to worry about persisting the <see cref="BaseRpcTarget"/> generated.
    /// When using a <see cref="Persistent"/> allocation type for the RPC target(s): <br />
    /// You will want to use <see cref="RpcTarget"/>, which returns <see cref="BaseRpcTarget"/>, during <see cref="NetworkBehaviour"/> initialization (i.e. <see cref="NetworkBehaviour.OnNetworkPostSpawn"/>) and it to a property.<br />
    /// Then, When invoking the RPC, you would use your <see cref="BaseRpcTarget"/> which is a persisted allocation of a given set of client identifiers.
    /// !! Important !!<br />
    /// You will want to invoke <see cref="BaseRpcTarget.Dispose"/> of any persisted properties created via <see cref="RpcTarget"/> when despawning or destroying the associated <see cref="NetworkBehaviour"/> component's <see cref="NetworkObject"/>. Not doing so will result in small memory leaks.
    /// </remarks>
    public enum RpcTargetUse
    {
        /// <summary>
        /// Creates a temporary <see cref="BaseRpcTarget"/> used for the frame an <see cref="RpcAttribute"/> decorated method is invoked.
        /// </summary>
        Temp,
        /// <summary>
        /// Creates a persisted <see cref="BaseRpcTarget"/> that does not change and will persist until <see cref="BaseRpcTarget.Dispose"/> is called.
        /// </summary>
        Persistent
    }

    /// <summary>
    /// Implementations of the various <see cref="SendTo"/> options, as well as additional runtime-only options
    /// <see cref="Single"/>,
    /// <see cref="Group(NativeArray{ulong})"/>,
    /// <see cref="Group(NativeList{ulong})"/>,
    /// <see cref="Group(ulong[])"/>,
    /// <see cref="Group{T}(T)"/>, <see cref="Not(ulong)"/>,
    /// <see cref="Not(NativeArray{ulong})"/>,
    /// <see cref="Not(NativeList{ulong})"/>,
    /// <see cref="Not(ulong[])"/>, and
    /// <see cref="Not{T}(T)"/>
    /// </summary>
    public class RpcTarget
    {
        private NetworkManager m_NetworkManager;
        internal RpcTarget(NetworkManager manager)
        {
            m_NetworkManager = manager;

            Everyone = new EveryoneRpcTarget(manager);
            Owner = new OwnerRpcTarget(manager);
            NotOwner = new NotOwnerRpcTarget(manager);
            Server = new ServerRpcTarget(manager);
            NotServer = new NotServerRpcTarget(manager);
            NotMe = new NotMeRpcTarget(manager);
            Me = new LocalSendRpcTarget(manager);
            ClientsAndHost = new ClientsAndHostRpcTarget(manager);

            m_CachedProxyRpcTargetGroup = new ProxyRpcTargetGroup(manager);
            m_CachedTargetGroup = new RpcTargetGroup(manager);
            m_CachedDirectSendTarget = new DirectSendRpcTarget(manager);
            m_CachedProxyRpcTarget = new ProxyRpcTarget(0, manager);

            m_CachedProxyRpcTargetGroup.Lock();
            m_CachedTargetGroup.Lock();
            m_CachedDirectSendTarget.Lock();
            m_CachedProxyRpcTarget.Lock();
        }

        public void Dispose()
        {
            Everyone.Dispose();
            Owner.Dispose();
            NotOwner.Dispose();
            Server.Dispose();
            NotServer.Dispose();
            NotMe.Dispose();
            Me.Dispose();
            ClientsAndHost.Dispose();

            m_CachedProxyRpcTargetGroup.Unlock();
            m_CachedTargetGroup.Unlock();
            m_CachedDirectSendTarget.Unlock();
            m_CachedProxyRpcTarget.Unlock();

            m_CachedProxyRpcTargetGroup.Dispose();
            m_CachedTargetGroup.Dispose();
            m_CachedDirectSendTarget.Dispose();
            m_CachedProxyRpcTarget.Dispose();
        }


        /// <summary>
        /// Send to the NetworkObject's current owner.
        /// Will execute locally if the local process is the owner.
        /// </summary>
        public BaseRpcTarget Owner;

        /// <summary>
        /// Send to everyone but the current owner, filtered to the current observer list.
        /// Will execute locally if the local process is not the owner.
        /// </summary>
        public BaseRpcTarget NotOwner;

        /// <summary>
        /// Send to the server, regardless of ownership.
        /// Will execute locally if invoked on the server.
        /// </summary>
        public BaseRpcTarget Server;

        /// <summary>
        /// Send to everyone but the server, filtered to the current observer list.
        /// Will NOT send to a server running in host mode - it is still treated as a server.
        /// If you want to send to servers when they are host, but not when they are dedicated server, use
        /// <see cref="SendTo.ClientsAndHost"/>.
        /// <br />
        /// <br />
        /// Will execute locally if invoked on a client.
        /// Will NOT execute locally if invoked on a server running in host mode.
        /// </summary>
        public BaseRpcTarget NotServer;

        /// <summary>
        /// Execute this RPC locally.
        /// <br />
        /// <br />
        /// Normally this is no different from a standard function call.
        /// <br />
        /// <br />
        /// Using the DeferLocal parameter of the attribute or the LocalDeferMode override in RpcSendParams,
        /// this can allow an RPC to be processed on localhost with a one-frame delay as if it were sent over
        /// the network.
        /// </summary>
        public BaseRpcTarget Me;

        /// <summary>
        /// Send this RPC to everyone but the local machine, filtered to the current observer list.
        /// </summary>
        public BaseRpcTarget NotMe;

        /// <summary>
        /// Send this RPC to everone, filtered to the current observer list.
        /// Will execute locally.
        /// </summary>
        public BaseRpcTarget Everyone;

        /// <summary>
        /// Send this RPC to all clients, including the host, if a host exists.
        /// If the server is running in host mode, this is the same as <see cref="Everyone" />.
        /// If the server is running in dedicated server mode, this is the same as <see cref="NotServer" />.
        /// </summary>
        public BaseRpcTarget ClientsAndHost;

        /// <summary>
        /// Send to a specific single client ID.
        /// </summary>
        /// <param name="clientId"></param>
        /// <param name="use"><see cref="RpcTargetUse.Temp"/> will return a cached target, which should not be stored as it will
        /// be overwritten in future calls to Single(). Do not call Dispose() on Temp targets.<br /><br /><see cref="RpcTargetUse.Persistent"/> will
        /// return a new target, which can be stored, but should not be done frequently because it results in a GC allocation. You must call Dispose() on Persistent targets when you are done with them.</param>
        /// <returns></returns>
        public BaseRpcTarget Single(ulong clientId, RpcTargetUse use)
        {
            if (clientId == m_NetworkManager.LocalClientId)
            {
                return Me;
            }

            if (m_NetworkManager.IsServer || clientId == NetworkManager.ServerClientId)
            {
                if (use == RpcTargetUse.Persistent)
                {
                    return new DirectSendRpcTarget(clientId, m_NetworkManager);
                }
                m_CachedDirectSendTarget.SetClientId(clientId);
                return m_CachedDirectSendTarget;
            }

            if (use == RpcTargetUse.Persistent)
            {
                return new ProxyRpcTarget(clientId, m_NetworkManager);
            }
            m_CachedProxyRpcTarget.SetClientId(clientId);
            return m_CachedProxyRpcTarget;
        }

        /// <summary>
        /// Send to everyone EXCEPT a specific single client ID.
        /// </summary>
        /// <param name="excludedClientId"></param>
        /// <param name="use"><see cref="RpcTargetUse.Temp"/> will return a cached target, which should not be stored as it will
        /// be overwritten in future calls to Not() or Group(). Do not call Dispose() on Temp targets.<br /><br /><see cref="RpcTargetUse.Persistent"/> will
        /// return a new target, which can be stored, but should not be done frequently because it results in a GC allocation. You must call Dispose() on Persistent targets when you are done with them.</param>
        /// <returns></returns>
        public BaseRpcTarget Not(ulong excludedClientId, RpcTargetUse use)
        {
            IGroupRpcTarget target;
            if (m_NetworkManager.IsServer)
            {
                if (use == RpcTargetUse.Persistent)
                {
                    target = new RpcTargetGroup(m_NetworkManager);
                }
                else
                {
                    target = m_CachedTargetGroup;
                }
            }
            else
            {
                if (use == RpcTargetUse.Persistent)
                {
                    target = new ProxyRpcTargetGroup(m_NetworkManager);
                }
                else
                {
                    target = m_CachedProxyRpcTargetGroup;
                }
            }
            target.Clear();
            foreach (var clientId in m_NetworkManager.ConnectedClientsIds)
            {
                if (clientId != excludedClientId)
                {
                    target.Add(clientId);
                }
            }

            // If ServerIsHost, ConnectedClientIds already contains ServerClientId and this would duplicate it.
            if (!m_NetworkManager.ServerIsHost && excludedClientId != NetworkManager.ServerClientId)
            {
                target.Add(NetworkManager.ServerClientId);
            }

            return target.Target;
        }

        /// <summary>
        /// Sends to a group of client IDs.
        /// NativeArrays can be trivially constructed using Allocator.Temp, making this an efficient
        /// Group method if the group list is dynamically constructed.
        /// </summary>
        /// <param name="clientIds"></param>
        /// <param name="use"><see cref="RpcTargetUse.Temp"/> will return a cached target, which should not be stored as it will
        /// be overwritten in future calls to Not() or Group(). Do not call Dispose() on Temp targets.<br /><br /><see cref="RpcTargetUse.Persistent"/> will
        /// return a new target, which can be stored, but should not be done frequently because it results in a GC allocation. You must call Dispose() on Persistent targets when you are done with them.</param>
        /// <returns></returns>
        public BaseRpcTarget Group(NativeArray<ulong> clientIds, RpcTargetUse use)
        {
            IGroupRpcTarget target;
            if (m_NetworkManager.IsServer)
            {
                if (use == RpcTargetUse.Persistent)
                {
                    target = new RpcTargetGroup(m_NetworkManager);
                }
                else
                {
                    target = m_CachedTargetGroup;
                }
            }
            else
            {
                if (use == RpcTargetUse.Persistent)
                {
                    target = new ProxyRpcTargetGroup(m_NetworkManager);
                }
                else
                {
                    target = m_CachedProxyRpcTargetGroup;
                }
            }
            target.Clear();
            foreach (var clientId in clientIds)
            {
                target.Add(clientId);
            }

            return target.Target;
        }

        /// <summary>
        /// Sends to a group of client IDs.
        /// NativeList can be trivially constructed using Allocator.Temp, making this an efficient
        /// Group method if the group list is dynamically constructed.
        /// </summary>
        /// <param name="clientIds"></param>
        /// <param name="use"><see cref="RpcTargetUse.Temp"/> will return a cached target, which should not be stored as it will
        /// be overwritten in future calls to Not() or Group(). Do not call Dispose() on Temp targets.<br /><br /><see cref="RpcTargetUse.Persistent"/> will
        /// return a new target, which can be stored, but should not be done frequently because it results in a GC allocation. You must call Dispose() on Persistent targets when you are done with them.</param>
        /// <returns></returns>
        public BaseRpcTarget Group(NativeList<ulong> clientIds, RpcTargetUse use)
        {
            var asArray = clientIds.AsArray();
            return Group(asArray, use);
        }

        /// <summary>
        /// Sends to a group of client IDs.
        /// Constructing arrays requires garbage collected allocations. This override is only recommended
        /// if you either have no strict performance requirements, or have the group of client IDs cached so
        /// it is not created each time.
        /// </summary>
        /// <param name="clientIds"></param>
        /// <param name="use"><see cref="RpcTargetUse.Temp"/> will return a cached target, which should not be stored as it will
        /// be overwritten in future calls to Not() or Group(). Do not call Dispose() on Temp targets.<br /><br /><see cref="RpcTargetUse.Persistent"/> will
        /// return a new target, which can be stored, but should not be done frequently because it results in a GC allocation. You must call Dispose() on Persistent targets when you are done with them.</param>
        /// <returns></returns>
        public BaseRpcTarget Group(ulong[] clientIds, RpcTargetUse use)
        {
            return Group(new NativeArray<ulong>(clientIds, Allocator.Temp), use);
        }

        /// <summary>
        /// Sends to a group of client IDs.
        /// This accepts any IEnumerable type, such as List&lt;ulong&gt;, but cannot be called without
        /// a garbage collected allocation (even if the type itself is a struct type, due to boxing).
        /// This override is only recommended if you either have no strict performance requirements,
        /// or have the group of client IDs cached so it is not created each time.
        /// </summary>
        /// <param name="clientIds"></param>
        /// <param name="use"><see cref="RpcTargetUse.Temp"/> will return a cached target, which should not be stored as it will
        /// be overwritten in future calls to Not() or Group(). Do not call Dispose() on Temp targets.<br /><br /><see cref="RpcTargetUse.Persistent"/> will
        /// return a new target, which can be stored, but should not be done frequently because it results in a GC allocation. You must call Dispose() on Persistent targets when you are done with them.</param>
        /// <returns></returns>
        public BaseRpcTarget Group<T>(T clientIds, RpcTargetUse use) where T : IEnumerable<ulong>
        {
            IGroupRpcTarget target;
            if (m_NetworkManager.IsServer)
            {
                if (use == RpcTargetUse.Persistent)
                {
                    target = new RpcTargetGroup(m_NetworkManager);
                }
                else
                {
                    target = m_CachedTargetGroup;
                }
            }
            else
            {
                if (use == RpcTargetUse.Persistent)
                {
                    target = new ProxyRpcTargetGroup(m_NetworkManager);
                }
                else
                {
                    target = m_CachedProxyRpcTargetGroup;
                }
            }
            target.Clear();
            foreach (var clientId in clientIds)
            {
                target.Add(clientId);
            }

            return target.Target;
        }

        /// <summary>
        /// Sends to everyone EXCEPT a group of client IDs.
        /// NativeArrays can be trivially constructed using Allocator.Temp, making this an efficient
        /// Group method if the group list is dynamically constructed.
        /// </summary>
        /// <param name="excludedClientIds"></param>
        /// <param name="use"><see cref="RpcTargetUse.Temp"/> will return a cached target, which should not be stored as it will
        /// be overwritten in future calls to Not() or Group(). Do not call Dispose() on Temp targets.<br /><br /><see cref="RpcTargetUse.Persistent"/> will
        /// return a new target, which can be stored, but should not be done frequently because it results in a GC allocation. You must call Dispose() on Persistent targets when you are done with them.</param>
        /// <returns></returns>
        public BaseRpcTarget Not(NativeArray<ulong> excludedClientIds, RpcTargetUse use)
        {
            IGroupRpcTarget target;
            if (m_NetworkManager.IsServer)
            {
                if (use == RpcTargetUse.Persistent)
                {
                    target = new RpcTargetGroup(m_NetworkManager);
                }
                else
                {
                    target = m_CachedTargetGroup;
                }
            }
            else
            {
                if (use == RpcTargetUse.Persistent)
                {
                    target = new ProxyRpcTargetGroup(m_NetworkManager);
                }
                else
                {
                    target = m_CachedProxyRpcTargetGroup;
                }
            }
            target.Clear();

            using var asASet = new NativeHashSet<ulong>(excludedClientIds.Length, Allocator.Temp);
            foreach (var clientId in excludedClientIds)
            {
                asASet.Add(clientId);
            }

            foreach (var clientId in m_NetworkManager.ConnectedClientsIds)
            {
                if (!asASet.Contains(clientId))
                {
                    target.Add(clientId);
                }
            }

            // If ServerIsHost, ConnectedClientIds already contains ServerClientId and this would duplicate it.
            if (!m_NetworkManager.ServerIsHost && !asASet.Contains(NetworkManager.ServerClientId))
            {
                target.Add(NetworkManager.ServerClientId);
            }

            return target.Target;
        }

        /// <summary>
        /// Sends to everyone EXCEPT a group of client IDs.
        /// NativeList can be trivially constructed using Allocator.Temp, making this an efficient
        /// Group method if the group list is dynamically constructed.
        /// </summary>
        /// <param name="excludedClientIds"></param>
        /// <param name="use"><see cref="RpcTargetUse.Temp"/> will return a cached target, which should not be stored as it will
        /// be overwritten in future calls to Not() or Group(). Do not call Dispose() on Temp targets.<br /><br /><see cref="RpcTargetUse.Persistent"/> will
        /// return a new target, which can be stored, but should not be done frequently because it results in a GC allocation. You must call Dispose() on Persistent targets when you are done with them.</param>
        /// <returns></returns>
        public BaseRpcTarget Not(NativeList<ulong> excludedClientIds, RpcTargetUse use)
        {
            var asArray = excludedClientIds.AsArray();
            return Not(asArray, use);
        }

        /// <summary>
        /// Sends to everyone EXCEPT a group of client IDs.
        /// Constructing arrays requires garbage collected allocations. This override is only recommended
        /// if you either have no strict performance requirements, or have the group of client IDs cached so
        /// it is not created each time.
        /// </summary>
        /// <param name="excludedClientIds"></param>
        /// <param name="use"><see cref="RpcTargetUse.Temp"/> will return a cached target, which should not be stored as it will
        /// be overwritten in future calls to Not() or Group(). Do not call Dispose() on Temp targets.<br /><br /><see cref="RpcTargetUse.Persistent"/> will
        /// return a new target, which can be stored, but should not be done frequently because it results in a GC allocation. You must call Dispose() on Persistent targets when you are done with them.</param>
        /// <returns></returns>
        public BaseRpcTarget Not(ulong[] excludedClientIds, RpcTargetUse use)
        {
            return Not(new NativeArray<ulong>(excludedClientIds, Allocator.Temp), use);
        }

        /// <summary>
        /// Sends to everyone EXCEPT a group of client IDs.
        /// This accepts any IEnumerable type, such as List&lt;ulong&gt;, but cannot be called without
        /// a garbage collected allocation (even if the type itself is a struct type, due to boxing).
        /// This override is only recommended if you either have no strict performance requirements,
        /// or have the group of client IDs cached so it is not created each time.
        /// </summary>
        /// <param name="excludedClientIds"></param>
        /// <param name="use"><see cref="RpcTargetUse.Temp"/> will return a cached target, which should not be stored as it will
        /// be overwritten in future calls to Not() or Group(). Do not call Dispose() on Temp targets.<br /><br /><see cref="RpcTargetUse.Persistent"/> will
        /// return a new target, which can be stored, but should not be done frequently because it results in a GC allocation. You must call Dispose() on Persistent targets when you are done with them.</param>
        /// <returns></returns>
        public BaseRpcTarget Not<T>(T excludedClientIds, RpcTargetUse use) where T : IEnumerable<ulong>
        {
            IGroupRpcTarget target;
            if (m_NetworkManager.IsServer)
            {
                if (use == RpcTargetUse.Persistent)
                {
                    target = new RpcTargetGroup(m_NetworkManager);
                }
                else
                {
                    target = m_CachedTargetGroup;
                }
            }
            else
            {
                if (use == RpcTargetUse.Persistent)
                {
                    target = new ProxyRpcTargetGroup(m_NetworkManager);
                }
                else
                {
                    target = m_CachedProxyRpcTargetGroup;
                }
            }
            target.Clear();

            using var asASet = new NativeHashSet<ulong>(m_NetworkManager.ConnectedClientsIds.Count, Allocator.Temp);
            foreach (var clientId in excludedClientIds)
            {
                asASet.Add(clientId);
            }

            foreach (var clientId in m_NetworkManager.ConnectedClientsIds)
            {
                if (!asASet.Contains(clientId))
                {
                    target.Add(clientId);
                }
            }

            // If ServerIsHost, ConnectedClientIds already contains ServerClientId and this would duplicate it.
            if (!m_NetworkManager.ServerIsHost && !asASet.Contains(NetworkManager.ServerClientId))
            {
                target.Add(NetworkManager.ServerClientId);
            }

            return target.Target;
        }

        private ProxyRpcTargetGroup m_CachedProxyRpcTargetGroup;
        private RpcTargetGroup m_CachedTargetGroup;
        private DirectSendRpcTarget m_CachedDirectSendTarget;
        private ProxyRpcTarget m_CachedProxyRpcTarget;
    }
}
