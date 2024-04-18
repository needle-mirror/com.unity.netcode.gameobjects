namespace Unity.Netcode
{
    internal class NotMeRpcTarget : BaseRpcTarget
    {
        private IGroupRpcTarget m_GroupSendTarget;
        private ServerRpcTarget m_ServerRpcTarget;

        public override void Dispose()
        {
            m_ServerRpcTarget.Dispose();
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
                    if (clientId == behaviour.NetworkManager.LocalClientId)
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
                    if (clientId == behaviour.NetworkManager.LocalClientId)
                    {
                        continue;
                    }
                    if (clientId == NetworkManager.ServerClientId)
                    {
                        continue;
                    }
                    m_GroupSendTarget.Add(clientId);
                }
            }
            m_GroupSendTarget.Target.Send(behaviour, ref message, delivery, rpcParams);
            if (!behaviour.IsServer)
            {
                m_ServerRpcTarget.Send(behaviour, ref message, delivery, rpcParams);
            }
        }

        internal NotMeRpcTarget(NetworkManager manager) : base(manager)
        {
            m_ServerRpcTarget = new ServerRpcTarget(manager);
        }
    }
}
