namespace Unity.Netcode
{
    internal class NotServerRpcTarget : BaseRpcTarget
    {
        protected IGroupRpcTarget m_GroupSendTarget;
        protected LocalSendRpcTarget m_LocalSendRpcTarget;

        public override void Dispose()
        {
            m_LocalSendRpcTarget.Dispose();
            if (m_GroupSendTarget != null)
            {
                m_GroupSendTarget.Target.Dispose();
                m_GroupSendTarget = null;
            }
        }

        internal override void Send(NetworkBehaviour behaviour, ref RpcMessage message, NetworkDelivery delivery, RpcParams rpcParams)
        {
            if (m_GroupSendTarget == null)
            {
                if (behaviour.IsServer)
                {
                    m_GroupSendTarget = new RpcTargetGroup(m_NetworkManager);
                }
                else
                {
                    m_GroupSendTarget = new ProxyRpcTargetGroup(m_NetworkManager);
                }
            }
            m_GroupSendTarget.Clear();

            if (behaviour.IsServer)
            {
                foreach (var clientId in behaviour.NetworkObject.Observers)
                {
                    if (clientId == NetworkManager.ServerClientId)
                    {
                        continue;
                    }

                    m_GroupSendTarget.Add(clientId);
                }
            }
            else
            {
                foreach (var clientId in m_NetworkManager.ConnectedClientsIds)
                {
                    if (clientId == NetworkManager.ServerClientId)
                    {
                        continue;
                    }

                    // If we are in distributed authority mode and connected to the service, then we exclude the owner/authority from the list
                    if (m_NetworkManager.DistributedAuthorityMode && m_NetworkManager.CMBServiceConnection && clientId == behaviour.OwnerClientId)
                    {
                        continue;
                    }

                    if (clientId == m_NetworkManager.LocalClientId)
                    {
                        m_LocalSendRpcTarget.Send(behaviour, ref message, delivery, rpcParams);
                        continue;
                    }

                    m_GroupSendTarget.Add(clientId);
                }
            }

            m_GroupSendTarget.Target.Send(behaviour, ref message, delivery, rpcParams);
        }

        internal NotServerRpcTarget(NetworkManager manager) : base(manager)
        {
            m_LocalSendRpcTarget = new LocalSendRpcTarget(manager);
        }
    }
}
