using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;

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

        static Asm _asm = new Asm();

        public static bool IsStruct(System.Type t)
        {
            return t.IsValueType && !t.IsPrimitive && !t.IsEnum;
        }

        static Dictionary<string, string> _type_name_map = new Dictionary<string, string>();

        public static Type CreateImplementationType(Type hostType, bool declare_parent_chain = true, bool declare_flatten_structure = false)
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

        static Dictionary<object, IntPtr> _allocated_objects = new Dictionary<object, IntPtr>();

        /// <summary>
        /// This method copies from a managed type into a blittable managed type.
        /// The type is converted from managed into a blittable managed type.
        /// </summary>
        /// <param name="from"></param>
        /// <param name="to"></param>
        public static void DeepCopyToImplementation(object from, out object to)
        {
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
                        to = null;
                        return;
                    }
                }

                if (_allocated_objects.ContainsKey(from))
                {
                    to = _allocated_objects[from];
                    return;
                }

                Type hostType = from.GetType();

                // Let's start with basic types.
                if (hostType.FullName.Equals("System.Object"))
                {
                    to = from;
                    return;
                }
                if (hostType.FullName.Equals("System.Int16"))
                {
                    to = (System.Int16)from;
                    return;
                }
                if (hostType.FullName.Equals("System.Int32"))
                {
                    to = (System.Int32)from;
                    return;
                }
                if (hostType.FullName.Equals("System.Int64"))
                {
                    to = (System.Int64)from;
                    return;
                }
                if (hostType.FullName.Equals("System.UInt16"))
                {
                    to = (System.UInt16)from;
                    return;
                }
                if (hostType.FullName.Equals("System.UInt32"))
                {
                    to = (System.UInt32)from;
                    return;
                }
                if (hostType.FullName.Equals("System.UInt64"))
                {
                    to = (System.UInt64)from;
                    return;
                }
                if (hostType.FullName.Equals("System.IntPtr"))
                {
                    to = (System.IntPtr)from;
                    return;
                }

                // Map boolean into byte.
                if (hostType.FullName.Equals("System.Boolean"))
                {
                    bool v = (bool)from;
                    to = (System.Byte)(v ? 1 : 0);
                    return;
                }

                // Map char into uint16.
                if (hostType.FullName.Equals("System.Char"))
                {
                    to = (System.UInt16)from;
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
                    to = Activator.CreateInstance(blittable_type);
                    // Set fields.
                    var a = (Array)from;
                    unsafe
                    {
                        var intptr = New(blittable_type, a.Length);
                        var len = a.Length;

                        System.Reflection.FieldInfo[] tfi = blittable_type.GetFields();
                        foreach (System.Reflection.FieldInfo fi in tfi)
                        {
                            String na = fi.Name;
                            if (na == "ptr")
                            {
                                var tfield = tfi.Where(k => k.Name == fi.Name).FirstOrDefault();
                                if (tfield == null)
                                    throw new ArgumentException("Field not found.");
                                CopyToGPUBuffer(a, intptr,
                                    CreateImplementationType(a.GetType().GetElementType(), false, false));
                                tfield.SetValue(to, intptr);
                            }
                            if (na == "len")
                            {
                                var tfield = tfi.Where(k => k.Name == fi.Name).FirstOrDefault();
                                if (tfield == null)
                                    throw new ArgumentException("Field not found.");
                                tfield.SetValue(to, len);
                            }
                        }

                    }
                    return;
                }

                if (IsStruct(hostType) || hostType.IsClass)
                {
                    Type f = from.GetType();
                    Type tr = blittable_type;

                    to = Activator.CreateInstance(blittable_type);

                    System.Reflection.FieldInfo[] ffi = f.GetFields();
                    System.Reflection.FieldInfo[] tfi = tr.GetFields();

                    foreach (System.Reflection.FieldInfo fi in ffi)
                    {
                        object field_value = fi.GetValue(from);
                        String na = fi.Name;
                        // Convert.
                        object res;
                        DeepCopyToImplementation(field_value, out res);
                        // Copy.
                        var tfield = tfi.Where(k => k.Name == fi.Name).FirstOrDefault();
                        if (tfield == null)
                            throw new ArgumentException("Field not found.");
                        // Note, if field is array, class, or struct, convert to IntPtr.
                        if (fi.FieldType.IsArray || fi.FieldType.IsClass)
                        {
                            if (res != null)
                            {
                                var handle = GCHandle.Alloc(res, GCHandleType.Pinned);
                                var ptr = (IntPtr)handle;
                                System.Console.WriteLine(ptr.ToString("x"));
                                // Make sure it can be reversed.
                                GCHandle test = (GCHandle) ptr;
                                res = ptr;
                            }
                            else
                            {
                                res = IntPtr.Zero;
                            }
                        }
                        tfield.SetValue(to, res);
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

        public static void DeepCopyFromImplementation(object from, out object to, Type target_type)
        {
            Type f_type = from.GetType();
            Type t_type = target_type;
            try
            {
                if (f_type == typeof(IntPtr) && t_type != typeof(IntPtr))
                {
                    bool is_null = false;
                    try
                    {
                        if (from.Equals(IntPtr.Zero))
                        {
                            is_null = true;
                        }
                    }
                    catch (Exception e)
                    {
                    }
                    if (is_null)
                    {
                        to = null;
                        return;
                    }
                    // Get blittable type for object.
                    f_type = CreateImplementationType(t_type, false, false);
                    var xxx = (IntPtr)from;
                    System.Console.WriteLine(xxx.ToString("x"));
                    // Make sure it can be reversed.
                    GCHandle test = (GCHandle)xxx;
                    // Convert back to object.
                    GCHandle handle2 = (GCHandle)xxx;
                    from = handle2.Target;
                }

                if (_allocated_objects.ContainsKey(from))
                {
                    to = _allocated_objects[from];
                    return;
                }

                if (t_type.FullName.Equals("System.Object"))
                {
                    to = from;
                    return;
                }
                if (t_type.FullName.Equals("System.Int16"))
                {
                    to = (System.Int16)from;
                    return;
                }
                if (t_type.FullName.Equals("System.Int32"))
                {
                    to = (System.Int32)from;
                    return;
                }
                if (t_type.FullName.Equals("System.Int64"))
                {
                    to = (System.Int64)from;
                    return;
                }
                if (t_type.FullName.Equals("System.UInt16"))
                {
                    to = (System.UInt16)from;
                    return;
                }
                if (t_type.FullName.Equals("System.UInt32"))
                {
                    to = (System.UInt32)from;
                    return;
                }
                if (t_type.FullName.Equals("System.UInt64"))
                {
                    to = (System.UInt64)from;
                    return;
                }
                if (t_type.FullName.Equals("System.IntPtr"))
                {
                    to = (System.IntPtr)from;
                    return;
                }

                // Map boolean into byte.
                if (t_type.FullName.Equals("System.Boolean"))
                {
                    byte v = (byte)from;
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
                    unsafe
                    {
                        // "from" must be a structure with two fields, "ptr" and "len".
                        void* ptr = null;
                        var intptr_src = new IntPtr(ptr);
                        var len = 0;

                        System.Reflection.FieldInfo[] ffi = f_type.GetFields();
                        foreach (System.Reflection.FieldInfo fi in ffi)
                        {
                            String na = fi.Name;
                            if (na == "ptr")
                            {
                                var tfield = ffi.Where(k => k.Name == fi.Name).FirstOrDefault();
                                if (tfield == null)
                                    throw new ArgumentException("Field not found.");
                                intptr_src = (IntPtr)tfield.GetValue(from);
                            }
                            if (na == "len")
                            {
                                var tfield = ffi.Where(k => k.Name == fi.Name).FirstOrDefault();
                                if (tfield == null)
                                    throw new ArgumentException("Field not found.");
                                len = (int)tfield.GetValue(from);
                            }
                        }

                        var to_array = Array.CreateInstance(t_type.GetElementType(), new int[1] { len });
                        CopyFromGPUBuffer(intptr_src, to_array, t_type.GetElementType());
                        to = to_array;
                    }
                    return;
                }

                if (IsStruct(t_type) || t_type.IsClass)
                {
                    System.Reflection.FieldInfo[] ffi = f_type.GetFields();
                    to = Activator.CreateInstance(t_type);
                    System.Reflection.FieldInfo[] tfi = t_type.GetFields();
                    for (int i = 0; i < ffi.Length; ++i)
                    {
                        {
                            var fi = ffi[i];
                            var ti = tfi[i];
                            object field_value = fi.GetValue(from);
                            String na = fi.Name;
                            // Convert.
                            object res;
                            DeepCopyFromImplementation(field_value, out res, ti.FieldType);
                            var tfield = tfi.Where(k => k.Name == fi.Name).FirstOrDefault();
                            if (tfield == null)
                                throw new ArgumentException("Field not found.");
                            tfield.SetValue(to, res);
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

        public static IntPtr CopyToGPUBuffer(Array from, IntPtr cpp_array, Type blittable_element_type)
        {
            IntPtr byte_ptr = cpp_array;
            int size_element = Marshal.SizeOf(blittable_element_type);
            for (int i = 0; i < from.Length; ++i)
            {
                object obj = Activator.CreateInstance(blittable_element_type);
                DeepCopyToImplementation(from.GetValue(i), out obj);
                Marshal.StructureToPtr(obj, byte_ptr, false);
                byte_ptr = new IntPtr((long)byte_ptr + size_element);
            }
            return cpp_array;
        }

        public static IntPtr CopyFromGPUBuffer(IntPtr a, Array to, Type blittable_element_type)
        {
            int size_element = Marshal.SizeOf(blittable_element_type);
            IntPtr mem = a;
            for (int i = 0; i < to.Length; ++i)
            {
                // copy.
                object obj = Marshal.PtrToStructure(mem, blittable_element_type);
                object to_obj = to.GetValue(i);
                DeepCopyFromImplementation(obj, out to_obj, to.GetType().GetElementType());
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
        public static IntPtr New(Type t, int elementCount)
        {
            if (!IsBlittable(t)) throw new Exception("Fucked!");
            return Marshal.AllocHGlobal(Marshal.SizeOf(t) * elementCount);
        }

        public static unsafe void* NewAndInit(Type t, int elementCount)
        {
            if (!IsBlittable(t)) throw new Exception("Fucked!");
            int newSizeInBytes = Marshal.SizeOf(t) * elementCount;
            byte* newArrayPointer = (byte*)Marshal.AllocHGlobal(newSizeInBytes).ToPointer();
            for (int i = 0; i < newSizeInBytes; i++)
                *(newArrayPointer + i) = 0;
            return (void*)newArrayPointer;
        }

        public static unsafe void Free(void* pointerToUnmanagedMemory)
        {
            Marshal.FreeHGlobal(new IntPtr(pointerToUnmanagedMemory));
        }

        public static unsafe void* Resize<T>(void* oldPointer, int newElementCount)
            where T : struct
        {
            return (Marshal.ReAllocHGlobal(new IntPtr(oldPointer),
                new IntPtr(Marshal.SizeOf(typeof(T)) * newElementCount))).ToPointer();
        }

        public static unsafe void Cp(IntPtr destPtr, IntPtr srcPtr, int size)
        {
            unsafe
            {
                // srcPtr and destPtr are IntPtr's pointing to valid memory locations
                // size is the number of long (normally 4 bytes) to copy
                byte* src = (byte*)srcPtr;
                byte* dest = (byte*)destPtr;
                for (int i = 0; i < size / sizeof(byte); i++)
                {
                    dest[i] = src[i];
                }
            }
        }
    }
}
