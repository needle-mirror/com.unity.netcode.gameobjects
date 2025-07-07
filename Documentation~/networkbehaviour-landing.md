# NetworkBehaviour

Understand how to use NetworkBehaviour components in your project.

| **Topic**                       | **Description**                  |
| :------------------------------ | :------------------------------- |
| **[NetworkBehaviour](basics/networkbehaviour.md)** | [`NetworkBehaviour`](https://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@latest?subfolder=/api/Unity.Netcode.NetworkBehaviour.html) is an abstract class that derives from [`MonoBehaviour`](https://docs.unity3d.com/ScriptReference/MonoBehaviour.html) and is primarily used to create unique netcode or game logic. To replicate any netcode-aware properties or send and receive RPCs, a [GameObject](https://docs.unity3d.com/Manual/GameObjects.html) must have a [NetworkObject](basics/networkobject.md) component and at least one `NetworkBehaviour` component. |
| **[Synchronize](basics/networkbehaviour-synchronize.md)** | You can use NetworkBehaviours to synchronize settings before, during, and after spawning NetworkObjects. |