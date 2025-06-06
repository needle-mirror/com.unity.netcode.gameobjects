#if COM_UNITY_MODULES_PHYSICS || COM_UNITY_MODULES_PHYSICS2D
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Unity.Netcode.Components
{
    /// <summary>
    /// NetworkRigidbodyBase is a unified <see cref="Rigidbody"/> and <see cref="Rigidbody2D"/> integration that helps to synchronize physics motion, collision, and interpolation
    /// when used with a <see cref="NetworkTransform"/>.
    /// </summary>
    /// <remarks>
    /// For a customizable netcode Rigidbody, create your own component from this class and use <see cref="Initialize(RigidbodyTypes, NetworkTransform, Rigidbody2D, Rigidbody)"/>
    /// during instantiation (i.e. invoked from within the Awake method). You can re-initialize after having initialized but only when the <see cref="NetworkObject"/> is not spawned.
    /// </remarks>
    public abstract class NetworkRigidbodyBase : NetworkBehaviour
    {
#if UNITY_EDITOR
        [HideInInspector]
        [SerializeField]
        internal bool NetworkRigidbodyBaseExpanded;
#endif

        /// <summary>
        /// When enabled, the associated <see cref="NetworkTransform"/> will use the Rigidbody/Rigidbody2D to apply and synchronize changes in position, rotation, and
        /// allows for the use of Rigidbody interpolation/extrapolation.
        /// </summary>
        /// <remarks>
        /// If <see cref="NetworkTransform.Interpolate"/> is enabled, non-authoritative instances can only use Rigidbody interpolation. If a network prefab is set to
        /// extrapolation and <see cref="NetworkTransform.Interpolate"/> is enabled, then non-authoritative instances will automatically be adjusted to use Rigidbody
        /// interpolation while the authoritative instance will still use extrapolation.
        /// </remarks>
        [Tooltip("When enabled and a NetworkTransform component is attached, the NetworkTransform will use the rigid body for motion and detecting changes in state.")]
        public bool UseRigidBodyForMotion;

        /// <summary>
        /// When enabled (default), automatically set the Kinematic state of the Rigidbody based on ownership.
        /// When disabled, Kinematic state needs to be set by external script(s).
        /// </summary>
        public bool AutoUpdateKinematicState = true;

        /// <summary>
        /// Primarily applies to the <see cref="AutoUpdateKinematicState"/> property when disabled but you still want
        /// the Rigidbody to be automatically set to Kinematic when despawned.
        /// </summary>
        public bool AutoSetKinematicOnDespawn = true;

#if COM_UNITY_MODULES_PHYSICS2D
        // Determines if this is a Rigidbody or Rigidbody2D implementation
        private bool m_IsRigidbody2D => RigidbodyType == RigidbodyTypes.Rigidbody2D;
#else
        private bool m_IsRigidbody2D = false;
#endif


        // Used to cache the authority state of this Rigidbody during the last frame
        private bool m_IsAuthority;

#if COM_UNITY_MODULES_PHYSICS
        protected internal Rigidbody m_InternalRigidbody { get; private set; }
#endif

#if COM_UNITY_MODULES_PHYSICS2D
        protected internal Rigidbody2D m_InternalRigidbody2D { get; private set; }
#endif

        internal NetworkTransform NetworkTransform;
        private float m_TickFrequency;
        private float m_TickRate;

        private enum InterpolationTypes
        {
            None,
            Interpolate,
            Extrapolate
        }
        private InterpolationTypes m_OriginalInterpolation;

        /// <summary>
        /// Used to define the type of Rigidbody implemented.
        /// <see cref=""/>
        /// </summary>
        public enum RigidbodyTypes
        {
#if COM_UNITY_MODULES_PHYSICS
            Rigidbody,
#endif
#if COM_UNITY_MODULES_PHYSICS2D
            Rigidbody2D,
#endif
        }

        public RigidbodyTypes RigidbodyType { get; private set; }
#if COM_UNITY_MODULES_PHYSICS2D && !COM_UNITY_MODULES_PHYSICS
        /// <summary>
        /// Initializes the networked Rigidbody based on the <see cref="RigidbodyTypes"/>
        /// passed in as a parameter.
        /// </summary>
        /// <remarks>
        /// Cannot be initialized while the associated <see cref="NetworkObject"/> is spawned.
        /// </remarks>
        /// <param name="rigidbodyType">type of rigid body being initialized</param>
        /// <param name="networkTransform">the <see cref="NetworkTransform"/> that is associated with this rigid body</param>
        /// <param name="rigidbody2D">(optional) The <see cref="Rigidbody2D"/> to be used</param>
        protected void Initialize(RigidbodyTypes rigidbodyType, NetworkTransform networkTransform = null, Rigidbody2D rigidbody2D = null)
        {
            // Don't initialize if already spawned
            if (IsSpawned)
            {
                Debug.LogError($"[{name}] Attempting to initialize while spawned is not allowed.");
                return;
            }
            RigidbodyType = rigidbodyType;
            m_InternalRigidbody2D = rigidbody2D;
            NetworkTransform = networkTransform;

            if (m_IsRigidbody2D && m_InternalRigidbody2D == null)
            {
                m_InternalRigidbody2D = GetComponent<Rigidbody2D>();
            }

            SetOriginalInterpolation();

            if (NetworkTransform == null)
            {
                NetworkTransform = GetComponent<NetworkTransform>();
            }

            if (NetworkTransform != null)
            {
                NetworkTransform.RegisterRigidbody(this);
            }
            else
            {
                throw new System.Exception($"[Missing {nameof(NetworkTransform)}] No {nameof(NetworkTransform)} is assigned or can be found during initialization!");
            }

            if (AutoUpdateKinematicState)
            {
                SetIsKinematic(true);
            }
        }
#endif

#if !COM_UNITY_MODULES_PHYSICS2D && COM_UNITY_MODULES_PHYSICS
        /// <summary>
        /// Initializes the networked Rigidbody based on the <see cref="RigidbodyTypes"/>
        /// passed in as a parameter.
        /// </summary>
        /// <remarks>
        /// Cannot be initialized while the associated <see cref="NetworkObject"/> is spawned.
        /// </remarks>
        /// <param name="rigidbodyType">type of rigid body being initialized</param>
        /// <param name="networkTransform">the <see cref="NetworkTransform"/> that is associated with this rigid body</param>
        /// <param name="rigidbody">(optional) The <see cref="Rigidbody"/> to be used</param>
        protected void Initialize(RigidbodyTypes rigidbodyType, NetworkTransform networkTransform = null, Rigidbody rigidbody = null)
        {
            // Don't initialize if already spawned
            if (IsSpawned)
            {
                Debug.LogError($"[{name}] Attempting to initialize while spawned is not allowed.");
                return;
            }
            RigidbodyType = rigidbodyType;
            m_InternalRigidbody = rigidbody;
            NetworkTransform = networkTransform;

            if (m_InternalRigidbody == null)
            {
                m_InternalRigidbody = GetComponent<Rigidbody>();
            }

            SetOriginalInterpolation();

            if (NetworkTransform == null)
            {
                NetworkTransform = GetComponent<NetworkTransform>();
            }

            if (NetworkTransform != null)
            {
                NetworkTransform.RegisterRigidbody(this);
            }
            else
            {
                throw new System.Exception($"[Missing {nameof(NetworkTransform)}] No {nameof(NetworkTransform)} is assigned or can be found during initialization!");
            }

            if (AutoUpdateKinematicState)
            {
                SetIsKinematic(true);
            }
        }
#endif

#if COM_UNITY_MODULES_PHYSICS && COM_UNITY_MODULES_PHYSICS2D
        /// <summary>
        /// Initializes the networked Rigidbody based on the <see cref="RigidbodyTypes"/>
        /// passed in as a parameter.
        /// </summary>
        /// <remarks>
        /// Cannot be initialized while the associated <see cref="NetworkObject"/> is spawned.
        /// </remarks>
        /// <param name="rigidbodyType">type of rigid body being initialized</param>
        /// <param name="networkTransform">the <see cref="NetworkTransform"/> that is associated with this rigid body</param>
        /// <param name="rigidbody2D">(optional) The <see cref="Rigidbody2D"/> to be used</param>
        /// <param name="rigidbody">(optional) The <see cref="Rigidbody"/> to be used</param>
        protected void Initialize(RigidbodyTypes rigidbodyType, NetworkTransform networkTransform = null, Rigidbody2D rigidbody2D = null, Rigidbody rigidbody = null)
        {
            // Don't initialize if already spawned
            if (IsSpawned)
            {
                Debug.LogError($"[{name}] Attempting to initialize while spawned is not allowed.");
                return;
            }
            RigidbodyType = rigidbodyType;

            m_InternalRigidbody2D = rigidbody2D;
            m_InternalRigidbody = rigidbody;
            NetworkTransform = networkTransform;

            if (m_IsRigidbody2D && m_InternalRigidbody2D == null)
            {
                m_InternalRigidbody2D = GetComponent<Rigidbody2D>();

            }
            else if (m_InternalRigidbody == null)
            {
                m_InternalRigidbody = GetComponent<Rigidbody>();
            }

            SetOriginalInterpolation();

            if (NetworkTransform == null)
            {
                NetworkTransform = GetComponent<NetworkTransform>();
            }

            if (NetworkTransform != null)
            {
                NetworkTransform.RegisterRigidbody(this);
            }
            else
            {
                throw new System.Exception($"[Missing {nameof(NetworkTransform)}] No {nameof(NetworkTransform)} is assigned or can be found during initialization!");
            }

            if (AutoUpdateKinematicState)
            {
                SetIsKinematic(true);
            }
        }
#endif
        internal Vector3 GetAdjustedPositionThreshold()
        {
            // Since the threshold is a measurement of unity world space units per tick, we will allow for the maximum threshold
            // to be no greater than the threshold measured in unity world space units per second
            var thresholdMax = NetworkTransform.PositionThreshold * m_TickRate;
            // Get the velocity in unity world space units per tick
            var perTickVelocity = GetLinearVelocity() * m_TickFrequency;
            // Since a rigid body can have "micro-motion" when allowed to come to rest (based on friction etc), we will allow for
            // no less than 1/10th the threshold value.
            var minThreshold = NetworkTransform.PositionThreshold * 0.1f;

            // Finally, we adjust the threshold based on the body's current velocity
            perTickVelocity.x = Mathf.Clamp(Mathf.Abs(perTickVelocity.x), minThreshold, thresholdMax);
            perTickVelocity.y = Mathf.Clamp(Mathf.Abs(perTickVelocity.y), minThreshold, thresholdMax);
            // 2D Rigidbody only moves on x & y axis
            if (!m_IsRigidbody2D)
            {
                perTickVelocity.z = Mathf.Clamp(Mathf.Abs(perTickVelocity.z), minThreshold, thresholdMax);
            }

            return perTickVelocity;
        }

        internal Vector3 GetAdjustedRotationThreshold()
        {
            // Since the rotation threshold is a measurement pf degrees per tick, we get the maximum threshold
            // by calculating the threshold in degrees per second.
            var thresholdMax = NetworkTransform.RotAngleThreshold * m_TickRate;
            // Angular velocity is expressed in radians per second where as the rotation being checked is in degrees.
            // Convert the angular velocity to degrees per second and then convert that to degrees per tick.
            var rotationPerTick = (GetAngularVelocity() * Mathf.Rad2Deg) * m_TickFrequency;
            var minThreshold = NetworkTransform.RotAngleThreshold * m_TickFrequency;

            // 2D Rigidbody only rotates around Z axis
            if (!m_IsRigidbody2D)
            {
                rotationPerTick.x = Mathf.Clamp(Mathf.Abs(rotationPerTick.x), minThreshold, thresholdMax);
                rotationPerTick.y = Mathf.Clamp(Mathf.Abs(rotationPerTick.y), minThreshold, thresholdMax);
            }
            rotationPerTick.z = Mathf.Clamp(Mathf.Abs(rotationPerTick.z), minThreshold, thresholdMax);

            return rotationPerTick;
        }

        /// <summary>
        /// Sets the linear velocity of the Rigidbody.
        /// </summary>
        /// <remarks>
        /// For <see cref="Rigidbody2D"/>, only the x and y components of the <see cref="Vector3"/> are applied.
        /// </remarks>
        public void SetLinearVelocity(Vector3 linearVelocity)
        {
#if COM_UNITY_MODULES_PHYSICS && COM_UNITY_MODULES_PHYSICS2D
            if (m_IsRigidbody2D)
            {
#if COM_UNITY_MODULES_PHYSICS2D_LINEAR
                m_InternalRigidbody2D.linearVelocity = linearVelocity;
#else
                m_InternalRigidbody2D.velocity = linearVelocity;
#endif
            }
            else
            {
                m_InternalRigidbody.linearVelocity = linearVelocity;
            }
#endif
#if COM_UNITY_MODULES_PHYSICS && !COM_UNITY_MODULES_PHYSICS2D
            m_InternalRigidbody.linearVelocity = linearVelocity;
#endif
#if !COM_UNITY_MODULES_PHYSICS && COM_UNITY_MODULES_PHYSICS2D
#if COM_UNITY_MODULES_PHYSICS2D_LINEAR
            m_InternalRigidbody2D.linearVelocity = linearVelocity;
#else
            m_InternalRigidbody2D.velocity = linearVelocity;
#endif
#endif
        }

        /// <summary>
        /// Gets the linear velocity of the Rigidbody.
        /// </summary>
        /// <remarks>
        /// For <see cref="Rigidbody2D"/>, the <see cref="Vector3"/> velocity returned is only applied to the x and y components.
        /// </remarks>
        /// <returns><see cref="Vector3"/> as the linear velocity</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3 GetLinearVelocity()
        {
#if COM_UNITY_MODULES_PHYSICS && COM_UNITY_MODULES_PHYSICS2D
#if COM_UNITY_MODULES_PHYSICS2D
            if (m_IsRigidbody2D)
            {
#if COM_UNITY_MODULES_PHYSICS2D_LINEAR
                return m_InternalRigidbody2D.linearVelocity;
#else
                return m_InternalRigidbody2D.velocity;
#endif
            }
            else
#endif
            {
                return m_InternalRigidbody.linearVelocity;
            }
#endif
#if COM_UNITY_MODULES_PHYSICS && !COM_UNITY_MODULES_PHYSICS2D
            return m_InternalRigidbody.linearVelocity;
#endif
#if !COM_UNITY_MODULES_PHYSICS && COM_UNITY_MODULES_PHYSICS2D
#if COM_UNITY_MODULES_PHYSICS2D_LINEAR
            return m_InternalRigidbody2D.linearVelocity;
#else
            return m_InternalRigidbody2D.velocity;
#endif
#endif
        }

        /// <summary>
        /// Sets the angular velocity for the Rigidbody.
        /// </summary>
        /// <remarks>
        /// For <see cref="Rigidbody2D"/>, the z component of <param name="angularVelocity"/> is only used to set the angular velocity.
        /// A quick way to pass in a 2D angular velocity component is: <see cref="Vector3.forward"/> * angularVelocity (where angularVelocity is a float)
        /// </remarks>
        /// <param name="angularVelocity">the angular velocity to apply to the body</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetAngularVelocity(Vector3 angularVelocity)
        {
#if COM_UNITY_MODULES_PHYSICS && COM_UNITY_MODULES_PHYSICS2D
            if (m_IsRigidbody2D)
            {
                m_InternalRigidbody2D.angularVelocity = angularVelocity.z;
            }
            else
            {
                m_InternalRigidbody.angularVelocity = angularVelocity;
            }
#endif
#if COM_UNITY_MODULES_PHYSICS && !COM_UNITY_MODULES_PHYSICS2D
            m_InternalRigidbody.angularVelocity = angularVelocity;
#endif
#if !COM_UNITY_MODULES_PHYSICS && COM_UNITY_MODULES_PHYSICS2D
            m_InternalRigidbody2D.angularVelocity = angularVelocity.z;
#endif
        }

        /// <summary>
        /// Gets the angular velocity for the Rigidbody.
        /// </summary>
        /// <remarks>
        /// For <see cref="Rigidbody2D"/>, the z component of the <see cref="Vector3"/> returned is the angular velocity of the object.
        /// </remarks>
        /// <returns>angular velocity as a <see cref="Vector3"/></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3 GetAngularVelocity()
        {
#if COM_UNITY_MODULES_PHYSICS && COM_UNITY_MODULES_PHYSICS2D
            if (m_IsRigidbody2D)
            {
                return Vector3.forward * m_InternalRigidbody2D.angularVelocity;
            }
            else
            {
                return m_InternalRigidbody.angularVelocity;
            }
#endif
#if COM_UNITY_MODULES_PHYSICS && !COM_UNITY_MODULES_PHYSICS2D
           return m_InternalRigidbody.angularVelocity;
#endif
#if !COM_UNITY_MODULES_PHYSICS && COM_UNITY_MODULES_PHYSICS2D
            return Vector3.forward * m_InternalRigidbody2D.angularVelocity;
#endif
        }

        /// <summary>
        /// Gets the position of the Rigidbody
        /// </summary>
        /// <returns><see cref="Vector3"/></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3 GetPosition()
        {
#if COM_UNITY_MODULES_PHYSICS && COM_UNITY_MODULES_PHYSICS2D
            if (m_IsRigidbody2D)
            {
                return m_InternalRigidbody2D.position;
            }
            else
            {
                return m_InternalRigidbody.position;
            }
#endif
#if COM_UNITY_MODULES_PHYSICS && !COM_UNITY_MODULES_PHYSICS2D
            return m_InternalRigidbody.position;
#endif
#if !COM_UNITY_MODULES_PHYSICS && COM_UNITY_MODULES_PHYSICS2D
            return m_InternalRigidbody2D.position;
#endif
        }

#if COM_UNITY_MODULES_PHYSICS2D
        private Quaternion Rotation2D()
        {
            var quaternion = Quaternion.identity;
            var angles = quaternion.eulerAngles;
            angles.z = m_InternalRigidbody2D.rotation;
            quaternion.eulerAngles = angles;
            return quaternion;
        }
#endif

#if COM_UNITY_MODULES_PHYSICS
        private Quaternion Rotation()
        {
            return m_InternalRigidbody.rotation;
        }
#endif

        /// <summary>
        /// Gets the rotation of the Rigidbody
        /// </summary>
        /// <returns><see cref="Quaternion"/></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Quaternion GetRotation()
        {
#if COM_UNITY_MODULES_PHYSICS && COM_UNITY_MODULES_PHYSICS2D
            return m_IsRigidbody2D ? Rotation2D() : Rotation();
#endif
#if COM_UNITY_MODULES_PHYSICS && !COM_UNITY_MODULES_PHYSICS2D
            return Rotation();
#endif
#if !COM_UNITY_MODULES_PHYSICS && COM_UNITY_MODULES_PHYSICS2D
            return Rotation2D();
#endif
        }

        /// <summary>
        /// Moves the rigid body
        /// </summary>
        /// <param name="position">The <see cref="Vector3"/> position to move towards</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void MovePosition(Vector3 position)
        {
#if COM_UNITY_MODULES_PHYSICS && COM_UNITY_MODULES_PHYSICS2D
            if (m_IsRigidbody2D)
            {
                m_InternalRigidbody2D.MovePosition(position);
            }
            else
            {
                m_InternalRigidbody.MovePosition(position);
            }
#endif
#if COM_UNITY_MODULES_PHYSICS && !COM_UNITY_MODULES_PHYSICS2D
            m_InternalRigidbody.MovePosition(position);
#endif
#if !COM_UNITY_MODULES_PHYSICS && COM_UNITY_MODULES_PHYSICS2D
            m_InternalRigidbody2D.MovePosition(position);
#endif
        }

        /// <summary>
        /// Directly applies a position (like teleporting)
        /// </summary>
        /// <param name="position"><see cref="Vector3"/> position to apply to the Rigidbody</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetPosition(Vector3 position)
        {
#if COM_UNITY_MODULES_PHYSICS && COM_UNITY_MODULES_PHYSICS2D
            if (m_IsRigidbody2D)
            {
                m_InternalRigidbody2D.position = position;
            }
            else
            {
                m_InternalRigidbody.position = position;
            }
#endif
#if COM_UNITY_MODULES_PHYSICS && !COM_UNITY_MODULES_PHYSICS2D
            m_InternalRigidbody.position = position;
#endif
#if !COM_UNITY_MODULES_PHYSICS && COM_UNITY_MODULES_PHYSICS2D
            m_InternalRigidbody2D.position = position;
#endif
        }

        /// <summary>
        /// Applies the rotation and position of the <see cref="GameObject"/>'s <see cref="Transform"/>
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ApplyCurrentTransform()
        {
#if COM_UNITY_MODULES_PHYSICS && COM_UNITY_MODULES_PHYSICS2D
            if (m_IsRigidbody2D)
            {
                m_InternalRigidbody2D.position = transform.position;
                m_InternalRigidbody2D.rotation = transform.eulerAngles.z;
            }
            else
            {
                m_InternalRigidbody.position = transform.position;
                m_InternalRigidbody.rotation = transform.rotation;
            }
#endif
#if COM_UNITY_MODULES_PHYSICS && !COM_UNITY_MODULES_PHYSICS2D
            m_InternalRigidbody.position = transform.position;
            m_InternalRigidbody.rotation = transform.rotation;
#endif
#if !COM_UNITY_MODULES_PHYSICS && COM_UNITY_MODULES_PHYSICS2D
            m_InternalRigidbody2D.position = transform.position;
            m_InternalRigidbody2D.rotation = transform.eulerAngles.z;
#endif
        }

        // Used for Rigidbody only (see info on normalized below)
        private Vector4 m_QuaternionCheck = Vector4.zero;

#if COM_UNITY_MODULES_PHYSICS2D
        private void InternalMoveRotation2D(Quaternion rotation)
        {
            var quaternion = Quaternion.identity;
            var angles = quaternion.eulerAngles;
            angles.z = m_InternalRigidbody2D.rotation;
            quaternion.eulerAngles = angles;
            m_InternalRigidbody2D.MoveRotation(quaternion);
        }
#endif

#if COM_UNITY_MODULES_PHYSICS
        private void InternalMoveRotation(Quaternion rotation)
        {
            // Evidently we need to check to make sure the quaternion is a perfect
            // magnitude of 1.0f when applying the rotation to a rigid body.
            m_QuaternionCheck.x = rotation.x;
            m_QuaternionCheck.y = rotation.y;
            m_QuaternionCheck.z = rotation.z;
            m_QuaternionCheck.w = rotation.w;
            // If the magnitude is greater than 1.0f (even by a very small fractional value), then normalize the quaternion
            if (m_QuaternionCheck.magnitude != 1.0f)
            {
                rotation.Normalize();
            }
            m_InternalRigidbody.MoveRotation(rotation);
        }
#endif

        /// <summary>
        /// Rotatates the Rigidbody towards a specified rotation
        /// </summary>
        /// <param name="rotation">The rotation expressed as a <see cref="Quaternion"/></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void MoveRotation(Quaternion rotation)
        {
#if COM_UNITY_MODULES_PHYSICS && COM_UNITY_MODULES_PHYSICS2D
            if (m_IsRigidbody2D)
            {
                InternalMoveRotation2D(rotation);
            }
            else
            {
                InternalMoveRotation(rotation);
            }
#endif
#if COM_UNITY_MODULES_PHYSICS && !COM_UNITY_MODULES_PHYSICS2D
            InternalMoveRotation(rotation);
#endif
#if !COM_UNITY_MODULES_PHYSICS && COM_UNITY_MODULES_PHYSICS2D
            InternalMoveRotation2D(rotation);
#endif
        }

        /// <summary>
        /// Applies a rotation to the Rigidbody
        /// </summary>
        /// <param name="rotation">The rotation to apply expressed as a <see cref="Quaternion"/></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetRotation(Quaternion rotation)
        {
#if COM_UNITY_MODULES_PHYSICS && COM_UNITY_MODULES_PHYSICS2D
            if (m_IsRigidbody2D)
            {
                m_InternalRigidbody2D.rotation = rotation.eulerAngles.z;
            }
            else
            {
                m_InternalRigidbody.rotation = rotation;
            }
#endif
#if COM_UNITY_MODULES_PHYSICS && !COM_UNITY_MODULES_PHYSICS2D
            m_InternalRigidbody.rotation = rotation;
#endif
#if !COM_UNITY_MODULES_PHYSICS && COM_UNITY_MODULES_PHYSICS2D
            m_InternalRigidbody2D.rotation = rotation.eulerAngles.z;
#endif
        }

#if COM_UNITY_MODULES_PHYSICS2D
        private void SetOriginalInterpolation2D()
        {
            switch (m_InternalRigidbody2D.interpolation)
            {
                case RigidbodyInterpolation2D.None:
                    {
                        m_OriginalInterpolation = InterpolationTypes.None;
                        break;
                    }
                case RigidbodyInterpolation2D.Interpolate:
                    {
                        m_OriginalInterpolation = InterpolationTypes.Interpolate;
                        break;
                    }
                case RigidbodyInterpolation2D.Extrapolate:
                    {
                        m_OriginalInterpolation = InterpolationTypes.Extrapolate;
                        break;
                    }
            }
        }
#endif

#if COM_UNITY_MODULES_PHYSICS
        private void SetOriginalInterpolation3D()
        {
            switch (m_InternalRigidbody.interpolation)
            {
                case RigidbodyInterpolation.None:
                    {
                        m_OriginalInterpolation = InterpolationTypes.None;
                        break;
                    }
                case RigidbodyInterpolation.Interpolate:
                    {
                        m_OriginalInterpolation = InterpolationTypes.Interpolate;
                        break;
                    }
                case RigidbodyInterpolation.Extrapolate:
                    {
                        m_OriginalInterpolation = InterpolationTypes.Extrapolate;
                        break;
                    }
            }
        }
#endif

        /// <summary>
        /// Sets the original interpolation of the Rigidbody while taking the Rigidbody type into consideration
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetOriginalInterpolation()
        {
#if COM_UNITY_MODULES_PHYSICS && COM_UNITY_MODULES_PHYSICS2D
            if (m_IsRigidbody2D)
            {
                SetOriginalInterpolation2D();
            }
            else
            {
                SetOriginalInterpolation3D();
            }
#endif
#if COM_UNITY_MODULES_PHYSICS && !COM_UNITY_MODULES_PHYSICS2D
            SetOriginalInterpolation3D();
#endif
#if !COM_UNITY_MODULES_PHYSICS && COM_UNITY_MODULES_PHYSICS2D
            SetOriginalInterpolation2D();
#endif
        }


#if COM_UNITY_MODULES_PHYSICS2D
        private void WakeIfSleeping2D()
        {
            if (m_InternalRigidbody2D.IsSleeping())
            {
                m_InternalRigidbody2D.WakeUp();
            }
        }
#endif

#if COM_UNITY_MODULES_PHYSICS
        private void WakeIfSleeping3D()
        {
            if (m_InternalRigidbody.IsSleeping())
            {
                m_InternalRigidbody.WakeUp();
            }
        }
#endif

        /// <summary>
        /// Wakes the Rigidbody if it is sleeping
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WakeIfSleeping()
        {
#if COM_UNITY_MODULES_PHYSICS && COM_UNITY_MODULES_PHYSICS2D
            if (m_IsRigidbody2D)
            {
                WakeIfSleeping2D();
            }
            else
            {
                WakeIfSleeping3D();
            }
#endif
#if COM_UNITY_MODULES_PHYSICS && !COM_UNITY_MODULES_PHYSICS2D
            WakeIfSleeping3D();
#endif
#if !COM_UNITY_MODULES_PHYSICS && COM_UNITY_MODULES_PHYSICS2D
            WakeIfSleeping2D();
#endif
        }

        /// <summary>
        /// Puts the Rigidbody to sleep
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SleepRigidbody()
        {
#if COM_UNITY_MODULES_PHYSICS && COM_UNITY_MODULES_PHYSICS2D
            if (m_IsRigidbody2D)
            {
                m_InternalRigidbody2D.Sleep();
            }
            else
            {
                m_InternalRigidbody.Sleep();
            }
#endif
#if COM_UNITY_MODULES_PHYSICS && !COM_UNITY_MODULES_PHYSICS2D
            m_InternalRigidbody.Sleep();
#endif
#if !COM_UNITY_MODULES_PHYSICS && COM_UNITY_MODULES_PHYSICS2D
            m_InternalRigidbody2D.Sleep();
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsKinematic()
        {
#if COM_UNITY_MODULES_PHYSICS && COM_UNITY_MODULES_PHYSICS2D
            if (m_IsRigidbody2D)
            {
                return m_InternalRigidbody2D.bodyType == RigidbodyType2D.Kinematic;
            }
            else
            {
                return m_InternalRigidbody.isKinematic;
            }
#endif
#if COM_UNITY_MODULES_PHYSICS && !COM_UNITY_MODULES_PHYSICS2D
            return m_InternalRigidbody.isKinematic;
#endif
#if !COM_UNITY_MODULES_PHYSICS && COM_UNITY_MODULES_PHYSICS2D
            return m_InternalRigidbody2D.bodyType == RigidbodyType2D.Kinematic;
#endif
        }

        /// <summary>
        /// Sets the kinematic state of the Rigidbody and handles updating the Rigidbody's
        /// interpolation setting based on the Kinematic state.
        /// </summary>
        /// <remarks>
        /// When using the Rigidbody for <see cref="NetworkTransform"/> motion, this automatically
        /// adjusts from extrapolation to interpolation if:
        /// - The Rigidbody was originally set to extrapolation
        /// - The NetworkTransform is set to interpolate
        /// When the two above conditions are true:
        /// - When switching from non-kinematic to kinematic this will automatically
        /// switch the Rigidbody from extrapolation to interpolate.
        /// - When switching from kinematic to non-kinematic this will automatically
        /// switch the Rigidbody from interpolation back to extrapolation.
        /// </remarks>
        /// <param name="isKinematic"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetIsKinematic(bool isKinematic)
        {
#if COM_UNITY_MODULES_PHYSICS && COM_UNITY_MODULES_PHYSICS2D
            if (m_IsRigidbody2D)
            {
                m_InternalRigidbody2D.bodyType = isKinematic ? RigidbodyType2D.Kinematic : RigidbodyType2D.Dynamic;
            }
            else
            {
                m_InternalRigidbody.isKinematic = isKinematic;
            }
#endif
#if COM_UNITY_MODULES_PHYSICS && !COM_UNITY_MODULES_PHYSICS2D
            m_InternalRigidbody.isKinematic = isKinematic;
#endif
#if !COM_UNITY_MODULES_PHYSICS && COM_UNITY_MODULES_PHYSICS2D
            m_InternalRigidbody2D.bodyType = isKinematic ? RigidbodyType2D.Kinematic : RigidbodyType2D.Dynamic;
#endif
            PostSetIsKinematic();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void PostSetIsKinematic()
        {
            // If we are not spawned, then exit early
            if (!IsSpawned)
            {
                return;
            }
            if (UseRigidBodyForMotion)
            {
                // Only if the NetworkTransform is set to interpolate do we need to check for extrapolation
                if (NetworkTransform.Interpolate && m_OriginalInterpolation == InterpolationTypes.Extrapolate)
                {
                    if (IsKinematic())
                    {
                        // If not already set to interpolate then set the Rigidbody to interpolate
                        if (m_InternalRigidbody.interpolation == RigidbodyInterpolation.Extrapolate)
                        {
                            // Sleep until the next fixed update when switching from extrapolation to interpolation
                            SleepRigidbody();
                            SetInterpolation(InterpolationTypes.Interpolate);
                        }
                    }
                    else
                    {
                        // Switch it back to the original interpolation if non-kinematic (doesn't require sleep).
                        SetInterpolation(m_OriginalInterpolation);
                    }
                }
            }
            else
            {
                SetInterpolation(m_IsAuthority ? m_OriginalInterpolation : (NetworkTransform.Interpolate ? InterpolationTypes.None : m_OriginalInterpolation));
            }
        }

#if COM_UNITY_MODULES_PHYSICS2D
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetInterpolation2D(InterpolationTypes interpolationType)
        {
            switch (interpolationType)
            {
                case InterpolationTypes.None:
                    {
                        m_InternalRigidbody2D.interpolation = RigidbodyInterpolation2D.None;
                        break;
                    }
                case InterpolationTypes.Interpolate:
                    {
                        m_InternalRigidbody2D.interpolation = RigidbodyInterpolation2D.Interpolate;
                        break;
                    }
                case InterpolationTypes.Extrapolate:
                    {
                        m_InternalRigidbody2D.interpolation = RigidbodyInterpolation2D.Extrapolate;
                        break;
                    }
            }
        }
#endif

#if COM_UNITY_MODULES_PHYSICS
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetInterpolation3D(InterpolationTypes interpolationType)
        {
            switch (interpolationType)
            {
                case InterpolationTypes.None:
                    {
                        m_InternalRigidbody.interpolation = RigidbodyInterpolation.None;
                        break;
                    }
                case InterpolationTypes.Interpolate:
                    {
                        m_InternalRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
                        break;
                    }
                case InterpolationTypes.Extrapolate:
                    {
                        m_InternalRigidbody.interpolation = RigidbodyInterpolation.Extrapolate;
                        break;
                    }
            }
        }
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetInterpolation(InterpolationTypes interpolationType)
        {
#if COM_UNITY_MODULES_PHYSICS && COM_UNITY_MODULES_PHYSICS2D
            if (m_IsRigidbody2D)
            {
                SetInterpolation2D(interpolationType);
            }
            else
            {
                SetInterpolation3D(interpolationType);
            }
#endif
#if COM_UNITY_MODULES_PHYSICS && !COM_UNITY_MODULES_PHYSICS2D
            SetInterpolation3D(interpolationType);
#endif
#if !COM_UNITY_MODULES_PHYSICS && COM_UNITY_MODULES_PHYSICS2D
            SetInterpolation2D(interpolationType);
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ResetInterpolation()
        {
            SetInterpolation(m_OriginalInterpolation);
        }

        protected override void OnOwnershipChanged(ulong previous, ulong current)
        {
            UpdateOwnershipAuthority();
            base.OnOwnershipChanged(previous, current);
        }

        /// <summary>
        /// Sets the authority based on whether it is server or owner authoritative
        /// </summary>
        /// <remarks>
        /// Distributed authority sessions will always be owner authoritative.
        /// </remarks>
        internal void UpdateOwnershipAuthority()
        {
            if (NetworkManager.DistributedAuthorityMode)
            {
                // When in distributed authority mode, always use HasAuthority
                m_IsAuthority = HasAuthority;
            }
            else
            {
                if (NetworkTransform.IsServerAuthoritative())
                {
                    m_IsAuthority = NetworkManager.IsServer;
                }
                else
                {
                    m_IsAuthority = IsOwner;
                }
            }

            if (AutoUpdateKinematicState)
            {
                SetIsKinematic(!m_IsAuthority);
            }
        }

        /// <inheritdoc />
        public override void OnNetworkSpawn()
        {
            m_TickFrequency = 1.0f / NetworkManager.NetworkConfig.TickRate;
            m_TickRate = NetworkManager.NetworkConfig.TickRate;
            UpdateOwnershipAuthority();
        }

        /// <inheritdoc />
        public override void OnNetworkDespawn()
        {
            if (UseRigidBodyForMotion && HasAuthority)
            {
                DetachFromFixedJoint();
                NetworkRigidbodyConnections.Clear();
            }

            // If we are automatically handling the kinematic state...
            if (AutoUpdateKinematicState || AutoSetKinematicOnDespawn)
            {
                // Turn off physics for the rigid body until spawned, otherwise
                // non-owners can run fixed updates before the first full
                // NetworkTransform update and physics will be applied (i.e. gravity, etc)
                SetIsKinematic(true);
            }
            SetInterpolation(m_OriginalInterpolation);
        }

        // TODO: Possibly provide a NetworkJoint that allows for more options than fixed.
        // Rigidbodies do not have the concept of "local space", and as such using a fixed joint will hold the object
        // in place relative to the parent so jitter/stutter does not occur.
        // Alternately, users can affix the fixed joint to a child GameObject (without a rigid body) of the parent NetworkObject
        // and then add a NetworkTransform to that in order to get the parented child NetworkObject to move around in "local space"

        internal System.Collections.Generic.List<NetworkRigidbodyBase> NetworkRigidbodyConnections = new System.Collections.Generic.List<NetworkRigidbodyBase>();
        internal NetworkRigidbodyBase ParentBody;

        private bool m_FixedJoint2DUsingGravity;
        private bool m_OriginalGravitySetting;
        private float m_OriginalGravityScale;

        /// <summary>
        /// When using a custom <see cref="NetworkRigidbodyBase"/>, this virtual method is invoked when the
        /// <see cref="FixedJoint"/> is created in the event any additional adjustments are needed.
        /// </summary>
        protected virtual void OnFixedJointCreated()
        {

        }

        /// <summary>
        /// When using a custom <see cref="NetworkRigidbodyBase"/>, this virtual method is invoked when the
        /// <see cref="FixedJoint2D"/> is created in the event any additional adjustments are needed.
        /// </summary>
        protected virtual void OnFixedJoint2DCreated()
        {

        }

#if COM_UNITY_MODULES_PHYSICS2D
        public FixedJoint2D FixedJoint2D { get; private set; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ApplyFixedJoint2D(NetworkRigidbodyBase bodyToConnect, Vector3 position, float connectedMassScale = 0.0f, float massScale = 1.0f, bool useGravity = false, bool zeroVelocity = true)
        {
            transform.position = position;
            m_InternalRigidbody2D.position = position;
            m_OriginalGravitySetting = bodyToConnect.m_InternalRigidbody.useGravity;
            m_FixedJoint2DUsingGravity = useGravity;

            if (!useGravity)
            {
                m_OriginalGravityScale = m_InternalRigidbody2D.gravityScale;
                m_InternalRigidbody2D.gravityScale = 0.0f;
            }

            if (zeroVelocity)
            {
#if COM_UNITY_MODULES_PHYSICS2D_LINEAR
                m_InternalRigidbody2D.linearVelocity = Vector2.zero;
#else
                m_InternalRigidbody2D.velocity = Vector2.zero;
#endif
                m_InternalRigidbody2D.angularVelocity = 0.0f;
            }

            FixedJoint2D = gameObject.AddComponent<FixedJoint2D>();
            FixedJoint2D.connectedBody = bodyToConnect.m_InternalRigidbody2D;
            OnFixedJoint2DCreated();
        }
#endif

#if COM_UNITY_MODULES_PHYSICS
        public FixedJoint FixedJoint { get; private set; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ApplyFixedJoint(NetworkRigidbodyBase bodyToConnectTo, Vector3 position, float connectedMassScale = 0.0f, float massScale = 1.0f, bool useGravity = false, bool zeroVelocity = true)
        {
            transform.position = position;
            m_InternalRigidbody.position = position;
            if (zeroVelocity)
            {
                m_InternalRigidbody.linearVelocity = Vector3.zero;
                m_InternalRigidbody.angularVelocity = Vector3.zero;
            }
            m_OriginalGravitySetting = m_InternalRigidbody.useGravity;
            m_InternalRigidbody.useGravity = useGravity;
            FixedJoint = gameObject.AddComponent<FixedJoint>();
            FixedJoint.connectedBody = bodyToConnectTo.m_InternalRigidbody;
            FixedJoint.connectedMassScale = connectedMassScale;
            FixedJoint.massScale = massScale;
            OnFixedJointCreated();
        }
#endif

        /// <summary>
        /// Authority Only:
        /// When invoked and not already attached to a fixed joint, this will connect two rigid bodies with <see cref="UseRigidBodyForMotion"/> enabled.
        /// Invoke this method on the rigid body you wish to attach to another (i.e. weapon to player, sticky bomb to player/object, etc).
        /// <seealso cref="FixedJoint"/>
        /// <seealso cref="FixedJoint2D"/>
        /// </summary>
        /// <remarks>
        /// Parenting relative:
        /// - This instance can be viewed as the child.
        /// - The <param name="objectToConnectTo"/> can be viewed as the parent.
        /// <br/>
        /// This is the recommended way, as opposed to parenting, to attached/detatch two rigid bodies to one another when <see cref="UseRigidBodyForMotion"/> is enabled.
        /// For more details on using <see cref="UnityEngine.FixedJoint"/> and <see cref="UnityEngine.FixedJoint2D"/>.
        /// <br/>
        /// This provides a simple joint solution between two rigid bodies and serves as an example. You can add different joint types by creating a customized/derived
        /// version of <see cref="NetworkRigidbodyBase"/>.
        /// </remarks>
        /// <param name="objectToConnectTo">The target object to attach to.</param>
        /// <param name="positionOfConnection">The position of the connection (i.e. where you want the object to be affixed).</param>
        /// <param name="connectedMassScale">The target object's mass scale relative to this object being attached.</param>
        /// <param name="massScale">This object's mass scale relative to the target object's.</param>
        /// <param name="useGravity">Determines if this object will have gravity applied to it along with the object you are connecting this one to (the default is to not use gravity for this object)</param>
        /// <param name="zeroVelocity">When true (the default), both linear and angular velocities of this object are set to zero.</param>
        /// <param name="teleportObject">When true (the default), this object will teleport itself to the position of connection.</param>
        /// <returns>true (success) false (failed)</returns>
        public bool AttachToFixedJoint(NetworkRigidbodyBase objectToConnectTo, Vector3 positionOfConnection, float connectedMassScale = 0.0f, float massScale = 1.0f, bool useGravity = false, bool zeroVelocity = true, bool teleportObject = true)
        {
            if (!UseRigidBodyForMotion)
            {
                Debug.LogError($"[{GetType().Name}] {name} does not have {nameof(UseRigidBodyForMotion)} set! Either enable {nameof(UseRigidBodyForMotion)} on this component or do not use a {nameof(FixedJoint)} when parenting under a {nameof(NetworkObject)}.");
                return false;
            }

            if (IsKinematic())
            {
                Debug.LogError($"[{GetType().Name}] {name} is currently kinematic! You cannot use a {nameof(FixedJoint)} with Kinematic bodies!");
                return false;
            }

            if (objectToConnectTo != null)
            {
#if COM_UNITY_MODULES_PHYSICS && COM_UNITY_MODULES_PHYSICS2D
                if (m_IsRigidbody2D)
                {
                    ApplyFixedJoint2D(objectToConnectTo, positionOfConnection, connectedMassScale, massScale, useGravity, zeroVelocity);
                }
                else
                {
                    ApplyFixedJoint(objectToConnectTo, positionOfConnection, connectedMassScale, massScale, useGravity, zeroVelocity);
                }
#endif
#if COM_UNITY_MODULES_PHYSICS && !COM_UNITY_MODULES_PHYSICS2D
                ApplyFixedJoint(objectToConnectTo, positionOfConnection, connectedMassScale, massScale, useGravity, zeroVelocity);
#endif
#if !COM_UNITY_MODULES_PHYSICS && COM_UNITY_MODULES_PHYSICS2D
                ApplyFixedJoint2D(objectToConnectTo, positionOfConnection, connectedMassScale, massScale, useGravity, zeroVelocity);
#endif
                ParentBody = objectToConnectTo;
                ParentBody.NetworkRigidbodyConnections.Add(this);
                if (teleportObject)
                {
                    NetworkTransform.SetState(teleportDisabled: false);
                }
                return true;
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RemoveFromParentBody()
        {
            ParentBody.NetworkRigidbodyConnections.Remove(this);
            ParentBody = null;
        }

#if COM_UNITY_MODULES_PHYSICS2D
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DetatchFromFixedJoint2D()
        {
            if (FixedJoint2D == null)
            {
                return;
            }
            if (!m_FixedJoint2DUsingGravity)
            {
                FixedJoint2D.connectedBody.gravityScale = m_OriginalGravityScale;
            }
            FixedJoint2D.connectedBody = null;
            Destroy(FixedJoint2D);
            FixedJoint2D = null;
            ResetInterpolation();
            RemoveFromParentBody();
        }
#endif
#if COM_UNITY_MODULES_PHYSICS
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DetatchFromFixedJoint3D()
        {
            if (FixedJoint == null)
            {
                return;
            }
            FixedJoint.connectedBody = null;
            m_InternalRigidbody.useGravity = m_OriginalGravitySetting;
            Destroy(FixedJoint);
            FixedJoint = null;
            ResetInterpolation();
            RemoveFromParentBody();
        }
#endif

        /// <summary>
        /// Authority Only:
        /// When invoked and already connected to an object via <see cref="FixedJoint"/> or <see cref="FixedJoint2D"/> (depending upon the type of rigid body),
        /// this will detach from the fixed joint and destroy the fixed joint component.
        /// </summary>
        /// <remarks>
        /// This is the recommended way, as opposed to parenting, to attached/detatch two rigid bodies to one another when <see cref="UseRigidBodyForMotion"/> is enabled.
        /// </remarks>
        public void DetachFromFixedJoint()
        {
            if (!HasAuthority)
            {
                Debug.LogError($"[{name}] Only authority can invoke {nameof(DetachFromFixedJoint)}!");
            }
            if (UseRigidBodyForMotion)
            {
#if COM_UNITY_MODULES_PHYSICS && COM_UNITY_MODULES_PHYSICS2D
                if (m_IsRigidbody2D)
                {
                    DetatchFromFixedJoint2D();
                }
                else
                {
                    DetatchFromFixedJoint3D();
                }
#endif
#if COM_UNITY_MODULES_PHYSICS && !COM_UNITY_MODULES_PHYSICS2D
                DetatchFromFixedJoint3D();
#endif
#if !COM_UNITY_MODULES_PHYSICS && COM_UNITY_MODULES_PHYSICS2D
                DetatchFromFixedJoint2D();
#endif
            }
        }
    }
}
// COM_UNITY_MODULES_PHYSICS
#endif
