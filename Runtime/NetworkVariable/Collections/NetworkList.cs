using System;
using System.Collections.Generic;
using Unity.Collections;

namespace Unity.Netcode
{
    /// <summary>
    /// Event based NetworkVariable container for syncing Lists
    /// </summary>
    /// <typeparam name="T">The type for the list</typeparam>
    [GenerateSerializationForGenericParameter(0)]
    public class NetworkList<T> : NetworkVariableBase where T : unmanaged, IEquatable<T>
    {
        private NativeList<T> m_List = new NativeList<T>(64, Allocator.Persistent);
        private NativeList<NetworkListEvent<T>> m_DirtyEvents = new NativeList<NetworkListEvent<T>>(64, Allocator.Persistent);

        /// <summary>
        /// Delegate type for list changed event
        /// </summary>
        /// <param name="changeEvent">Struct containing information about the change event</param>
        public delegate void OnListChangedDelegate(NetworkListEvent<T> changeEvent);

        /// <summary>
        /// Creates A NetworkList/>
        /// </summary>
        public event OnListChangedDelegate OnListChanged;

        /// <summary>
        /// Constructor method for <see cref="NetworkList{T}"/>
        /// </summary>
        public NetworkList() { }

        /// <inheritdoc cref="NetworkList{T}"/>
        /// <param name="values">An optional collection of initial values to populate the list. If null, the list will start empty.</param>
        /// <param name="readPerm">The read permission level for the network list. Determines who can read the list (e.g., server-only or everyone). Default is defined by DefaultReadPerm</param>
        /// <param name="writePerm">The write permission level for the network list. Determines who can modify the list (e.g., server-only or specific clients). Default is defined by DefaultWritePerm.</param>
        public NetworkList(IEnumerable<T> values = default,
            NetworkVariableReadPermission readPerm = DefaultReadPerm,
            NetworkVariableWritePermission writePerm = DefaultWritePerm)
            : base(readPerm, writePerm)
        {
            // allow null IEnumerable<T> to mean "no values"
            if (values != null)
            {
                foreach (var value in values)
                {
                    m_List.Add(value);
                }
            }
        }

        /// <summary>
        /// Finalizer that ensures proper cleanup of network list resources
        /// </summary>
        ~NetworkList()
        {
            Dispose();
        }

        /// <inheritdoc cref="NetworkVariable{T}.ResetDirty"/>
        public override void ResetDirty()
        {
            base.ResetDirty();
            if (m_DirtyEvents.Length > 0)
            {
                m_DirtyEvents.Clear();
            }
        }

        /// <inheritdoc cref="NetworkVariable{T}.IsDirty"/>
        public override bool IsDirty()
        {
            // we call the base class to allow the SetDirty() mechanism to work
            return base.IsDirty() || m_DirtyEvents.Length > 0;
        }

        internal void MarkNetworkObjectDirty()
        {
            MarkNetworkBehaviourDirty();
        }

        /// <inheritdoc cref="NetworkVariable{T}.WriteDelta"/>
        public override void WriteDelta(FastBufferWriter writer)
        {

            if (base.IsDirty())
            {
                writer.WriteValueSafe((ushort)1);
                writer.WriteValueSafe(NetworkListEvent<T>.EventType.Full);
                WriteField(writer);

                return;
            }

            writer.WriteValueSafe((ushort)m_DirtyEvents.Length);
            for (int i = 0; i < m_DirtyEvents.Length; i++)
            {
                var element = m_DirtyEvents.ElementAt(i);
                writer.WriteValueSafe(element.Type);
                switch (element.Type)
                {
                    case NetworkListEvent<T>.EventType.Add:
                        {
                            NetworkVariableSerialization<T>.Serializer.Write(writer, ref element.Value);
                        }
                        break;
                    case NetworkListEvent<T>.EventType.Insert:
                        {
                            BytePacker.WriteValueBitPacked(writer, element.Index);
                            NetworkVariableSerialization<T>.Serializer.Write(writer, ref element.Value);
                        }
                        break;
                    case NetworkListEvent<T>.EventType.Remove:
                        {
                            NetworkVariableSerialization<T>.Serializer.Write(writer, ref element.Value);
                        }
                        break;
                    case NetworkListEvent<T>.EventType.RemoveAt:
                        {
                            BytePacker.WriteValueBitPacked(writer, element.Index);
                        }
                        break;
                    case NetworkListEvent<T>.EventType.Value:
                        {
                            BytePacker.WriteValueBitPacked(writer, element.Index);
                            NetworkVariableSerialization<T>.Serializer.Write(writer, ref element.Value);
                        }
                        break;
                    case NetworkListEvent<T>.EventType.Clear:
                        {
                            //Nothing has to be written
                        }
                        break;
                }
            }
        }

        /// <inheritdoc cref="NetworkVariable{T}.WriteField"/>
        public override void WriteField(FastBufferWriter writer)
        {
            writer.WriteValueSafe((ushort)m_List.Length);
            for (int i = 0; i < m_List.Length; i++)
            {
                NetworkVariableSerialization<T>.Serializer.Write(writer, ref m_List.ElementAt(i));
            }
        }

        /// <inheritdoc cref="NetworkVariable{T}.ReadField"/>
        public override void ReadField(FastBufferReader reader)
        {
            m_List.Clear();
            reader.ReadValueSafe(out ushort count);
            for (int i = 0; i < count; i++)
            {
                var value = new T();
                NetworkVariableSerialization<T>.Serializer.Read(reader, ref value);
                m_List.Add(value);
            }
        }

        /// <inheritdoc cref="NetworkVariable{T}.ReadDelta"/>
        public override void ReadDelta(FastBufferReader reader, bool keepDirtyDelta)
        {
            // This is only invoked by <see cref="NetworkVariableDeltaMessage"/> and the only time
            // keepDirtyDelta is set is when it is the server processing. To be able to handle previous
            // versions, we use IsServer to keep the dirty states received and the keepDirtyDelta to
            // actually mark this as dirty and add it to the list of <see cref="NetworkObject"/>s to
            // be updated. With the forwarding of deltas being handled by <see cref="NetworkVariableDeltaMessage"/>,
            // once all clients have been forwarded the dirty events, we clear them by invoking <see cref="PostDeltaRead"/>.
            var isServer = m_NetworkManager.IsServer;
            reader.ReadValueSafe(out ushort deltaCount);
            for (int i = 0; i < deltaCount; i++)
            {
                reader.ReadValueSafe(out NetworkListEvent<T>.EventType eventType);
                switch (eventType)
                {
                    case NetworkListEvent<T>.EventType.Add:
                        {
                            var value = new T();
                            NetworkVariableSerialization<T>.Serializer.Read(reader, ref value);
                            m_List.Add(value);

                            if (OnListChanged != null)
                            {
                                OnListChanged(new NetworkListEvent<T>
                                {
                                    Type = eventType,
                                    Index = m_List.Length - 1,
                                    Value = m_List[m_List.Length - 1]
                                });
                            }

                            if (isServer)
                            {
                                m_DirtyEvents.Add(new NetworkListEvent<T>()
                                {
                                    Type = eventType,
                                    Index = m_List.Length - 1,
                                    Value = m_List[m_List.Length - 1]
                                });
                                // Preserve the legacy way of handling this
                                if (keepDirtyDelta)
                                {
                                    MarkNetworkObjectDirty();
                                }
                            }
                        }
                        break;
                    case NetworkListEvent<T>.EventType.Insert:
                        {
                            ByteUnpacker.ReadValueBitPacked(reader, out int index);
                            var value = new T();
                            NetworkVariableSerialization<T>.Serializer.Read(reader, ref value);

                            if (index < m_List.Length)
                            {
                                m_List.InsertRangeWithBeginEnd(index, index + 1);
                                m_List[index] = value;
                            }
                            else
                            {
                                m_List.Add(value);
                            }

                            if (OnListChanged != null)
                            {
                                OnListChanged(new NetworkListEvent<T>
                                {
                                    Type = eventType,
                                    Index = index,
                                    Value = m_List[index]
                                });
                            }

                            if (isServer)
                            {
                                m_DirtyEvents.Add(new NetworkListEvent<T>()
                                {
                                    Type = eventType,
                                    Index = index,
                                    Value = m_List[index]
                                });
                                // Preserve the legacy way of handling this
                                if (keepDirtyDelta)
                                {
                                    MarkNetworkObjectDirty();
                                }
                            }
                        }
                        break;
                    case NetworkListEvent<T>.EventType.Remove:
                        {
                            var value = new T();
                            NetworkVariableSerialization<T>.Serializer.Read(reader, ref value);
                            int index = m_List.IndexOf(value);
                            if (index == -1)
                            {
                                break;
                            }

                            m_List.RemoveAt(index);

                            if (OnListChanged != null)
                            {
                                OnListChanged(new NetworkListEvent<T>
                                {
                                    Type = eventType,
                                    Index = index,
                                    Value = value
                                });
                            }

                            if (isServer)
                            {
                                m_DirtyEvents.Add(new NetworkListEvent<T>()
                                {
                                    Type = eventType,
                                    Index = index,
                                    Value = value
                                });
                                // Preserve the legacy way of handling this
                                if (keepDirtyDelta)
                                {
                                    MarkNetworkObjectDirty();
                                }
                            }
                        }
                        break;
                    case NetworkListEvent<T>.EventType.RemoveAt:
                        {
                            ByteUnpacker.ReadValueBitPacked(reader, out int index);
                            T value = m_List[index];
                            m_List.RemoveAt(index);

                            if (OnListChanged != null)
                            {
                                OnListChanged(new NetworkListEvent<T>
                                {
                                    Type = eventType,
                                    Index = index,
                                    Value = value
                                });
                            }

                            if (isServer)
                            {
                                m_DirtyEvents.Add(new NetworkListEvent<T>()
                                {
                                    Type = eventType,
                                    Index = index,
                                    Value = value
                                });
                                // Preserve the legacy way of handling this
                                if (keepDirtyDelta)
                                {
                                    MarkNetworkObjectDirty();
                                }
                            }
                        }
                        break;
                    case NetworkListEvent<T>.EventType.Value:
                        {
                            ByteUnpacker.ReadValueBitPacked(reader, out int index);
                            var value = new T();
                            NetworkVariableSerialization<T>.Serializer.Read(reader, ref value);
                            if (index >= m_List.Length)
                            {
                                throw new Exception("Shouldn't be here, index is higher than list length");
                            }

                            var previousValue = m_List[index];
                            m_List[index] = value;

                            if (OnListChanged != null)
                            {
                                OnListChanged(new NetworkListEvent<T>
                                {
                                    Type = eventType,
                                    Index = index,
                                    Value = value,
                                    PreviousValue = previousValue
                                });
                            }

                            if (isServer)
                            {
                                m_DirtyEvents.Add(new NetworkListEvent<T>()
                                {
                                    Type = eventType,
                                    Index = index,
                                    Value = value,
                                    PreviousValue = previousValue
                                });
                                // Preserve the legacy way of handling this
                                if (keepDirtyDelta)
                                {
                                    MarkNetworkObjectDirty();
                                }
                            }
                        }
                        break;
                    case NetworkListEvent<T>.EventType.Clear:
                        {
                            //Read nothing
                            m_List.Clear();

                            if (OnListChanged != null)
                            {
                                OnListChanged(new NetworkListEvent<T>
                                {
                                    Type = eventType,
                                });
                            }

                            if (isServer)
                            {
                                m_DirtyEvents.Add(new NetworkListEvent<T>()
                                {
                                    Type = eventType
                                });

                                // Preserve the legacy way of handling this
                                if (keepDirtyDelta)
                                {
                                    MarkNetworkObjectDirty();
                                }
                            }
                        }
                        break;
                    case NetworkListEvent<T>.EventType.Full:
                        {
                            ReadField(reader);
                            ResetDirty();
                        }
                        break;
                }
            }
        }

        /// <inheritdoc cref="NetworkVariable{T}.PostDeltaRead"/>
        /// <remarks>
        /// For NetworkList, we just need to reset dirty if a server has read deltas
        /// </remarks>
        internal override void PostDeltaRead()
        {
            if (m_NetworkManager.IsServer)
            {
                ResetDirty();
            }
        }

        /// <summary>
        /// Returns an enumerator that iterates through the <see cref="NetworkList{T}" />.
        /// </summary>
        /// <returns>An enumerator for the <see cref="NetworkList{T}"/>.</returns>
        public IEnumerator<T> GetEnumerator()
        {
            return m_List.GetEnumerator();
        }

        /// <summary>
        /// Adds an item to the end of the <see cref="NetworkList{T}"/>.
        /// </summary>
        /// <param name="item">The item to be added to the list.</param>
        /// <remarks>
        /// This method checks for write permissions before adding the item.
        /// </remarks>
        public void Add(T item)
        {
            // check write permissions
            if (!CanClientWrite(m_NetworkManager.LocalClientId))
            {
                LogWritePermissionError();
                return;
            }

            m_List.Add(item);

            var listEvent = new NetworkListEvent<T>()
            {
                Type = NetworkListEvent<T>.EventType.Add,
                Value = item,
                Index = m_List.Length - 1
            };

            HandleAddListEvent(listEvent);
        }

        /// <summary>
        /// Removes all items from the <see cref="NetworkList{T}"/>.
        /// </summary>
        /// <remarks>
        /// This method checks for write permissions before clearing the list.
        /// </remarks>
        public void Clear()
        {
            // check write permissions
            if (!CanClientWrite(m_NetworkManager.LocalClientId))
            {
                LogWritePermissionError();
                return;
            }

            m_List.Clear();

            var listEvent = new NetworkListEvent<T>()
            {
                Type = NetworkListEvent<T>.EventType.Clear
            };

            HandleAddListEvent(listEvent);
        }

        /// <summary>
        /// Determines whether the <see cref="NetworkList{T}"/> contains a specific value.
        /// </summary>
        /// <param name="item">The object to locate in the <see cref="NetworkList{T}"/>.</param>
        /// <returns><see langword="true" /> if the <see cref="item"/> is found in the <see cref="NetworkList{T}"/>; otherwise, <see langword="false" />.</returns>
        public bool Contains(T item)
        {
            int index = m_List.IndexOf(item);
            return index != -1;
        }

        /// <summary>
        /// Removes the first occurrence of a specific object from the NetworkList.
        /// </summary>
        /// <remarks>
        /// This method checks for write permissions before removing the item.
        /// </remarks>
        /// <param name="item">The object to remove from the list.</param>
        /// <returns><see langword="true" /> if the item was successfully removed from the list; otherwise, <see langword="false" />.</returns>
        public bool Remove(T item)
        {
            // check write permissions
            if (!CanClientWrite(m_NetworkManager.LocalClientId))
            {
                LogWritePermissionError();
                return false;
            }

            int index = m_List.IndexOf(item);
            if (index == -1)
            {
                return false;
            }

            m_List.RemoveAt(index);
            var listEvent = new NetworkListEvent<T>()
            {
                Type = NetworkListEvent<T>.EventType.Remove,
                Value = item
            };

            HandleAddListEvent(listEvent);
            return true;
        }

        /// <summary>
        /// Gets the number of elements contained in the <see cref="NetworkList{T}"/>.
        /// </summary>
        public int Count => m_List.Length;

        /// <summary>
        /// Determines the index of a specific <see cref="item"/> in the <see cref="NetworkList{T}"/>.
        /// </summary>
        /// <param name="item">The object to remove from the list.</param>
        /// <returns>The index of the <see cref="item"/> if found in the list; otherwise, -1.</returns>
        public int IndexOf(T item)
        {
            return m_List.IndexOf(item);
        }

        /// <summary>
        /// Inserts <see cref="item"/> to the <see cref="NetworkList{T}"/> at the specified <see cref="index"/>.
        /// </summary>
        /// <remarks>
        /// This method checks for write permissions before inserting the item.
        /// </remarks>
        /// <param name="index">The index at which the item should be inserted.</param>
        /// <param name="item">The item to insert.</param>
        public void Insert(int index, T item)
        {
            // check write permissions
            if (!CanClientWrite(m_NetworkManager.LocalClientId))
            {
                LogWritePermissionError();
                return;
            }

            if (index < m_List.Length)
            {
                m_List.InsertRangeWithBeginEnd(index, index + 1);
                m_List[index] = item;
            }
            else
            {
                m_List.Add(item);
            }

            var listEvent = new NetworkListEvent<T>()
            {
                Type = NetworkListEvent<T>.EventType.Insert,
                Index = index,
                Value = item
            };

            HandleAddListEvent(listEvent);
        }

        /// <summary>
        /// Removes the <see cref="NetworkList{T}"/> item at the specified <see cref="index"/>.
        /// </summary>
        /// <remarks>
        /// This method checks for write permissions before removing the item.
        /// </remarks>
        /// <param name="index">The index of the element to remove.</param>
        public void RemoveAt(int index)
        {
            // check write permissions
            if (!CanClientWrite(m_NetworkManager.LocalClientId))
            {
                throw new InvalidOperationException("Client is not allowed to write to this NetworkList");
            }

            var value = m_List[index];
            m_List.RemoveAt(index);

            var listEvent = new NetworkListEvent<T>()
            {
                Type = NetworkListEvent<T>.EventType.RemoveAt,
                Index = index,
                Value = value
            };

            HandleAddListEvent(listEvent);
        }

        /// <summary>
        /// Gets or sets the element at the specified index in the <see cref="NetworkList{T}"/>.
        /// </summary>
        /// <remarks>
        /// This method checks for write permissions before setting the value.
        /// </remarks>
        /// <param name="index">The zero-based index of the element to get or set.</param>
        /// <value>The element at the specified index.</value>
        public T this[int index]
        {
            get => m_List[index];
            set
            {
                // check write permissions
                if (!CanClientWrite(m_NetworkManager.LocalClientId))
                {
                    LogWritePermissionError();
                    return;
                }

                var previousValue = m_List[index];
                m_List[index] = value;

                var listEvent = new NetworkListEvent<T>()
                {
                    Type = NetworkListEvent<T>.EventType.Value,
                    Index = index,
                    Value = value,
                    PreviousValue = previousValue
                };

                HandleAddListEvent(listEvent);
            }
        }

        private void HandleAddListEvent(NetworkListEvent<T> listEvent)
        {
            m_DirtyEvents.Add(listEvent);
            MarkNetworkObjectDirty();
            OnListChanged?.Invoke(listEvent);
        }

        /// <summary>
        /// This method should not be used. It is left over from a previous interface
        /// </summary>
        public int LastModifiedTick
        {
            get
            {
                // todo: implement proper network tick for NetworkList
                return NetworkTickSystem.NoTick;
            }
        }

        /// <summary>
        /// Overridden <see cref="IDisposable"/> implementation.
        /// CAUTION: If you derive from this class and override the <see cref="Dispose"/> method,
        /// you **must** always invoke the base.Dispose() method!
        /// </summary>
        public override void Dispose()
        {
            if (m_List.IsCreated)
            {
                m_List.Dispose();
            }

            if (m_DirtyEvents.IsCreated)
            {
                m_DirtyEvents.Dispose();
            }

            base.Dispose();
        }
    }

    /// <summary>
    /// Struct containing event information about changes to a NetworkList.
    /// </summary>
    /// <typeparam name="T">The type for the list that the event is about</typeparam>
    public struct NetworkListEvent<T>
    {
        /// <summary>
        /// Enum representing the different operations available for triggering an event.
        /// </summary>
        public enum EventType : byte
        {
            /// <summary>
            /// Add
            /// </summary>
            Add,

            /// <summary>
            /// Insert
            /// </summary>
            Insert,

            /// <summary>
            /// Remove
            /// </summary>
            Remove,

            /// <summary>
            /// Remove at
            /// </summary>
            RemoveAt,

            /// <summary>
            /// Value changed
            /// </summary>
            Value,

            /// <summary>
            /// Clear
            /// </summary>
            Clear,

            /// <summary>
            /// Full list refresh
            /// </summary>
            Full
        }

        /// <summary>
        /// Enum representing the operation made to the list.
        /// </summary>
        public EventType Type;

        /// <summary>
        /// The value changed, added or removed if available.
        /// </summary>
        public T Value;

        /// <summary>
        /// The previous value when "Value" has changed, if available.
        /// </summary>
        public T PreviousValue;

        /// <summary>
        /// the index changed, added or removed if available
        /// </summary>
        public int Index;
    }
}
