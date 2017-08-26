using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Swigged.Cuda;

namespace GpuCore
{
    public struct StructWithInt32
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

    public struct StructStruct
    {
        public int a;
        public StructWithInt32 b;
    }


    [StructLayout(LayoutKind.Sequential)]
    public struct contact_info
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public String cell;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public String home;
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
            if (!Buffers.IsBlittable<System.Byte>()) throw new Exception("Expecting true.");
            if (!Buffers.IsBlittable<System.SByte>()) throw new Exception("Expecting true.");
            if (!Buffers.IsBlittable<System.Int16>()) throw new Exception("Expecting true.");
            if (!Buffers.IsBlittable<System.UInt16>()) throw new Exception("Expecting true.");
            if (!Buffers.IsBlittable<System.Int32>()) throw new Exception("Expecting true.");
            if (!Buffers.IsBlittable<System.UInt32>()) throw new Exception("Expecting true.");
            if (!Buffers.IsBlittable<System.Int64>()) throw new Exception("Expecting true.");
            if (!Buffers.IsBlittable<System.UInt64>()) throw new Exception("Expecting true.");
            if (!Buffers.IsBlittable<System.IntPtr>()) throw new Exception("Expecting true.");
            if (!Buffers.IsBlittable<System.UIntPtr>()) throw new Exception("Expecting true.");
            if (!Buffers.IsBlittable<System.Single>()) throw new Exception("Expecting true.");
            if (!Buffers.IsBlittable<System.Double>()) throw new Exception("Expecting true.");

            // None of the following should be blittable.

            if (Buffers.IsBlittable<System.Array>()) throw new Exception("Expecting false.");
            if (Buffers.IsBlittable<System.Boolean>()) throw new Exception("Expecting false.");
            if (Buffers.IsBlittable<System.Char>()) throw new Exception("Expecting false.");
            // System.Console.WriteLine(Blittable.IsBlittable < System.Class>());
            if (Buffers.IsBlittable<System.Object>()) throw new Exception("Expecting false.");
            // System.Console.WriteLine(Blittable.IsBlittable < System.Mdarray>());
            if (Buffers.IsBlittable<System.String>()) throw new Exception("Expecting false.");
            // System.Console.WriteLine(Blittable.IsBlittable < System.Valuetype>());
            // System.Console.WriteLine(Blittable.IsBlittable < System.Szarray>());

            // Mixed.

            if (Buffers.IsBlittable<System.Char>()) throw new Exception("Expecting false.");
            if (Buffers.IsBlittable<char>()) throw new Exception("Expecting false.");
            if (Buffers.IsBlittable<System.Boolean>()) throw new Exception("Expecting false.");
            if (Buffers.IsBlittable<bool>()) throw new Exception("Expecting false.");
            if (!Buffers.IsBlittable<System.Int32>()) throw new Exception("Expecting true.");
            if (!Buffers.IsBlittable<int>()) throw new Exception("Expecting true.");
            if (!Buffers.IsBlittable(typeof(decimal))) throw new Exception("Expecting true.");
            if (Marshal.SizeOf(typeof(decimal)) != 16) throw new Exception("Expecting 16.");

            // Surprisingly, arrays are blittable.
	        if (!Buffers.IsBlittable<int[]>()) throw new Exception("Expecting true.");
	        if (Buffers.IsBlittable<char[]>()) throw new Exception("Expecting false.");
            if (!Buffers.IsBlittable<StructWithInt32>()) throw new Exception("Expecting true.");
            // Structs are blittable, but only if all fields of it are blittable.
            if (Buffers.IsBlittable<StructWithBool>()) throw new Exception("Expecting false.");
            if (Buffers.IsBlittable<StructWithChar>()) throw new Exception("Expecting false.");
            if (Buffers.IsBlittable<ClassWithReference>()) throw new Exception("Expecting false.");
            if (!Buffers.IsBlittable<UInt128>()) throw new Exception("Expecting true.");
			if (!Buffers.IsBlittable<BlittableChar>()) throw new Exception("Expecting true.");
            if (!Buffers.IsBlittable<ValueTuple<int>>()) throw new Exception("Expecting true.");

            // Custom marshaling screws it all up. We don't use that because we want to generate code that
            // uses C# data types directly, not those converted to C.
            // See https://gist.github.com/erichschroeter/df895f2855af0fc89dd5

            if (Buffers.IsBlittable<ValueTuple<contact_info>>()) throw new Exception("Expecting false.");
            if (Buffers.IsBlittable<MyStruct>()) throw new Exception("Expecting false.");

            /////////////////////////////////////////////////////////////////////
            /////////////////////////////////////////////////////////////////////
            //
            // Convert non-blittable type into blittable type.
            //
            /////////////////////////////////////////////////////////////////////
            /////////////////////////////////////////////////////////////////////

            {
                Type orig = typeof(int);
                Type bt = Buffers.CreateImplementationType(orig);
                if (!Buffers.IsBlittable(bt)) throw new Exception("Expecting true.");
                string orig_s = Buffers.OutputType(orig);
                string bt_s = Buffers.OutputType(bt);
                if (orig_s != bt_s) throw new Exception("Types not identical.");
            }

            {
				Type orig = typeof(char);
				Type bt = Buffers.CreateImplementationType(orig);
				if (!Buffers.IsBlittable(bt)) throw new Exception("Expecting true.");
				string orig_s = Buffers.OutputType(orig);
				string bt_s = Buffers.OutputType(bt);
				if (orig_s == bt_s) throw new Exception("Types should not be identical.");
            }

            {
				Type orig = typeof(int[]);
				Type bt = Buffers.CreateImplementationType(orig);
				if (!Buffers.IsBlittable(bt)) throw new Exception("Expecting true.");
				string orig_s = Buffers.OutputType(orig);
				string bt_s = Buffers.OutputType(bt);
                if (orig_s == bt_s) throw new Exception("Types should not be identical.");
            }

            {
				Type orig = typeof(StructWithInt32);
				Type bt = Buffers.CreateImplementationType(orig);
				if (!Buffers.IsBlittable(bt)) throw new Exception("Expecting true.");
				string orig_s = Buffers.OutputType(orig);
				string bt_s = Buffers.OutputType(bt);
				if (orig_s != bt_s) throw new Exception("Types not identical.");
            }

            {
				Type orig = typeof(StructWithBool);
				Type bt = Buffers.CreateImplementationType(orig);
				if (!Buffers.IsBlittable(bt)) throw new Exception("Expecting true.");
				string orig_s = Buffers.OutputType(orig);
				string bt_s = Buffers.OutputType(bt);
				if (orig_s == bt_s) throw new Exception("Types should not be identical.");
            }

            {
				Type orig = typeof(StructWithChar);
				Type bt = Buffers.CreateImplementationType(orig);
				if (!Buffers.IsBlittable(bt)) throw new Exception("Expecting true.");
				string orig_s = Buffers.OutputType(orig);
				string bt_s = Buffers.OutputType(bt);
				if (orig_s == bt_s) throw new Exception("Types should not be identical.");
            }

            {
				Type orig = typeof(ClassWithReference);
				Type bt = Buffers.CreateImplementationType(orig);
				if (!Buffers.IsBlittable(bt)) throw new Exception("Expecting true.");
				string orig_s = Buffers.OutputType(orig);
				string bt_s = Buffers.OutputType(bt);
				if (orig_s == bt_s) throw new Exception("Types should not be identical.");
            }

            {
				Type orig = typeof(List<int>);
				Type bt = Buffers.CreateImplementationType(orig);
				if (!Buffers.IsBlittable(bt)) throw new Exception("Expecting true.");
				string orig_s = Buffers.OutputType(orig);
                System.Console.WriteLine(orig_s);
				string bt_s = Buffers.OutputType(bt);
                System.Console.WriteLine(bt_s);
				if (orig_s == bt_s) throw new Exception("Types should not be identical.");
            }

            {
				Type orig = typeof(List<char>);
				Type bt = Buffers.CreateImplementationType(orig);
				if (!Buffers.IsBlittable(bt)) throw new Exception("Expecting true.");
				string orig_s = Buffers.OutputType(orig);
				string bt_s = Buffers.OutputType(bt);
				if (orig_s == bt_s) throw new Exception("Types should not be identical.");
            }

            /////////////////////////////////////////////////////////////////////
            /////////////////////////////////////////////////////////////////////
            //
            // Prerequisite -- need to be able to pin object references in order
            // to do a deep copy.
            //
            /////////////////////////////////////////////////////////////////////
            /////////////////////////////////////////////////////////////////////


            // Let's now see how GCHandle works, which we will need for copying to blittable type.
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


            /////////////////////////////////////////////////////////////////////
            /////////////////////////////////////////////////////////////////////
            //
            // Deep copy to implementation and back.
            //
            /////////////////////////////////////////////////////////////////////
            /////////////////////////////////////////////////////////////////////



            {
                // Let's try to convert a simple non-blittable type into a blittable type.
                int i = 1;
                Buffers.DeepCopyToImplementation(i, out object j);
                if ((int)j != i) throw new Exception("Copy failed.");
            }

            {
                int f = 1;
                System.Console.WriteLine();
                Buffers.DeepCopyToImplementation(f, out object j);
                Buffers.DeepCopyFromImplementation(j, out object too, typeof(int));
                var g = (int)too;
                if (g != f) throw new Exception("Copy failed.");
            }

            {
                // Let's try to convert a simple non-blittable type into a blittable type.
                StructWithInt32 f = new StructWithInt32() { a = 1, b = 2 };
                System.Console.WriteLine();
                Buffers.DeepCopyToImplementation(f, out object j);
                Type t = j.GetType();
                Type ft = f.GetType();
                //StructWithInt32 v = (StructWithInt32)j; cannot be done because the types are not the same.
                Buffers.DeepCopyFromImplementation(j, out object too, ft);
                StructWithInt32 v = (StructWithInt32)too;
                if (!f.Equals(v)) throw new Exception("Copy failed.");
            }

            {
                // Let's try to convert a simple non-blittable type into a blittable type.
                StructStruct f = new StructStruct() { a = 1, b = new StructWithInt32(){a=3, b=4} };
                Type orig = f.GetType();
                Type bt = Buffers.CreateImplementationType(orig);
                if (!Buffers.IsBlittable(bt)) throw new Exception("Expecting true.");
                string orig_s = Buffers.OutputType(orig);
                System.Console.WriteLine(orig_s);
                string bt_s = Buffers.OutputType(bt);
                System.Console.WriteLine(bt_s);
                System.Console.WriteLine(Marshal.SizeOf(f));

                GCHandle handle = GCHandle.Alloc(f, GCHandleType.Pinned);
                IntPtr pointer = (IntPtr)handle;
                handle.Free();

                System.Console.WriteLine();
                Buffers.DeepCopyToImplementation(f, out object j);
            }

            {
                // Let's try to convert a simple non-blittable type into a blittable type.
                var f = new int[] { 1, 2, 3 };
                Buffers.DeepCopyToImplementation(f, out object j);
                Buffers.DeepCopyFromImplementation(j, out object too, f.GetType());
                int[] v = (int[])too;
                if (!f.SequenceEqual(v)) throw new Exception("Copy failed.");
            }

            {
                StructWithInt32 f = new StructWithInt32() { a = 1, b = 2 };
                System.Console.WriteLine();
                Buffers.DeepCopyToImplementation(f, out object j);
                Buffers.DeepCopyFromImplementation(j, out object too, typeof(StructWithInt32));
                var g = (StructWithInt32)too;
                if (g.a != f.a) throw new Exception("Copy failed.");
                if (g.b != f.b) throw new Exception("Copy failed.");
            }

            {
                StructWithBool f = new StructWithBool() { a = true, b = 2 };
                System.Console.WriteLine();
                Buffers.DeepCopyToImplementation(f, out object j);
                Buffers.DeepCopyFromImplementation(j, out object too, typeof(StructWithBool));
                var g = (StructWithBool)too;
                if (g.a != f.a) throw new Exception("Copy failed.");
                if (g.b != f.b) throw new Exception("Copy failed.");
            }

            {
                StructWithBool f = new StructWithBool() { a = false, b = 2 };
                System.Console.WriteLine();
                Buffers.DeepCopyToImplementation(f, out object j);
                Buffers.DeepCopyFromImplementation(j, out object too, typeof(StructWithBool));
                var g = (StructWithBool)too;
                if (g.a != f.a) throw new Exception("Copy failed.");
                if (g.b != f.b) throw new Exception("Copy failed.");
            }

            {
                // Let's try a binary tree.
                var n1 = new TreeNode() { Left = null, Right = null, Id = 1 };
                var n2 = new TreeNode() { Left = null, Right = null, Id = 2 };
                var n3 = new TreeNode() { Left = n1, Right = n2, Id = 3 };
                var n4 = new TreeNode() { Left = n3, Right = null, Id = 4 };
                var bt = Buffers.CreateImplementationType(typeof(TreeNode));
                System.Console.WriteLine();
                System.Console.WriteLine(Buffers.IsBlittable(bt));//// 
                Buffers.OutputType(typeof(TreeNode));
                Buffers.OutputType(bt);
                Buffers.DeepCopyToImplementation(n4, out object j);
                Buffers.DeepCopyFromImplementation(j, out object too, typeof(TreeNode));
                var o4 = (TreeNode) too;
                if (o4.Id != n4.Id) throw new Exception("Copy failed.");
                if (o4.Left.Id != n3.Id) throw new Exception("Copy failed.");
                if (o4.Left.Left.Id != n1.Id) throw new Exception("Copy failed.");
                if (o4.Left.Right.Id != n2.Id) throw new Exception("Copy failed.");
            }
        }
    }
}
