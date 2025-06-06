using System;

namespace Unity.Netcode
{
    /// <summary>
    /// The base abstract RPC Target class used by all universal RPC targets.
    /// </summary>
    public abstract class BaseRpcTarget : IDisposable
    {
        /// <summary>
        /// The <see cref="NetworkManager"/> instance which can be used to handle sending and receiving the specific target(s)
        /// </summary>
        protected NetworkManager m_NetworkManager;
        private bool m_Locked;

        internal void Lock()
        {
            m_Locked = true;
        }

        internal void Unlock()
        {
            m_Locked = false;
        }

        internal BaseRpcTarget(NetworkManager manager)
        {
            m_NetworkManager = manager;
        }

        /// <summary>
        /// Verifies the target can be disposed based on its lock state.
        /// </summary>
        /// <exception cref="Exception">Thrown when attempting to dispose a locked temporary RPC target</exception>
        protected void CheckLockBeforeDispose()
        {
            if (m_Locked)
            {
                throw new Exception($"RPC targets obtained through {nameof(RpcTargetUse)}.{RpcTargetUse.Temp} may not be disposed.");
            }
        }

        /// <summary>
        /// Releases resources used by the RPC target.
        /// </summary>
        public abstract void Dispose();

        internal abstract void Send(NetworkBehaviour behaviour, ref RpcMessage message, NetworkDelivery delivery, RpcParams rpcParams);

        private protected void SendMessageToClient(NetworkBehaviour behaviour, ulong clientId, ref RpcMessage message, NetworkDelivery delivery)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR || UNITY_MP_TOOLS_NET_STATS_MONITOR_ENABLED_IN_RELEASE
            var size =
#endif
                behaviour.NetworkManager.MessageManager.SendMessage(ref message, delivery, clientId);

#if DEVELOPMENT_BUILD || UNITY_EDITOR || UNITY_MP_TOOLS_NET_STATS_MONITOR_ENABLED_IN_RELEASE
            if (NetworkBehaviour.__rpc_name_table[behaviour.GetType()].TryGetValue(message.Metadata.NetworkRpcMethodId, out var rpcMethodName))
            {
                behaviour.NetworkManager.NetworkMetrics.TrackRpcSent(
                    clientId,
                    behaviour.NetworkObject,
                    rpcMethodName,
                    behaviour.__getTypeName(),
                    size);
            }
#endif
        }
    }
}
