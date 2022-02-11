using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ImGuiBackends.Direct3D11
{
    internal readonly unsafe ref struct NativeArray<T> where T : unmanaged
    {
        readonly T* arrayPtr;
        public readonly int Length;

        public static implicit operator T* (NativeArray<T> array) => array.arrayPtr;

        public NativeArray(int length)
        {
            arrayPtr = (T*)Marshal.AllocHGlobal(sizeof(T) * length);
            this.Length = length;
        }
        public void Dispose()
        {
            Marshal.FreeHGlobal((IntPtr)arrayPtr);
        }
    }

    unsafe readonly ref struct NativePtrArray<T> where T : unmanaged
    {
        readonly T** arrayPtr;
        public readonly int Length;

        public static implicit operator T** (NativePtrArray<T> array) => array.arrayPtr;

        public NativePtrArray(int length)
        {
            arrayPtr = (T**)Marshal.AllocHGlobal(sizeof(T*) * length);
            this.Length = length;
        }

        public void Dispose()
        {
            Marshal.FreeHGlobal((IntPtr)arrayPtr);
        }
    }
}
