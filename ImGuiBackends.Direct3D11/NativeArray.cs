using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ImGuiBackends.Direct3D11
{
    internal readonly unsafe ref struct NativeArray<T> where T : unmanaged
    {
        readonly T* arrayPtr;
        public readonly int Length;

        public static implicit operator T* (NativeArray<T> array) => array.arrayPtr;

        public NativeArray(int length, bool init = true)
        {
            arrayPtr = (T*)Marshal.AllocHGlobal(sizeof(T) * length);
            if (init)
            {
                Unsafe.InitBlockUnaligned(arrayPtr, 0, (uint)(sizeof(T) * length));
            }
            this.Length = length;
        }

        public void Dispose()
        {
            if (arrayPtr != (void*)0)
                Marshal.FreeHGlobal((IntPtr)arrayPtr);
        }

        public NativeArrayEnumerator GetEnumerator() => new(in this);

        public unsafe readonly ref struct PointerArray
        {
            private readonly NativeArray<nint> backingArray;
            public static implicit operator T**(PointerArray array) => (T**)array.backingArray.arrayPtr;

            public readonly int Length;
            public PointerArray(int length)
            {
                // Making a copy is an extra 4 bytes but much faster on frequent access.
                this.Length = length;
                this.backingArray = new(length, true);
            }

            public void Dispose()
            {
                this.backingArray.Dispose();
            }

            public PointerArrayEnumerator GetEnumerator() => new(in this.backingArray);

            internal unsafe ref struct PointerArrayEnumerator
            {
                public ref T* Current => ref ((T**)backing.arrayPtr)[_index];

                private int _index;

                private readonly NativeArray<nint> backing;

                internal PointerArrayEnumerator(in NativeArray<nint> array)
                {
                    this.backing = array;
                    this._index = -1;
                }

                public bool MoveNext()
                {
                    int index = _index + 1;

                    if (index < this.backing.Length)
                    {
                        _index = index;
                        return true;
                    }

                    return false;
                }
            }
        }

        internal unsafe ref struct NativeArrayEnumerator
        {
            public ref T Current => ref backing.arrayPtr[_index];

            private int _index;

            private readonly NativeArray<T> backing;

            internal NativeArrayEnumerator(in NativeArray<T> array)
            {
                this.backing = array;
                this._index = -1;
            }

            public bool MoveNext()
            {
                int index = _index + 1;

                if (index < this.backing.Length)
                {
                    _index = index;
                    return true;
                }

                return false;
            }
        }
    }
}
