using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Swigged.Cuda;

namespace AllAboutBlittable
{
    struct StructWithInt32
    {
        public int a;
        public int b;
    }

    struct StructWithBool
    {
        public bool a;
        public int b;
    }

    struct StructWithChar
    {
        public Char a;
        public int b;
    }

    class ClassWithReference
    {
        public int a;
        public ClassWithReference b;
    }


    class TreeNode
    {
        public TreeNode Left;
        public TreeNode Right;
        public int Id;
    }

    /// <summary>
    /// These types from http://aakinshin.net/blog/post/blittable/
    /// This is a formatted value type that contains blittable types.
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    public struct UInt128
    {
        [FieldOffset(0)]
        public ulong Value1;
        [FieldOffset(8)]
        public ulong Value2;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MyStruct
    {
        public UInt128 UInt128;
        public char Char;
    }

    /// <summary>
    /// This is a formatted value type.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct BlittableChar
    {
        public char Value;

        public static explicit operator BlittableChar(char value)
        {
            return new BlittableChar { Value = value };
        }

        public static implicit operator char(BlittableChar value)
        {
            return value.Value;
        }
    }

    class Program
    {
        static void Main(string[] args)
        {

            // Let's start with some basics:
            //
            // (1) What is a blittable type?
            //
            // A blittable type is as having an identical presentation in memory for
            // managed and unmanaged environments, and can be directly shared.
            // https://en.wikipedia.org/wiki/Blittable_types
            //
            // (2) What is marshalling?
            //
            // Marshalling is the conversion from one representation of a type into another representation.
            // https://en.wikipedia.org/wiki/Marshalling_(computer_science)
            //
            // (3) What is an Appication Binary Interface (ABI)?
            //
            // An ABI defines the structures and methods used to access external, already
            // compiled libraries/code at the level of machine code.
            // https://en.wikipedia.org/wiki/Application_binary_interface
            //
            // (4) What is an execution model?
            //
            // Every programming language has an execution model, which is specified as part of the language 
            // specification, and is implemented as part of the language implementation. 
            // https://en.wikipedia.org/wiki/Execution_model
            //
            // (5) What is Unified Virtual Address (UVA)?
            //
            // UVA provides a single virtual memory address space for all memory in the system, and enables
            // pointers to be accessed from GPU code no matter where in the system they reside, whether its
            // device memory (on the same or a different GPU), host memory, or on-chip shared memory.
            // Note: UVA enables “Zero-Copy” memory, which is pinned host memory accessible by device code directly,
            // over PCI-Express, without a memcpy. 
            // http://docs.nvidia.com/cuda/cuda-c-programming-guide/index.html#unified-virtual-address-space
            //
            // (6) What is Unified Memory in an NVIDIA GPU?
            //
            // Unified Memory is memory that is able to automatically migrate to and from the GPU/Host CPU.
            // https://devblogs.nvidia.com/parallelforall/unified-memory-in-cuda-6/
            //
            // (7) What types are blittable?
            //
            // Reference types are not; bool and char are not.
            // https://docs.microsoft.com/en-us/dotnet/framework/interop/blittable-and-non-blittable-types
            //
            // Blittable and Non-Blittable Types
            // http://msdn.microsoft.com/en-us/75dwhxf7.aspx
            // Copying and Pinning
            // http://msdn.microsoft.com/en-us/23acw07k.aspx
            // CLR Inside Out: Marshaling between Managed and Unmanaged Code
            // http://msdn.microsoft.com/en-us/cc164193.aspx
            //     .NET Column: P / Invoke Revisited
            // http://msdn.microsoft.com/en-us/cc163910.aspx
            // An Overview of Managed/ Unmanaged Code Interoperability
            // http://msdn.microsoft.com/en-us/library/ms973872.aspx
            //
            // (8) What is GCHandleType?
            //
            // GCHandleType is used in GCHandle.Alloc() to define the type of handle
            // to allocate for an object reference. There are four types:
            // Normal, used to indicate that an object is not collected by the garbage collector.
            // The object can be moved however.
            //
            // Pinned, used to indicate "Normal" behavior, plus the object cannot be moved. As
            // unmanaged code uses pointers to indicate differenct objects, all objects must be
            // pinned if they are to be used in unsafe/unmanaged code.
            //
            // Weak, used to to allow an object to be collected and zeroed out.
            //
            // WeakTrackResurrection, similar to "Weak", but where the object is not zeroed out.
            //
            // https://msdn.microsoft.com/en-us/library/83y4ak54(v=vs.110).aspx
            // https://stackoverflow.com/questions/25274575/gchandle-when-to-use-gchandletype-normal-explicitly

            // Let's call my IsBlittable function, showing what is blittable or not.
            // These are from MS, https://docs.microsoft.com/en-us/dotnet/framework/interop/blittable-and-non-blittable-types.
            // All should be true.
            System.Console.WriteLine(Blittable.IsBlittable<System.Byte>());
            System.Console.WriteLine(Blittable.IsBlittable < System.SByte>());
            System.Console.WriteLine(Blittable.IsBlittable < System.Int16>());
            System.Console.WriteLine(Blittable.IsBlittable < System.UInt16>());
            System.Console.WriteLine(Blittable.IsBlittable < System.Int32>());
            System.Console.WriteLine(Blittable.IsBlittable < System.UInt32>());
            System.Console.WriteLine(Blittable.IsBlittable < System.Int64>());
            System.Console.WriteLine(Blittable.IsBlittable < System.UInt64>());
            System.Console.WriteLine(Blittable.IsBlittable < System.IntPtr>());
            System.Console.WriteLine(Blittable.IsBlittable < System.UIntPtr>());
            System.Console.WriteLine(Blittable.IsBlittable < System.Single>());
            System.Console.WriteLine(Blittable.IsBlittable < System.Double>());

            // None of the following should be blittable.

            System.Console.WriteLine(Blittable.IsBlittable < System.Array>());
            System.Console.WriteLine(Blittable.IsBlittable < System.Boolean>());
            System.Console.WriteLine(Blittable.IsBlittable < System.Char>());
            // System.Console.WriteLine(Blittable.IsBlittable < System.Class>());
            System.Console.WriteLine(Blittable.IsBlittable < System.Object>());
            // System.Console.WriteLine(Blittable.IsBlittable < System.Mdarray>());
            System.Console.WriteLine(Blittable.IsBlittable < System.String>());
            // System.Console.WriteLine(Blittable.IsBlittable < System.Valuetype>());
            // System.Console.WriteLine(Blittable.IsBlittable < System.Szarray>());

            System.Console.WriteLine(Blittable.IsBlittable<System.Char>() + " expect false");
            System.Console.WriteLine(Blittable.IsBlittable<char>() + " expect false");
            System.Console.WriteLine(Blittable.IsBlittable<System.Boolean>() + " expect false");
            System.Console.WriteLine(Blittable.IsBlittable<bool>() + " expect false");
            System.Console.WriteLine(Blittable.IsBlittable<System.Int32>());
            System.Console.WriteLine(Blittable.IsBlittable<int>());
            System.Console.WriteLine(Blittable.IsBlittable(typeof(decimal)));
            System.Console.WriteLine(Marshal.SizeOf(typeof(decimal)));
            // Surprisingly, arrays are blittable.
            System.Console.WriteLine(Blittable.IsBlittable<int[]>());
            System.Console.WriteLine(Blittable.IsBlittable<StructWithInt32>());
            // Structs are blittable, but only if all fields of it are blittable.
            System.Console.WriteLine(Blittable.IsBlittable<StructWithBool>() + " expect false");
            System.Console.WriteLine(Blittable.IsBlittable<StructWithChar>());
            System.Console.WriteLine(Blittable.IsBlittable<ClassWithReference>());
            System.Console.WriteLine(Blittable.IsBlittable<UInt128>());
            System.Console.WriteLine(Blittable.IsBlittable<MyStruct>());
            System.Console.WriteLine(Blittable.IsBlittable<BlittableChar>());

            // Let's now see how GCHandle works, which we will need for CUDA.
            // At it turns out, to pin something, it must be blittable.
            {
                int f = new int();
                GCHandle handle = GCHandle.Alloc(f, GCHandleType.Pinned);
                IntPtr pointer = (IntPtr)handle;
                handle.Free();
            }

            {
                StructWithInt32 f = new StructWithInt32();
                GCHandle handle = GCHandle.Alloc(f, GCHandleType.Pinned);
                IntPtr pointer = (IntPtr)handle;
                handle.Free();
            }

            {
                // This code will throw exception because StructWithBool is not blittable.
                //StructWithBool f = new StructWithBool();
                //GCHandle handle = GCHandle.Alloc(f, GCHandleType.Pinned);
                //IntPtr pointer = (IntPtr)handle;
            }

            {
                // StructWithBool is not blittable, but a handle can be created to imform the garbage collector
                // to not free the object even if there are no references to the object. Free must be
                // called in order to allow the garbage collector to release the object.
                StructWithBool f = new StructWithBool();
                GCHandle handle = GCHandle.Alloc(f);
                IntPtr pointer = (IntPtr)handle;
                handle.Free();
            }

            // Let's try some cuda driver functions. First, let's initialize things.
            Cuda.cuInit(0);
            Cuda.cuCtxCreate_v2(out CUcontext pctx, (uint)Swigged.Cuda.CUctx_flags.CU_CTX_MAP_HOST, 0);

            {
                // Let's try allocating a block of memory on the host. cuMemHostAlloc allocates bytesize
                // bytes of host memory that is page-locked and accessible to the device.
                // Note: cuMemHostAlloc and cuMemAllocHost seem to be almost identical except for the
                // third parameter to cuMemHostAlloc that is used for the type of memory allocation.
                var res = Cuda.cuMemHostAlloc(out IntPtr p, 10, (uint)Cuda.CU_MEMHOSTALLOC_DEVICEMAP);
                if (res == CUresult.CUDA_SUCCESS) System.Console.WriteLine("Worked.");
                else System.Console.WriteLine("Did not work.");
            }

            {
                // Allocate CPU memory, pin it, then register it with GPU.
                int f = new int();
                GCHandle handle = GCHandle.Alloc(f, GCHandleType.Pinned);
                IntPtr pointer = (IntPtr)handle;
                var size = Marshal.SizeOf(f);
                var res = Cuda.cuMemHostRegister_v2(pointer, (uint)size, (uint)Cuda.CU_MEMHOSTALLOC_DEVICEMAP);
                if (res == CUresult.CUDA_SUCCESS) System.Console.WriteLine("Worked.");
                else System.Console.WriteLine("Did not work.");
            }

            {
                // Allocate Unified Memory.
                var size = 4;
                var res = Cuda.cuMemAllocManaged(out IntPtr pointer, (uint)size, (uint)Swigged.Cuda.CUmemAttach_flags.CU_MEM_ATTACH_GLOBAL);
                if (res == CUresult.CUDA_SUCCESS) System.Console.WriteLine("Worked.");
                else System.Console.WriteLine("Did not work.");
            }

            /////////////////////////////////////////////////////////////////////
            /////////////////////////////////////////////////////////////////////
            //
            // Convert non-blittable type into blittable type.
            //
            /////////////////////////////////////////////////////////////////////
            /////////////////////////////////////////////////////////////////////


            {
                System.Console.WriteLine();
                var bt = Blittable.CreateBlittableType(typeof(int), false, false);
                Blittable.OutputType(typeof(int));
                System.Console.WriteLine(Blittable.IsBlittable(bt));
                Blittable.OutputType(bt);
            }

            {
                System.Console.WriteLine();
                Blittable.OutputType(typeof(char));
                var bt = Blittable.CreateBlittableType(typeof(char), false, false);
                System.Console.WriteLine(Blittable.IsBlittable(bt));
                Blittable.OutputType(bt);
            }

            {
                System.Console.WriteLine();
                Blittable.OutputType(typeof(int[]));
                var bt = Blittable.CreateBlittableType(typeof(int[]), false, false);
                System.Console.WriteLine(Blittable.IsBlittable(bt));
                Blittable.OutputType(bt);
            }

            {
                System.Console.WriteLine();
                Blittable.OutputType(typeof(StructWithInt32));
                var bt = Blittable.CreateBlittableType(typeof(StructWithInt32), false, false);
                System.Console.WriteLine(Blittable.IsBlittable(bt));
                Blittable.OutputType(bt);
            }

            {
                System.Console.WriteLine();
                Blittable.OutputType(typeof(StructWithBool));
                var bt = Blittable.CreateBlittableType(typeof(StructWithBool), false, false);
                System.Console.WriteLine(Blittable.IsBlittable(bt));
                Blittable.OutputType(bt);
            }

            {
                System.Console.WriteLine();
                Blittable.OutputType(typeof(StructWithChar));
                var bt = Blittable.CreateBlittableType(typeof(StructWithChar), false, false);
                System.Console.WriteLine(Blittable.IsBlittable(bt));
                Blittable.OutputType(bt);
            }

            {
                // Let's try to convert a simple non-blittable object into a blittable one.
                var bt = Blittable.CreateBlittableType(typeof(ClassWithReference), false, false);
                System.Console.WriteLine();
                System.Console.WriteLine(Blittable.IsBlittable(bt));
                Blittable.OutputType(typeof(ClassWithReference));
                Blittable.OutputType(bt);
            }

            {
                var list = new List<int>();
                System.Console.WriteLine();
                Blittable.OutputType(list.GetType());
                System.Console.WriteLine(Blittable.IsBlittable(list.GetType()));
                var bt = Blittable.CreateBlittableType(list.GetType(), false, false);
                System.Console.WriteLine(Blittable.IsBlittable(bt));
                Blittable.OutputType(bt);
            }

            {
                var list = new List<char>();
                System.Console.WriteLine(Blittable.IsBlittable(list.GetType()));
                var bt = Blittable.CreateBlittableType(list.GetType(), false, false);
                System.Console.WriteLine();
                System.Console.WriteLine(Blittable.IsBlittable(bt));
                Blittable.OutputType(list.GetType());
                Blittable.OutputType(bt);
            }

            /////////////////////////////////////////////////////////////////////
            /////////////////////////////////////////////////////////////////////
            //
            // Copy from non-blittable or blittable managed object to unmanaged.
            //
            /////////////////////////////////////////////////////////////////////
            /////////////////////////////////////////////////////////////////////


            {
                // Let's try to convert a simple non-blittable type into a blittable type.
                int i = 1;
                System.Console.WriteLine();
                Blittable.CopyToManagedBlittableType(i, out object j);
            }

            {
                // Let's try to convert a simple non-blittable type into a blittable type.
                StructWithInt32 f = new StructWithInt32() { a = 1, b = 2 };
                System.Console.WriteLine();
                Blittable.CopyToManagedBlittableType(f, out object j);
            }

            {
                // Let's try to convert a simple non-blittable type into a blittable type.
                var f = new int[] { 1, 2, 3 };
                System.Console.WriteLine();
                Blittable.CopyToManagedBlittableType(f, out object j);
            }


            /////////////////////////////////////////////////////////////////////
            /////////////////////////////////////////////////////////////////////
            //
            // Deep copy from unmanaged to non-blittable managed.
            //
            /////////////////////////////////////////////////////////////////////
            /////////////////////////////////////////////////////////////////////


            {
                int f = 1;
                System.Console.WriteLine();
                Blittable.CopyToManagedBlittableType(f, out object j);
                Blittable.CopyFromManagedBlittableType(j, out object too, typeof(int));
                var g = (int) too;
                if (g != f) throw new Exception("Copy failed.");
            }

            {
                StructWithInt32 f = new StructWithInt32() { a = 1, b = 2 };
                System.Console.WriteLine();
                Blittable.CopyToManagedBlittableType(f, out object j);
                Blittable.CopyFromManagedBlittableType(j, out object too, typeof(StructWithInt32));
                var g = (StructWithInt32)too;
                if (g.a != f.a) throw new Exception("Copy failed.");
                if (g.b != f.b) throw new Exception("Copy failed.");
            }

            {
                StructWithBool f = new StructWithBool() { a = true, b = 2 };
                System.Console.WriteLine();
                Blittable.CopyToManagedBlittableType(f, out object j);
                Blittable.CopyFromManagedBlittableType(j, out object too, typeof(StructWithBool));
                var g = (StructWithBool)too;
                if (g.a != f.a) throw new Exception("Copy failed.");
                if (g.b != f.b) throw new Exception("Copy failed.");
            }

            {
                StructWithBool f = new StructWithBool() { a = false, b = 2 };
                System.Console.WriteLine();
                Blittable.CopyToManagedBlittableType(f, out object j);
                Blittable.CopyFromManagedBlittableType(j, out object too, typeof(StructWithBool));
                var g = (StructWithBool)too;
                if (g.a != f.a) throw new Exception("Copy failed.");
                if (g.b != f.b) throw new Exception("Copy failed.");
            }

            {
                var f = new int[] { 1, 2, 3 };
                System.Console.WriteLine();
                Blittable.CopyToManagedBlittableType(f, out object j);
                Blittable.CopyFromManagedBlittableType(j, out object too, typeof(int[]));
                var g = (int[]) too;
                for (int i = 0; i < f.Length; ++i)
                    if (g[i] != f[i]) throw new Exception("Copy failed.");
            }

            {
                // Let's try a binary tree.
                var n1 = new TreeNode() { Left = null, Right = null, Id = 1 };
                var n2 = new TreeNode() { Left = null, Right = null, Id = 2 };
                var n3 = new TreeNode() { Left = n1, Right = n2, Id = 3 };
                var n4 = new TreeNode() { Left = n3, Right = null, Id = 4 };
                var bt = Blittable.CreateBlittableType(typeof(TreeNode), false, false);
                System.Console.WriteLine();
                System.Console.WriteLine(Blittable.IsBlittable(bt));//// 
                Blittable.OutputType(typeof(TreeNode));
                Blittable.OutputType(bt);
                Blittable.CopyToManagedBlittableType(n4, out object j);
                Blittable.CopyFromManagedBlittableType(j, out object too, typeof(TreeNode));
                var o4 = (TreeNode) too;
                if (o4.Id != n4.Id) throw new Exception("Copy failed.");
                if (o4.Left.Id != n3.Id) throw new Exception("Copy failed.");
                if (o4.Left.Left.Id != n1.Id) throw new Exception("Copy failed.");
                if (o4.Left.Right.Id != n2.Id) throw new Exception("Copy failed.");
            }
        }
    }
}
