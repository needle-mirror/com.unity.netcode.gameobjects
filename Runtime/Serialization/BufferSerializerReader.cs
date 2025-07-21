using System;
using Unity.Collections;
using UnityEngine;

namespace Unity.Netcode
{
    internal struct BufferSerializerReader : IReaderWriter
    {
        private FastBufferReader m_Reader;

        public BufferSerializerReader(FastBufferReader reader)
        {
            m_Reader = reader;
        }

        public bool IsReader => true;
        public bool IsWriter => false;

        public FastBufferReader GetFastBufferReader()
        {
            return m_Reader;
        }

        public FastBufferWriter GetFastBufferWriter()
        {
            throw new InvalidOperationException("Cannot retrieve a FastBufferWriter from a serializer where IsWriter = false");
        }

        public void SerializeValue(ref string s, bool oneByteChars = false) => m_Reader.ReadValueSafe(out s, oneByteChars);
        public void SerializeValue(ref byte value) => m_Reader.ReadByteSafe(out value);
        public void SerializeValue<T>(ref T value, FastBufferWriter.ForPrimitives unused = default) where T : unmanaged, IComparable, IConvertible, IComparable<T>, IEquatable<T> => m_Reader.ReadValueSafe(out value);
        public void SerializeValue<T>(ref T[] value, FastBufferWriter.ForPrimitives unused = default) where T : unmanaged, IComparable, IConvertible, IComparable<T>, IEquatable<T> => m_Reader.ReadValueSafe(out value);

        public void SerializeValue<T>(ref T value, FastBufferWriter.ForEnums unused = default) where T : unmanaged, Enum => m_Reader.ReadValueSafe(out value);
        public void SerializeValue<T>(ref T[] value, FastBufferWriter.ForEnums unused = default) where T : unmanaged, Enum => m_Reader.ReadValueSafe(out value);

        public void SerializeValue<T>(ref T value, FastBufferWriter.ForStructs unused = default) where T : unmanaged, INetworkSerializeByMemcpy => m_Reader.ReadValueSafe(out value);
        public void SerializeValue<T>(ref T[] value, FastBufferWriter.ForStructs unused = default) where T : unmanaged, INetworkSerializeByMemcpy => m_Reader.ReadValueSafe(out value);
        public void SerializeValue<T>(ref NativeArray<T> value, Allocator allocator, FastBufferWriter.ForGeneric unused = default) where T : unmanaged => m_Reader.ReadValueSafe(out value, allocator);
#if UNITY_NETCODE_NATIVE_COLLECTION_SUPPORT
        public void SerializeValue<T>(ref NativeList<T> value, FastBufferWriter.ForGeneric unused = default) where T : unmanaged => m_Reader.ReadValueSafeInPlace(ref value);
#endif

        public void SerializeValue<T>(ref T value, FastBufferWriter.ForNetworkSerializable unused = default) where T : INetworkSerializable, new() => m_Reader.ReadNetworkSerializableInPlace(ref value);
        public void SerializeValue<T>(ref T[] value, FastBufferWriter.ForNetworkSerializable unused = default) where T : INetworkSerializable, new() => m_Reader.ReadValue(out value);

        public void SerializeValue<T>(ref T value, FastBufferWriter.ForFixedStrings unused = default)
            where T : unmanaged, INativeList<byte>, IUTF8Bytes => m_Reader.ReadValueSafe(out value);

        public void SerializeValue<T>(ref NativeArray<T> value, Allocator allocator) where T : unmanaged, INativeList<byte>, IUTF8Bytes => m_Reader.ReadValueSafe(out value, allocator);

#if UNITY_NETCODE_NATIVE_COLLECTION_SUPPORT
        public void SerializeValue<T>(ref NativeList<T> value) where T : unmanaged, INativeList<byte>, IUTF8Bytes => m_Reader.ReadValueSafeInPlace(ref value);
#endif

        public void SerializeValue(ref Vector2 value) => m_Reader.ReadValueSafe(out value);
        public void SerializeValue(ref Vector2[] value) => m_Reader.ReadValueSafe(out value);

        public void SerializeValue(ref Vector3 value) => m_Reader.ReadValueSafe(out value);
        public void SerializeValue(ref Vector3[] value) => m_Reader.ReadValueSafe(out value);

        public void SerializeValue(ref Vector2Int value) => m_Reader.ReadValueSafe(out value);
        public void SerializeValue(ref Vector2Int[] value) => m_Reader.ReadValueSafe(out value);

        public void SerializeValue(ref Vector3Int value) => m_Reader.ReadValueSafe(out value);
        public void SerializeValue(ref Vector3Int[] value) => m_Reader.ReadValueSafe(out value);

        public void SerializeValue(ref Vector4 value) => m_Reader.ReadValueSafe(out value);
        public void SerializeValue(ref Vector4[] value) => m_Reader.ReadValueSafe(out value);

        public void SerializeValue(ref Quaternion value) => m_Reader.ReadValueSafe(out value);
        public void SerializeValue(ref Quaternion[] value) => m_Reader.ReadValueSafe(out value);

        public void SerializeValue(ref Pose value) => m_Reader.ReadValueSafe(out value);
        public void SerializeValue(ref Pose[] value) => m_Reader.ReadValueSafe(out value);

        public void SerializeValue(ref Color value) => m_Reader.ReadValueSafe(out value);
        public void SerializeValue(ref Color[] value) => m_Reader.ReadValueSafe(out value);

        public void SerializeValue(ref Color32 value) => m_Reader.ReadValueSafe(out value);
        public void SerializeValue(ref Color32[] value) => m_Reader.ReadValueSafe(out value);

        public void SerializeValue(ref Ray value) => m_Reader.ReadValueSafe(out value);
        public void SerializeValue(ref Ray[] value) => m_Reader.ReadValueSafe(out value);

        public void SerializeValue(ref Ray2D value) => m_Reader.ReadValueSafe(out value);
        public void SerializeValue(ref Ray2D[] value) => m_Reader.ReadValueSafe(out value);

        public void SerializeNetworkSerializable<T>(ref T value) where T : INetworkSerializable, new() => m_Reader.ReadNetworkSerializable(out value);

        public bool PreCheck(int amount)
        {
            return m_Reader.TryBeginRead(amount);
        }

        public void SerializeValuePreChecked(ref string s, bool oneByteChars = false) => m_Reader.ReadValue(out s, oneByteChars);
        public void SerializeValuePreChecked(ref byte value) => m_Reader.ReadByte(out value);
        public void SerializeValuePreChecked<T>(ref T value, FastBufferWriter.ForPrimitives unused = default) where T : unmanaged, IComparable, IConvertible, IComparable<T>, IEquatable<T> => m_Reader.ReadValue(out value);
        public void SerializeValuePreChecked<T>(ref T[] value, FastBufferWriter.ForPrimitives unused = default) where T : unmanaged, IComparable, IConvertible, IComparable<T>, IEquatable<T> => m_Reader.ReadValue(out value);

        public void SerializeValuePreChecked<T>(ref T value, FastBufferWriter.ForEnums unused = default) where T : unmanaged, Enum => m_Reader.ReadValue(out value);
        public void SerializeValuePreChecked<T>(ref T[] value, FastBufferWriter.ForEnums unused = default) where T : unmanaged, Enum => m_Reader.ReadValue(out value);

        public void SerializeValuePreChecked<T>(ref T value, FastBufferWriter.ForStructs unused = default) where T : unmanaged, INetworkSerializeByMemcpy => m_Reader.ReadValue(out value);
        public void SerializeValuePreChecked<T>(ref T[] value, FastBufferWriter.ForStructs unused = default) where T : unmanaged, INetworkSerializeByMemcpy => m_Reader.ReadValue(out value);
        public void SerializeValuePreChecked<T>(ref NativeArray<T> value, Allocator allocator, FastBufferWriter.ForGeneric unused = default) where T : unmanaged => m_Reader.ReadValue(out value, allocator);

#if UNITY_NETCODE_NATIVE_COLLECTION_SUPPORT
        public void SerializeValuePreChecked<T>(ref NativeList<T> value, FastBufferWriter.ForGeneric unused = default) where T : unmanaged => m_Reader.ReadValueInPlace(ref value);
#endif

        public void SerializeValuePreChecked<T>(ref T value, FastBufferWriter.ForFixedStrings unused = default)
            where T : unmanaged, INativeList<byte>, IUTF8Bytes => m_Reader.ReadValue(out value);

        public void SerializeValuePreChecked(ref Vector2 value) => m_Reader.ReadValue(out value);
        public void SerializeValuePreChecked(ref Vector2[] value) => m_Reader.ReadValue(out value);

        public void SerializeValuePreChecked(ref Vector3 value) => m_Reader.ReadValue(out value);
        public void SerializeValuePreChecked(ref Vector3[] value) => m_Reader.ReadValue(out value);

        public void SerializeValuePreChecked(ref Vector2Int value) => m_Reader.ReadValue(out value);
        public void SerializeValuePreChecked(ref Vector2Int[] value) => m_Reader.ReadValue(out value);

        public void SerializeValuePreChecked(ref Vector3Int value) => m_Reader.ReadValue(out value);
        public void SerializeValuePreChecked(ref Vector3Int[] value) => m_Reader.ReadValue(out value);

        public void SerializeValuePreChecked(ref Vector4 value) => m_Reader.ReadValue(out value);
        public void SerializeValuePreChecked(ref Vector4[] value) => m_Reader.ReadValue(out value);

        public void SerializeValuePreChecked(ref Quaternion value) => m_Reader.ReadValue(out value);
        public void SerializeValuePreChecked(ref Quaternion[] value) => m_Reader.ReadValue(out value);

        public void SerializeValuePreChecked(ref Pose value) => m_Reader.ReadValue(out value);
        public void SerializeValuePreChecked(ref Pose[] value) => m_Reader.ReadValue(out value);

        public void SerializeValuePreChecked(ref Color value) => m_Reader.ReadValue(out value);
        public void SerializeValuePreChecked(ref Color[] value) => m_Reader.ReadValue(out value);

        public void SerializeValuePreChecked(ref Color32 value) => m_Reader.ReadValue(out value);
        public void SerializeValuePreChecked(ref Color32[] value) => m_Reader.ReadValue(out value);

        public void SerializeValuePreChecked(ref Ray value) => m_Reader.ReadValue(out value);
        public void SerializeValuePreChecked(ref Ray[] value) => m_Reader.ReadValue(out value);

        public void SerializeValuePreChecked(ref Ray2D value) => m_Reader.ReadValue(out value);
        public void SerializeValuePreChecked(ref Ray2D[] value) => m_Reader.ReadValue(out value);
    }
}
