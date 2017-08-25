using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;

namespace AllAboutBlittable
{

    public class Blittable
    {
        /// <summary>
        /// This code to check if a type is blittable. From http://aakinshin.net/blog/post/blittable/
        /// Original from https://stackoverflow.com/questions/10574645/the-fastest-way-to-check-if-a-type-is-blittable/31485271#31485271
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
        /// Data class used by CreateBlittableType in order to create a blittable type
        /// corresponding to a host type.
        /// </summary>
        class Data
        {
            public System.Reflection.AssemblyName assemblyName;
            public AssemblyBuilder ab;
            public ModuleBuilder mb;
            static int v = 1;

            public Data()
            {
                assemblyName = new System.Reflection.AssemblyName("DynamicAssembly" + v++);
                ab = AppDomain.CurrentDomain.DefineDynamicAssembly(
                    assemblyName,
                    AssemblyBuilderAccess.RunAndSave);
                mb = ab.DefineDynamicModule(assemblyName.Name, assemblyName.Name + ".dll");
            }
        }

        public static bool IsStruct(System.Type t)
        {
            return t.IsValueType && !t.IsPrimitive && !t.IsEnum;
        }

        private static Stack<bool> level = new Stack<bool>();
        static Data data = new Data();

        public static Type CreateBlittableType(Type hostType, bool declare_parent_chain, bool declare_flatten_structure)
        {
            level.Push(true);
            try
            {
                // Let's start with basic types.
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
                    if (bt != null && !bt.FullName.Equals("System.Object"))
                    {
                        bbt = CreateBlittableType(bt, declare_parent_chain, declare_flatten_structure);
                    }
                }

                name = hostType.FullName;
                tf = new System.Reflection.TypeFilter((Type t, object o) =>
                {
                    return t.FullName == name;
                });

                // Find if blittable type for hostType was already performed.
                Type[] types = data.mb.FindTypes(tf, null);

                // If blittable type was not created, create one with all fields corresponding
                // to that in host, with special attention to arrays.
                if (types.Length != 0 && level.Count == 1)
                    return types[0];


                if (hostType.IsArray)
                {
                    if (level.Count() > 1)
                        return typeof(System.IntPtr);

                    Type elementType = CreateBlittableType(hostType.GetElementType(), declare_parent_chain,
                        declare_flatten_structure);
                    object array_obj = Array.CreateInstance(elementType, 0);
                    Type array_type = array_obj.GetType();

                    // For arrays, convert into a struct with first field being a
                    // pointer, and the second field a length.

                    var tb = data.mb.DefineType(
                        array_type.FullName,
                        System.Reflection.TypeAttributes.Public
                        | System.Reflection.TypeAttributes.SequentialLayout
                        | System.Reflection.TypeAttributes.Serializable);

                    // Convert byte, int, etc., in host type to pointer in blittable type.
                    // With array, we need to also encode the length.
                    tb.DefineField("ptr", typeof(IntPtr), System.Reflection.FieldAttributes.Public);
                    tb.DefineField("len", typeof(Int32), System.Reflection.FieldAttributes.Public);

                    return tb.CreateType();
                }
                else if (IsStruct(hostType) || hostType.IsClass)
                {
                    if (level.Count() > 1)
                        return typeof(System.IntPtr);

                    TypeBuilder tb = null;
                    if (bbt != null)
                    {
                        tb = data.mb.DefineType(
                            name,
                            System.Reflection.TypeAttributes.Public
                            | System.Reflection.TypeAttributes.SequentialLayout
                            | System.Reflection.TypeAttributes.Serializable,
                            bbt);
                    }
                    else
                    {
                        tb = data.mb.DefineType(
                            name,
                            System.Reflection.TypeAttributes.Public
                            | System.Reflection.TypeAttributes.SequentialLayout
                            | System.Reflection.TypeAttributes.Serializable);
                    }
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
                            Type elementType = CreateBlittableType(field.FieldType, declare_parent_chain,
                                declare_flatten_structure);
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
                level.Pop();
            }
        }

        static Dictionary<object, IntPtr> _allocated_objects = new Dictionary<object, IntPtr>();

        static Dictionary<Type, Type> _original_to_blittable_type_map = new Dictionary<Type, Type>();

        /// <summary>
        /// This method copies from a managed type into a blittable managed type.
        /// The type is converted from managed into a blittable managed type.
        /// </summary>
        /// <param name="from"></param>
        /// <param name="to"></param>
        public static void CopyToManagedBlittableType(object from, out object to)
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
                //        bbt = CreateBlittableType(bt, declare_parent_chain, declare_flatten_structure);
                //    }
                //}

                String name = hostType.FullName;
                name = name.Replace("[", "\\[").Replace("]", "\\]");
                System.Reflection.TypeFilter tf = new System.Reflection.TypeFilter((Type t, object o) =>
                {
                    return t.FullName == name;
                });

                // Find blittable type for hostType.
                Type[] types = data.mb.FindTypes(tf, null);

                if (types.Length == 0) throw new Exception("Unknown type.");
                Type blittable_type = types[0];

                if (hostType.IsArray)
                {
                    to = Activator.CreateInstance(blittable_type);
                    // Set fields.
                    var a = (Array)from;
                    unsafe
                    {
                        var ptr = New(blittable_type, a.Length);
                        var intptr = new IntPtr(ptr);
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
                                CopyToNativeArray(a, intptr,
                                    CreateBlittableType(a.GetType().GetElementType(), false, false));
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
                        CopyToManagedBlittableType(field_value, out res);
                        // Copy.
                        var tfield = tfi.Where(k => k.Name == fi.Name).FirstOrDefault();
                        if (tfield == null)
                            throw new ArgumentException("Field not found.");
                        // Note, if field is array, class, or struct, convert to IntPtr.
                        if (fi.FieldType.IsArray || fi.FieldType.IsClass || IsStruct(fi.FieldType))
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

        public static void CopyFromManagedBlittableType(object from, out object to, Type target_type)
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
                    f_type = CreateBlittableType(t_type, false, false);
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
                //        bbt = CreateBlittableType(bt, declare_parent_chain, declare_flatten_structure);
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
                        CopyFromNativeArray(intptr_src, to_array, t_type.GetElementType());
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
                            CopyFromManagedBlittableType(field_value, out res, ti.FieldType);
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

        public static void CopyFromPtrToBlittable(IntPtr ptr, object blittable_object)
        {
            Marshal.PtrToStructure(ptr, blittable_object);
        }

        public static IntPtr CreateNativeArray(int length, int blittable_element_size)
        {
            IntPtr cpp_array = Marshal.AllocHGlobal(blittable_element_size * length);
            return cpp_array;
        }

        public static IntPtr CreateNativeArray(Array from, Type blittable_element_type)
        {
            int size_element = Marshal.SizeOf(blittable_element_type);
            IntPtr cpp_array = Marshal.AllocHGlobal(size_element * from.Length);
            return cpp_array;
        }

        public static IntPtr CopyToNativeArray(Array from, IntPtr cpp_array, Type blittable_element_type)
        {
            IntPtr byte_ptr = cpp_array;
            int size_element = Marshal.SizeOf(blittable_element_type);
            for (int i = 0; i < from.Length; ++i)
            {
                object obj = Activator.CreateInstance(blittable_element_type);
                CopyToManagedBlittableType(from.GetValue(i), out obj);
                Marshal.StructureToPtr(obj, byte_ptr, false);
                byte_ptr = new IntPtr((long)byte_ptr + size_element);
            }
            return cpp_array;
        }

        public static IntPtr CopyFromNativeArray(IntPtr a, Array to, Type blittable_element_type)
        {
            int size_element = Marshal.SizeOf(blittable_element_type);
            IntPtr mem = a;
            for (int i = 0; i < to.Length; ++i)
            {
                // copy.
                object obj = Marshal.PtrToStructure(mem, blittable_element_type);
                object to_obj = to.GetValue(i);
                CopyFromManagedBlittableType(obj, out to_obj, to.GetType().GetElementType());
                to.SetValue(to_obj, i);
                mem = new IntPtr((long)mem + size_element);
            }
            return a;
        }

        public static void OutputType(System.Type type)
        {
            if (type.IsArray)
            {
                System.Console.WriteLine(type.GetElementType().FullName + "[]");
                return;
            }
            if (type.IsValueType && !IsStruct(type))
            {
                System.Console.WriteLine(type.FullName);
                return;
            }
            var fields = type.GetFields(
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Static);
            if (type.IsValueType && IsStruct(type))
                Console.WriteLine("struct {");
            else if (type.IsClass)
                Console.WriteLine("class {");
            foreach (var field in fields)
            {
                Console.WriteLine("{0} = {1}", field.Name, field.FieldType.Name);
            }
            Console.WriteLine("}");
        }


        /// <summary>
        /// Code based on https://www.codeproject.com/Articles/32125/Unmanaged-Arrays-in-C-No-Problem
        /// </summary>
        public static unsafe void* New(Type t, int elementCount)
        {
            if (!IsBlittable(t)) throw new Exception("Fucked!");
            return Marshal.AllocHGlobal(Marshal.SizeOf(t) * elementCount).ToPointer();
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


        public static unsafe void Copy(IntPtr destPtr, IntPtr srcPtr, int size)
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
