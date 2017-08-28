﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using Swigged.Cuda;

namespace GpuCore
{

    /// <summary>
    /// This code marshals C#/Net data structures that have an unknown implementation to/from
    /// the implementation for NVIDIA GPUs.
    /// 
    /// In particular:
    /// * Object references are implemented as pointers.
    /// * Blittable types are implemented as is.
    /// * Char is implemented as UInt16.
    /// * Bool is implemented as Byte, with true = 1, false = 0.
    /// </summary>
    public class Buffers
    {
        private CUcontext _pctx;
        Asm _asm;
        static Dictionary<string, string> _type_name_map = new Dictionary<string, string>();
        static Dictionary<object, IntPtr> _allocated_objects = new Dictionary<object, IntPtr>();

        public Buffers()
        {
            _asm = new Asm();
            Cuda.cuInit(0);
            Cuda.cuCtxCreate_v2(out _pctx, (uint)Swigged.Cuda.CUctx_flags.CU_CTX_MAP_HOST, 0);
        }

        /// <summary>
        /// This code to check if a type is blittable.
        /// See http://aakinshin.net/blog/post/blittable/
        /// Original from https://stackoverflow.com/questions/10574645/the-fastest-way-to-check-if-a-type-is-blittable/31485271#31485271
        /// Purportedly, System.Decimal is supposed to not be blittable, but appears on Windows 10, VS 2017, NF 4.6.
        /// </summary>

        public static bool IsBlittable<T>()
        {
            return IsBlittableCache<T>.Value;
        }

        public static bool IsBlittable(Type type)
        {
            if (type.IsArray)
            {
                var elem = type.GetElementType();
                return elem.IsValueType && IsBlittable(elem);
            }
            try
            {
                object instance = FormatterServices.GetUninitializedObject(type);
                GCHandle.Alloc(instance, GCHandleType.Pinned).Free();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static class IsBlittableCache<T>
        {
            public static readonly bool Value = IsBlittable(typeof(T));
        }

        public static bool IsStruct(System.Type t)
        {
            return t.IsValueType && !t.IsPrimitive && !t.IsEnum;
        }


        /// <summary>
        /// Asm class used by CreateImplementationType in order to create a blittable type
        /// corresponding to a host type.
        /// </summary>
        class Asm
        {
            public System.Reflection.AssemblyName assemblyName;
            public AssemblyBuilder ab;
            public ModuleBuilder mb;
            static int v = 1;

            public Asm()
            {
                assemblyName = new System.Reflection.AssemblyName("DynamicAssembly" + v++);
                ab = AppDomain.CurrentDomain.DefineDynamicAssembly(
                    assemblyName,
                    AssemblyBuilderAccess.RunAndSave);
                mb = ab.DefineDynamicModule(assemblyName.Name, assemblyName.Name + ".dll");
            }
        }

        public Type CreateImplementationType(Type hostType, bool declare_parent_chain = true, bool declare_flatten_structure = false)
        {
            try
            {
                // Let's start with basic types.
                if (hostType.FullName.Equals("System.Object"))
                {
                    return typeof(System.Object);
                }
                if (hostType.FullName.Equals("System.Int16"))
                {
                    return typeof(System.Int16);
                }
                if (hostType.FullName.Equals("System.Int32"))
                {
                    return typeof(System.Int32);
                }
                if (hostType.FullName.Equals("System.Int64"))
                {
                    return typeof(System.Int64);
                }
                if (hostType.FullName.Equals("System.UInt16"))
                {
                    return typeof(System.UInt16);
                }
                if (hostType.FullName.Equals("System.UInt32"))
                {
                    return typeof(System.UInt32);
                }
                if (hostType.FullName.Equals("System.UInt64"))
                {
                    return typeof(System.UInt64);
                }
                if (hostType.FullName.Equals("System.IntPtr"))
                {
                    return typeof(System.IntPtr);
                }

                // Map boolean into byte.
                if (hostType.FullName.Equals("System.Boolean"))
                {
                    return typeof(System.Byte);
                }

                // Map char into uint16.
                if (hostType.FullName.Equals("System.Char"))
                {
                    return typeof(System.UInt16);
                }

                String name;
                System.Reflection.TypeFilter tf;
                Type bbt = null;

                // Declare inheritance types.
                if (declare_parent_chain)
                {
                    // First, declare base type
                    Type bt = hostType.BaseType;
                    bbt = bt;
                    if (bt != null && !bt.FullName.Equals("System.Object") && !bt.FullName.Equals("System.ValueType"))
                    {
                        bbt = CreateImplementationType(bt, declare_parent_chain, declare_flatten_structure);
                    }
                }

                name = hostType.FullName;
                _type_name_map.TryGetValue(name, out string alt);
                tf = new System.Reflection.TypeFilter((Type t, object o) =>
                {
                    return t.FullName == name || t.FullName == alt;
                });

                // Find if blittable type for hostType was already performed.
                Type[] types = _asm.mb.FindTypes(tf, null);

                // If blittable type was not created, create one with all fields corresponding
                // to that in host, with special attention to arrays.
                if (types.Length != 0)
                    return types[0];

                if (hostType.IsArray)
                {
                    Type elementType = CreateImplementationType(hostType.GetElementType(), declare_parent_chain,
                        declare_flatten_structure);
                    object array_obj = Array.CreateInstance(elementType, 0);
                    Type array_type = array_obj.GetType();

                    // For arrays, convert into a struct with first field being a
                    // pointer, and the second field a length.

                    var tb = _asm.mb.DefineType(
                        array_type.FullName,
                        System.Reflection.TypeAttributes.Public
                        | System.Reflection.TypeAttributes.SequentialLayout
                        | System.Reflection.TypeAttributes.Serializable);
                    _type_name_map[hostType.FullName] = tb.FullName;

                    // Convert byte, int, etc., in host type to pointer in blittable type.
                    // With array, we need to also encode the length.
                    tb.DefineField("ptr", typeof(IntPtr), System.Reflection.FieldAttributes.Public);
                    tb.DefineField("len", typeof(Int32), System.Reflection.FieldAttributes.Public);

                    return tb.CreateType();
                }
                else if (IsStruct(hostType))
                {
                    TypeBuilder tb = null;
                    if (bbt != null)
                    {
                        tb = _asm.mb.DefineType(
                            name,
                            System.Reflection.TypeAttributes.Public
                            | System.Reflection.TypeAttributes.SequentialLayout
                            | System.Reflection.TypeAttributes.Serializable,
                            bbt);
                    }
                    else
                    {
                        tb = _asm.mb.DefineType(
                            name,
                            System.Reflection.TypeAttributes.Public
                            | System.Reflection.TypeAttributes.SequentialLayout
                            | System.Reflection.TypeAttributes.Serializable);
                    }
                    _type_name_map[name] = tb.FullName;
                    Type ht = hostType;
                    while (ht != null)
                    {
                        var fields = ht.GetFields(
                            System.Reflection.BindingFlags.Instance
                            | System.Reflection.BindingFlags.NonPublic
                            | System.Reflection.BindingFlags.Public
                            | System.Reflection.BindingFlags.Static);
                        var fields2 = ht.GetFields();
                        foreach (var field in fields)
                        {
                            // For non-array type fields, just define the field as is.
                            // Recurse
                            Type elementType = CreateImplementationType(field.FieldType, declare_parent_chain,
                                declare_flatten_structure);
                            // If elementType is a reference or array, then we need to convert it to a IntPtr.
                            if (elementType.IsClass || elementType.IsArray)
                                elementType = typeof(System.IntPtr);
                            tb.DefineField(field.Name, elementType, System.Reflection.FieldAttributes.Public);
                        }
                        if (declare_flatten_structure)
                            ht = ht.BaseType;
                        else
                            ht = null;
                    }
                    // Base type will be used.
                    return tb.CreateType();
                }
                else if (hostType.IsClass)
                {
                    TypeBuilder tb = null;
                    if (bbt != null)
                    {
                        tb = _asm.mb.DefineType(
                            name,
                            System.Reflection.TypeAttributes.Public
                            | System.Reflection.TypeAttributes.SequentialLayout
                            | System.Reflection.TypeAttributes.Serializable,
                            bbt);
                    }
                    else
                    {
                        tb = _asm.mb.DefineType(
                            name,
                            System.Reflection.TypeAttributes.Public
                            | System.Reflection.TypeAttributes.SequentialLayout
                            | System.Reflection.TypeAttributes.Serializable);
                    }
                    _type_name_map[name] = tb.FullName;
                    Type ht = hostType;
                    while (ht != null)
                    {
                        var fields = ht.GetFields(
                            System.Reflection.BindingFlags.Instance
                            | System.Reflection.BindingFlags.NonPublic
                            | System.Reflection.BindingFlags.Public
                            | System.Reflection.BindingFlags.Static);
                        var fields2 = ht.GetFields();
                        foreach (var field in fields)
                        {
                            // For non-array type fields, just define the field as is.
                            // Recurse
                            Type elementType = CreateImplementationType(field.FieldType, declare_parent_chain,
                                declare_flatten_structure);
                            // If elementType is a reference or array, then we need to convert it to a IntPtr.
                            if (elementType.IsClass || elementType.IsArray)
                                elementType = typeof(System.IntPtr);
                            tb.DefineField(field.Name, elementType, System.Reflection.FieldAttributes.Public);
                        }
                        if (declare_flatten_structure)
                            ht = ht.BaseType;
                        else
                            ht = null;
                    }
                    // Base type will be used.
                    return tb.CreateType();
                }
                else return null;
            }
            catch (Exception e)
            {
                System.Console.WriteLine("Exception");
                System.Console.WriteLine(e);
                throw e;
            }
            finally
            {
            }
        }


        /// <summary>
        /// This method copies from a managed type into a blittable managed type.
        /// The type is converted from managed into a blittable managed type.
        /// </summary>
        /// <param name="from"></param>
        /// <param name="to"></param>
        public unsafe void DeepCopyToImplementation(object from, void* to_buffer)
        {
            // Copy object to a buffer.
            try
            {
                {
                    bool is_null = false;
                    try
                    {
                        if (from == null) is_null = true;
                        else if (from.Equals(null)) is_null = true;
                    }
                    catch (Exception e)
                    {
                    }
                    if (is_null)
                    {
                        throw new Exception("Unknown type of object.");
                    }
                }

                if (_allocated_objects.ContainsKey(from))
                {
                   // to = _allocated_objects[from];
                    return;
                }

                Type hostType = from.GetType();

                // Let's start with basic types.
                if (hostType.FullName.Equals("System.Object"))
                {
                    throw new Exception("Type is System.Object, but I don't know what to represent it as.");
                    Cp(to_buffer, from);
                    return;
                }
                if (hostType.FullName.Equals("System.Int16"))
                {
                    Cp(to_buffer, from);
                    return;
                }
                if (hostType.FullName.Equals("System.Int32"))
                {
                    Cp(to_buffer, from);
                    return;
                }
                if (hostType.FullName.Equals("System.Int64"))
                {
                    Cp(to_buffer, from);
                    return;
                }
                if (hostType.FullName.Equals("System.UInt16"))
                {
                    Cp(to_buffer, from);
                    return;
                }
                if (hostType.FullName.Equals("System.UInt32"))
                {
                    Cp(to_buffer, from);
                    return;
                }
                if (hostType.FullName.Equals("System.UInt64"))
                {
                    Cp(to_buffer, from);
                    return;
                }
                if (hostType.FullName.Equals("System.IntPtr"))
                {
                    Cp(to_buffer, from);
                    return;
                }

                // Map boolean into byte.
                if (hostType.FullName.Equals("System.Boolean"))
                {
                    bool v = (bool)from;
                    System.Byte v2 = (System.Byte)(v ? 1 : 0);
                    Cp(to_buffer, v2);
                    return;
                }

                // Map char into uint16.
                if (hostType.FullName.Equals("System.Char"))
                {
                    Char v = (Char)from;
                    System.UInt16 v2 = (System.UInt16)v;
                    Cp(to_buffer, v2);
                    return;
                }

                //// Declare inheritance types.
                //Type bbt = null;
                //if (declare_parent_chain)
                //{
                //    // First, declare base type
                //    Type bt = hostType.BaseType;
                //    if (bt != null && !bt.FullName.Equals("System.Object"))
                //    {
                //        bbt = CreateImplementationType(bt, declare_parent_chain, declare_flatten_structure);
                //    }
                //}

                String name = hostType.FullName;
                name = name.Replace("[", "\\[").Replace("]", "\\]");
                System.Reflection.TypeFilter tf = new System.Reflection.TypeFilter((Type t, object o) =>
                {
                    return t.FullName == name;
                });

                // Find blittable type for hostType.
                Type[] types = _asm.mb.FindTypes(tf, null);

                if (types.Length == 0) throw new Exception("Unknown type.");
                Type blittable_type = types[0];

                if (hostType.IsArray)
                {
                    // An array is represented as a pointer/length struct.
                    // The data in the array is contained in the buffer following the length.
                    // The buffer allocated must be big enough to contain all data. Use
                    // Buffer.SizeOf(array) to get the representation buffer size.
                    // If the element is an array or a class, a buffer is allocated for each
                    // element, and an intptr used in the array.
                    var a = (Array)from;
                    unsafe
                    {
                        IntPtr destIntPtr = IntPtr.Zero;

                        IntPtr srcIntPtr = (IntPtr)GCHandle.Alloc(from);
                        var blittable_element_type = CreateImplementationType(from.GetType().GetElementType());
                        var len = a.Length;
                        int bytes;
                        if (from.GetType().GetElementType().IsClass)
                        {
                            // We create a buffer for the class, and stuff a pointer in the array.
                            bytes = Marshal.SizeOf(typeof(IntPtr)) // Pointer
                                        + Marshal.SizeOf(typeof(Int32)) // length
                                        + Marshal.SizeOf(typeof(IntPtr)) * len; // elements
                        }
                        else
                        {
                            bytes = Marshal.SizeOf(typeof(IntPtr)) // Pointer
                                        + Marshal.SizeOf(typeof(Int32)) // length
                                        + Marshal.SizeOf(blittable_element_type) * len; // elements
                        }
                        destIntPtr = (IntPtr)to_buffer;
                        IntPtr df0 = new IntPtr((long)destIntPtr);
                        IntPtr df1 = new IntPtr(Marshal.SizeOf(typeof(IntPtr)) // Pointer
                                                + (long)destIntPtr);
                        IntPtr df2 = new IntPtr(Marshal.SizeOf(typeof(IntPtr)) // Pointer
                                                + Marshal.SizeOf(typeof(Int32)) // length
                                                + (long)destIntPtr);
                        System.Reflection.FieldInfo[] tfi = blittable_type.GetFields();
                        Cp(df0, df2); // Copy df2 to *df0
                        Cp(df1, len);
                        CopyToGPUBuffer(a, df2, CreateImplementationType(a.GetType().GetElementType()));
                    }
                    return;
                }

                if (IsStruct(hostType) || hostType.IsClass)
                {
                    Type f = from.GetType();
                    Type tr = blittable_type;
                    int size = Marshal.SizeOf(tr);
                    void* ip = to_buffer;

                    System.Reflection.FieldInfo[] ffi = f.GetFields();
                    System.Reflection.FieldInfo[] tfi = tr.GetFields();

                    foreach (System.Reflection.FieldInfo fi in ffi)
                    {
                        object field_value = fi.GetValue(from);
                        String na = fi.Name;
                        var tfield = tfi.Where(k => k.Name == fi.Name).FirstOrDefault();
                        if (tfield == null)
                            throw new ArgumentException("Field not found.");
                        // Copy.
                        var field_size = Marshal.SizeOf(tfield.FieldType);
                        // Note, if field is array, class, or struct, convert to IntPtr.
                        if (fi.FieldType.IsArray)
                        {
                            // Allocate a whole new buffer, copy to that, place buffer pointer into field at ip.
                            if (field_value != null)
                            {
                                Array ff = (Array) field_value;
                                var size2 = Marshal.SizeOf(typeof(IntPtr)) // Pointer
                                           + Marshal.SizeOf(typeof(Int32)) // length
                                           + Marshal.SizeOf(typeof(Int32)) * ff.Length; // Array values
                                IntPtr gp = New(size2);
                                DeepCopyToImplementation(field_value, gp);
                                DeepCopyToImplementation(gp, ip);
                                ip = (void*)((long)ip + Marshal.SizeOf(typeof(IntPtr)));
                            }
                            else
                            {
                                field_value = IntPtr.Zero;
                                DeepCopyToImplementation(field_value, ip);
                                ip = (void*)((long)ip + Marshal.SizeOf(typeof(IntPtr)));
                            }
                        }
                        else if (fi.FieldType.IsClass)
                        {
                            // Allocate a whole new buffer, copy to that, place buffer pointer into field at ip.
                            if (field_value != null)
                            {
                                var size2 = Marshal.SizeOf(tfield.FieldType);
                                IntPtr gp = New(size2);
                                DeepCopyToImplementation(field_value, gp);
                                DeepCopyToImplementation(gp, ip);
                                ip = (void*)((long)ip + Marshal.SizeOf(typeof(IntPtr)));
                            }
                            else
                            {
                                field_value = IntPtr.Zero;
                                DeepCopyToImplementation(field_value, ip);
                                ip = (void*)((long)ip + Marshal.SizeOf(typeof(IntPtr)));
                            }
                        }
                        else if (IsStruct(fi.FieldType))
                        {
                            throw new Exception("Whoops.");
                        }
                        else
                        {
                            DeepCopyToImplementation(field_value, ip);
                            ip = (void*)((long)ip + field_size);
                        }
                    }

                    return;
                }

                throw new Exception("Unknown type.");
            }
            catch (Exception e)
            {
                System.Console.WriteLine("Exception");
                System.Console.WriteLine(e);
                throw e;
            }
        }

        public void DeepCopyToImplementation(object from, IntPtr to_buffer)
        {
            unsafe
            {
                DeepCopyToImplementation(from, (void*)to_buffer);
            }
        }

        public unsafe void DeepCopyFromImplementation(IntPtr from, out object to, Type target_type)
        {
            Type t_type = target_type;
            Type f_type = CreateImplementationType(t_type);
            try
            {

                if (t_type.FullName.Equals("System.Object"))
                {
                    object o = Marshal.PtrToStructure<System.Object>(from);
                    to = o;
                    return;
                }
                if (t_type.FullName.Equals("System.Int16"))
                {
                    object o = Marshal.PtrToStructure<System.Int16>(from);
                    to = o;
                    return;
                }
                if (t_type.FullName.Equals("System.Int32"))
                {
                    object o = Marshal.PtrToStructure<System.Int32>(from);
                    to = o;
                    return;
                }
                if (t_type.FullName.Equals("System.Int64"))
                {
                    object o = Marshal.PtrToStructure<System.Int64>(from);
                    to = o;
                    return;
                }
                if (t_type.FullName.Equals("System.UInt16"))
                {
                    object o = Marshal.PtrToStructure<System.UInt16>(from);
                    to = o;
                    return;
                }
                if (t_type.FullName.Equals("System.UInt32"))
                {
                    object o = Marshal.PtrToStructure<System.UInt32>(from);
                    to = o;
                    return;
                }
                if (t_type.FullName.Equals("System.UInt64"))
                {
                    object o = Marshal.PtrToStructure<System.UInt64>(from);
                    to = o;
                    return;
                }
                if (t_type.FullName.Equals("System.IntPtr"))
                {
                    object o = Marshal.PtrToStructure<System.IntPtr>(from);
                    to = o;
                    return;
                }

                // Map boolean into byte.
                if (t_type.FullName.Equals("System.Boolean"))
                {
                    byte v = *(byte*)from;
                    to = (System.Boolean)(v == 1 ? true : false);
                    return;
                }

                // Map char into uint16.
                if (t_type.FullName.Equals("System.Char"))
                {
                    to = (System.Char)from;
                    return;
                }

                String name;
                System.Reflection.TypeFilter tf;
                Type bbt = null;

                //// Declare inheritance types.
                //if (declare_parent_chain)
                //{
                //    // First, declare base type
                //    Type bt = hostType.BaseType;
                //    if (bt != null && !bt.FullName.Equals("System.Object"))
                //    {
                //        bbt = CreateImplementationType(bt, declare_parent_chain, declare_flatten_structure);
                //    }
                //}

                name = f_type.FullName;
                name = name.Replace("[", "\\[").Replace("]", "\\]");
                tf = new System.Reflection.TypeFilter((Type t, object o) =>
                {
                    return t.FullName == name;
                });


                if (t_type.IsArray)
                {
                    // "from" is assumed to be a unmanaged buffer
                    // with three fields, "ptr", "len", "data".
                    byte * ptr = (byte*) from;
                    int len = *(int*)((long)(byte*)from + Marshal.SizeOf(typeof(IntPtr)));
                    IntPtr intptr_src = *(IntPtr*)((long)(byte*)from);
                    // For now, only one-dimension, given "len".
                    var to_array = Array.CreateInstance(t_type.GetElementType(), new int[1] { len });
                    CopyFromGPUBuffer(intptr_src, to_array, t_type.GetElementType());
                    to = to_array;
                    return;
                }

                if (IsStruct(t_type) || t_type.IsClass)
                {
                    IntPtr ip = from;
                    if (ip == IntPtr.Zero)
                    {
                        to = null;
                        return;
                    }

                    to = Activator.CreateInstance(t_type);

                    System.Reflection.FieldInfo[] ffi = f_type.GetFields();
                    System.Reflection.FieldInfo[] tfi = t_type.GetFields();


                    for (int i = 0; i < ffi.Length; ++i)
                    {
                        var ffield = ffi[i];
                        var tfield = tfi.Where(k => k.Name == ffield.Name).FirstOrDefault();
                        if (tfield == null) throw new ArgumentException("Field not found.");
                        // Note, special case all field types.
                        if (tfield.FieldType.IsArray)
                        {
                            int field_size = Marshal.SizeOf(typeof(IntPtr));
                            IntPtr fffff = (IntPtr)Marshal.PtrToStructure<IntPtr>(ip);
                            DeepCopyFromImplementation(fffff, out object tooo, tfield.FieldType);
                            tfield.SetValue(to, tooo);
                            ip = (IntPtr)((long)ip + field_size);
                        }
                        else if (tfield.FieldType.IsClass)
                        {
                            int field_size = Marshal.SizeOf(typeof(IntPtr));
                            IntPtr fffff = (IntPtr)Marshal.PtrToStructure<IntPtr>(ip);
                            DeepCopyFromImplementation(fffff, out object tooo, tfield.FieldType);
                            tfield.SetValue(to, tooo);
                            ip = (IntPtr)((long)ip + field_size);
                        }
                        else
                        {
                            int field_size = Marshal.SizeOf(ffield.FieldType);
                            DeepCopyFromImplementation(ip, out object tooo, tfield.FieldType);
                            tfield.SetValue(to, tooo);
                            ip = (IntPtr)((long)ip + field_size);
                        }
                    }

                    return;
                }

                throw new Exception("Unknown type.");
            }
            catch (Exception e)
            {
                System.Console.WriteLine("Exception");
                System.Console.WriteLine(e);
                throw e;
            }
        }

        public unsafe IntPtr CopyToGPUBuffer(Array from, IntPtr cpp_array, Type blittable_element_type)
        {
            IntPtr byte_ptr = cpp_array;
            int size_element = Marshal.SizeOf(blittable_element_type);
            for (int i = 0; i < from.Length; ++i)
            {
                DeepCopyToImplementation(from.GetValue(i), (byte*)byte_ptr);
                byte_ptr = new IntPtr((long)byte_ptr + size_element);
            }
            return cpp_array;
        }

        public IntPtr CopyFromGPUBuffer(IntPtr a, Array to, Type blittable_element_type)
        {
            int size_element = Marshal.SizeOf(blittable_element_type);
            IntPtr mem = a;
            for (int i = 0; i < to.Length; ++i)
            {
                // copy.
                object obj = Marshal.PtrToStructure(mem, blittable_element_type);
                object to_obj = to.GetValue(i);
                DeepCopyFromImplementation(mem, out to_obj, to.GetType().GetElementType());
                to.SetValue(to_obj, i);
                mem = new IntPtr((long)mem + size_element);
            }
            return a;
        }

        public static string OutputType(System.Type type)
        {
            if (type.IsArray)
            {
                return type.GetElementType().FullName + "[]";
            }
            if (type.IsValueType && !IsStruct(type))
            {
                return type.FullName;
            }
            StringBuilder sb = new StringBuilder();
            var fields = type.GetFields(
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Static);
            if (type.IsValueType && IsStruct(type))
                sb.Append("struct {").AppendLine();
            else if (type.IsClass)
                sb.Append("class {").AppendLine();
            foreach (var field in fields)
                sb.AppendFormat("{0} = {1}", field.Name, field.FieldType.Name).AppendLine();
            sb.Append("}").AppendLine();
            return sb.ToString();
        }

        /// <summary>
        /// Allocated a GPU managed buffer.
        /// Code based on https://www.codeproject.com/Articles/32125/Unmanaged-Arrays-in-C-No-Problem
        /// </summary>
        public IntPtr New(int bytes)
        {
            if (false)
            {
                // Let's try allocating a block of memory on the host. cuMemHostAlloc allocates bytesize
                // bytes of host memory that is page-locked and accessible to the device.
                // Note: cuMemHostAlloc and cuMemAllocHost seem to be almost identical except for the
                // third parameter to cuMemHostAlloc that is used for the type of memory allocation.
                var res = Cuda.cuMemHostAlloc(out IntPtr p, 10, (uint)Cuda.CU_MEMHOSTALLOC_DEVICEMAP);
                if (res == CUresult.CUDA_SUCCESS) System.Console.WriteLine("Worked.");
                else System.Console.WriteLine("Did not work.");
            }

            if (false)
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
                var size = bytes;
                var res = Cuda.cuMemAllocManaged(out IntPtr pointer, (uint)size, (uint)Swigged.Cuda.CUmemAttach_flags.CU_MEM_ATTACH_GLOBAL);
                if (res != CUresult.CUDA_SUCCESS) throw new Exception("cuMemAllocManged failed.");
                return pointer;
            }

            if (false)
            {
                return Marshal.AllocHGlobal(bytes);
            }
        }

        public unsafe IntPtr NewAndInit(Type t, int elementCount)
        {
            unsafe
            {
                if (!IsBlittable(t)) throw new Exception("Fucked!");
                int newSizeInBytes = Marshal.SizeOf(t) * elementCount;
                var result = Marshal.AllocHGlobal(newSizeInBytes);
                byte* newArrayPointer = (byte*) result.ToPointer();
                for (int i = 0; i < newSizeInBytes; i++)
                    *(newArrayPointer + i) = 0;
                return result;
            }
        }

        public void Free(IntPtr pointerToUnmanagedMemory)
        {
            Marshal.FreeHGlobal(pointerToUnmanagedMemory);
        }

        public unsafe void* Resize<T>(void* oldPointer, int newElementCount)
            where T : struct
        {
            return (Marshal.ReAllocHGlobal(new IntPtr(oldPointer),
                new IntPtr(Marshal.SizeOf(typeof(T)) * newElementCount))).ToPointer();
        }

        public void Cp(IntPtr destPtr, IntPtr srcPtr, int size)
        {
            unsafe
            {
                // srcPtr and destPtr are IntPtr's pointing to valid memory locations
                // size is the number of bytes to copy
                byte* src = (byte*)srcPtr;
                byte* dest = (byte*)destPtr;
                for (int i = 0; i < size; i++)
                {
                    dest[i] = src[i];
                }
            }
        }

        public void Cp(IntPtr destPtr, object src)
        {
            unsafe
            {
                Marshal.StructureToPtr(src, destPtr, false);
            }
        }

        public unsafe void Cp(void* destPtr, object src)
        {
            unsafe
            {
                Marshal.StructureToPtr(src, (IntPtr)destPtr, false);
            }
        }
    }
}
