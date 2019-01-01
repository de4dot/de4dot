/*
    Copyright (C) 2011-2015 de4dot@gmail.com

    This file is part of de4dot.

    de4dot is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    de4dot is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with de4dot.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace de4dot.code.deobfuscators.CodeFort {
	interface ICFType {
		Type Get(SerializedTypes serializedTypes);
	}

	static class ITypeCreator {
		public static ICFType Create(string name) => new StringType(name);
		public static ICFType Create(Type type) => new ExistingType(type);
	}

	class StringType : ICFType {
		readonly string name;
		public StringType(string name) => this.name = name;
		public Type Get(SerializedTypes serializedTypes) => serializedTypes.GetBuilderType(name);
		public override string ToString() => name;
	}

	class ExistingType : ICFType {
		readonly Type type;
		public ExistingType(Type type) => this.type = type;
		public Type Get(SerializedTypes serializedTypes) => type;
		public override string ToString() => type.ToString();
	}

	class GenericType : ICFType {
		ICFType type;
		ICFType[] genericArgs;

		public GenericType(string type, ICFType[] genericArgs)
			: this(ITypeCreator.Create(type), genericArgs) {
		}

		public GenericType(Type type, ICFType[] genericArgs)
			: this(ITypeCreator.Create(type), genericArgs) {
		}

		public GenericType(ICFType type, ICFType[] genericArgs) {
			this.type = type;
			this.genericArgs = genericArgs;
		}

		public Type Get(SerializedTypes serializedTypes) {
			var genericType = type.Get(serializedTypes);
			var types = new List<Type>(genericArgs.Length);
			foreach (var ga in genericArgs)
				types.Add(ga.Get(serializedTypes));
			return genericType.MakeGenericType(types.ToArray());
		}

		public override string ToString() {
			var sb = new StringBuilder();
			sb.Append(GetTypeName());
			if (genericArgs != null && genericArgs.Length > 0) {
				sb.Append('<');
				for (int i = 0; i < genericArgs.Length; i++) {
					if (i != 0)
						sb.Append(',');
					sb.Append(genericArgs[i].ToString());
				}
				sb.Append('>');
			}
			return sb.ToString();
		}

		string GetTypeName() {
			var typeName = type.ToString();
			int index = typeName.LastIndexOf('`');
			if (index < 0)
				return typeName;
			return typeName.Substring(0, index);
		}
	}

	class ListType : GenericType {
		public ListType(string type)
			: this(ITypeCreator.Create(type)) {
		}

		public ListType(Type type)
			: this(ITypeCreator.Create(type)) {
		}

		public ListType(ICFType type)
			: base(typeof(List<>), new ICFType[] { type }) {
		}
	}

	class TypeInfoBase {
		public readonly string name;
		public readonly string dcNamespace;
		public readonly string dcName;

		protected TypeInfoBase(string name, string dcNamespace, string dcName) {
			this.name = name;
			this.dcNamespace = dcNamespace;
			this.dcName = dcName;
		}

		public override string ToString() {
			if (!string.IsNullOrEmpty(dcNamespace))
				return $"{name} - {dcNamespace}.{dcName}";
			return $"{name} - {dcName}";
		}
	}

	class TypeInfo : TypeInfoBase {
		public readonly ICFType baseType;
		public readonly TypeFieldInfo[] fieldInfos;

		public TypeInfo(string name, string dcName, TypeFieldInfo[] fieldInfos)
			: this(name, "", dcName, fieldInfos) {
		}

		public TypeInfo(string name, string dcNamespace, string dcName, TypeFieldInfo[] fieldInfos)
			: this(ITypeCreator.Create(typeof(object)), name, dcNamespace, dcName, fieldInfos) {
		}

		public TypeInfo(ICFType baseType, string name, string dcName, TypeFieldInfo[] fieldInfos)
			: this(baseType, name, "", dcName, fieldInfos) {
		}

		public TypeInfo(ICFType baseType, string name, string dcNamespace, string dcName, TypeFieldInfo[] fieldInfos)
			: base(name, dcNamespace, dcName) {
			this.baseType = baseType;
			this.fieldInfos = fieldInfos;
		}
	}

	class TypeFieldInfo {
		public readonly ICFType type;
		public readonly string name;
		public readonly string dmName;

		public TypeFieldInfo(string type, string name, string dmName)
			: this(ITypeCreator.Create(type), name, dmName) {
		}

		public TypeFieldInfo(Type type, string name, string dmName)
			: this(ITypeCreator.Create(type), name, dmName) {
		}

		public TypeFieldInfo(ICFType type, string name, string dmName) {
			this.type = type;
			this.name = name;
			this.dmName = dmName;
		}

		public override string ToString() => $"{type} {name} - {dmName}";
	}

	class EnumInfo : TypeInfoBase {
		public readonly EnumFieldInfo[] fieldInfos;
		public readonly Type underlyingType = typeof(int);

		public EnumInfo(string name, string dcName, EnumFieldInfo[] fieldInfos)
			: this(name, "", dcName, fieldInfos) {
		}

		public EnumInfo(string name, string dcNamespace, string dcName, EnumFieldInfo[] fieldInfos)
			: base(name, dcNamespace, dcName) => this.fieldInfos = fieldInfos;
	}

	class EnumFieldInfo {
		public readonly int value;
		public readonly string name;
		public readonly string emValue;

		public EnumFieldInfo(int value, string name, string emValue) {
			this.value = value;
			this.name = name;
			this.emValue = emValue;
		}

		public override string ToString() => $"({value}) {name} - {emValue}";
	}

	class SerializedTypes {
		const string serializationAssemblyname = "System.Runtime.Serialization, Version=3.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089";

		static readonly EnumInfo[] enumInfos = new EnumInfo[] {
			new EnumInfo("InstructionType", "a", new EnumFieldInfo[] {
				new EnumFieldInfo(0, "BeginCatchBlock", "1"),
				new EnumFieldInfo(1, "BeginExceptFilterBlock", "2"),
				new EnumFieldInfo(2, "BeginExceptionBlock", "3"),
				new EnumFieldInfo(3, "BeginFaultBlock", "4"),
				new EnumFieldInfo(4, "BeginFinallyBlock", "5"),
				new EnumFieldInfo(5, "BeginScope", "6"),
				new EnumFieldInfo(6, "LocalVariable", "7"),
				new EnumFieldInfo(7, "Label", "8"),
				new EnumFieldInfo(8, "NoOperand", "A"),
				new EnumFieldInfo(9, "ByteOperand", "B"),
				new EnumFieldInfo(10, "ConstructorOperand", "C"),
				new EnumFieldInfo(11, "DoubleOperand", "D"),
				new EnumFieldInfo(12, "FieldOperand", "E"),
				new EnumFieldInfo(13, "SingleOperand", "F"),
				new EnumFieldInfo(14, "Int32Operand", "G"),
				new EnumFieldInfo(15, "TargetOperand", "H"),
				new EnumFieldInfo(16, "TargetsOperand", "I"),
				new EnumFieldInfo(17, "LocalOperand", "J"),
				new EnumFieldInfo(18, "Int64Operand", "K"),
				new EnumFieldInfo(19, "MethodOperand", "L"),
				new EnumFieldInfo(20, "SByteOperand", "M"),
				new EnumFieldInfo(21, "Int16Operand", "N"),
				new EnumFieldInfo(22, "StringOperand", "O"),
				new EnumFieldInfo(23, "TypeOperand", "P"),
				new EnumFieldInfo(24, "EndExceptionBlock", "b"),
				new EnumFieldInfo(25, "EndScope", "c"),
				new EnumFieldInfo(26, "MarkLabel", "d"),
				new EnumFieldInfo(27, "NotImpl1", "e"),
				new EnumFieldInfo(28, "ThrowException", "f"),
				new EnumFieldInfo(29, "NotImpl2", "g"),
			}),

			new EnumInfo("MemberTypes1", "l", new EnumFieldInfo[] {
				new EnumFieldInfo(0, "Constructor", "1"),
				new EnumFieldInfo(1, "TypeInitializer", "7"),
				new EnumFieldInfo(2, "Method", "2"),
				new EnumFieldInfo(3, "Field", "3"),
				new EnumFieldInfo(4, "Property", "4"),
				new EnumFieldInfo(5, "Event", "5"),
				new EnumFieldInfo(6, "NestedType", "6"),
			}),
		};

		static readonly TypeInfo[] typeInfos = new TypeInfo[] {
			new TypeInfo("AllTypes", "b", new TypeFieldInfo[] {
				new TypeFieldInfo(new ListType("TypeDef"), "Types", "T"),
			}),

			new TypeInfo("Instruction", "c", new TypeFieldInfo[] {
				new TypeFieldInfo(typeof(object), "Operand", "A"),
				new TypeFieldInfo("InstructionType", "InstructionType", "K"),
				new TypeFieldInfo(typeof(string), "OpCode", "O"),
			}),

			new TypeInfo("InstructionLabel", "d", new TypeFieldInfo[] {
			}),

			new TypeInfo("LocalVariable", "e", new TypeFieldInfo[] {
				new TypeFieldInfo(typeof(bool), "IsPinned", "P"),
				new TypeFieldInfo("TypeRef", "VariableType", "T"),
			}),

			new TypeInfo("TypeRef", "f", new TypeFieldInfo[] {
				new TypeFieldInfo("AssemblyRef", "AssemblyRef", "A"),
				new TypeFieldInfo(typeof(string), "ReflectionTypeFullName", "F"),
				new TypeFieldInfo(new ListType("TypeRef"), "GenericArguments", "G"),
				new TypeFieldInfo("TypeDef", "InternalBaseType", "I"),
				new TypeFieldInfo(typeof(int?), "ArrayDimensions", "V"),
			}),

			new TypeInfo("AssemblyRef", "g", new TypeFieldInfo[] {
				new TypeFieldInfo(typeof(string), "Name", "N"),
			}),

			new TypeInfo("MemberRef", "h", new TypeFieldInfo[] {
				new TypeFieldInfo(typeof(int), "BindingFlags", "B"),
				new TypeFieldInfo("TypeRef", "DeclaringType", "C"),
				new TypeFieldInfo("MemberTypes1", "MemberTypes1", "K"),
				new TypeFieldInfo("MemberDef", "MemberDef", "M"),
				new TypeFieldInfo(typeof(string), "Name", "N"),
				new TypeFieldInfo("TypeRef", "ReturnType", "T"),
			}),

			new TypeInfo(ITypeCreator.Create("MemberRef"), "MethodRef", "i", new TypeFieldInfo[] {
				new TypeFieldInfo(new ListType("ParameterRef"), "Parameters", "P"),
				new TypeFieldInfo(typeof(int), "CallingConventions", "V"),
			}),

			new TypeInfo("ParameterRef", "j", new TypeFieldInfo[] {
				new TypeFieldInfo("TypeRef", "TypeRef", "T"),
			}),

			new TypeInfo("TypeDef", "k", new TypeFieldInfo[] {
				new TypeFieldInfo(typeof(int), "TypeAttributes", "A"),
				new TypeFieldInfo("TypeRef", "BaseType", "B"),
				new TypeFieldInfo(new ListType("TypeDef"), "NestedTypes", "E"),
				new TypeFieldInfo(new ListType("MemberDef"), "Members", "M"),
				new TypeFieldInfo(typeof(string), "Name", "N"),
			}),

			new TypeInfo("MemberDef", "m", new TypeFieldInfo[] {
				new TypeFieldInfo(typeof(int), "Attributes", "B"),
				new TypeFieldInfo("MemberTypes1", "MemberTypes1", "K"),
				new TypeFieldInfo(typeof(string), "Name", "N"),
				new TypeFieldInfo("TypeRef", "Type", "T"),
			}),

			new TypeInfo(ITypeCreator.Create("MemberDef"), "PropertyDef", "n", new TypeFieldInfo[] {
				new TypeFieldInfo("MethodDef", "GetMethod", "G"),
				new TypeFieldInfo(new ListType("ParameterDef"), "ParameterTypes", "P"),
				new TypeFieldInfo("MethodDef", "SetMethod", "S"),
			}),

			new TypeInfo(ITypeCreator.Create("MemberDef"), "EventDef", "o", new TypeFieldInfo[] {
				new TypeFieldInfo("MethodDef", "AddOnMethod", "A"),
				new TypeFieldInfo("MethodDef", "RemoveOnMethod", "R"),
			}),

			new TypeInfo(ITypeCreator.Create("MemberDef"), "MethodDef", "p", new TypeFieldInfo[] {
				new TypeFieldInfo(new ListType("Instruction"), "Instructions", "A"),
				new TypeFieldInfo(typeof(CallingConventions), "CallingConventions", "C"),
				new TypeFieldInfo(typeof(MethodImplAttributes), "MethodImplAttributes", "I"),
				new TypeFieldInfo(new ListType("ParameterDef"), "ParameterTypes", "P"),
			}),

			new TypeInfo("ParameterDef", "q", new TypeFieldInfo[] {
				new TypeFieldInfo(typeof(string), "Name", "N"),
				new TypeFieldInfo("TypeRef", "TypeRef", "T"),
			}),
		};

		class PropertyInfoCreator {
			Type type;
			List<PropertyInfo> properties = new List<PropertyInfo>();
			List<object> values = new List<object>();

			public PropertyInfo[] Properties => properties.ToArray();
			public object[] Values => values.ToArray();
			public PropertyInfoCreator(Type type) => this.type = type;

			public void Add(string propertyName, object value) {
				var prop = type.GetProperty(propertyName);
				if (prop == null)
					throw new ApplicationException($"Could not find property {propertyName} (type {type})");
				properties.Add(prop);
				values.Add(value);
			}
		}

		ModuleBuilder moduleBuilder;
		Dictionary<string, EnumBuilder> enumBuilders = new Dictionary<string, EnumBuilder>(StringComparer.Ordinal);
		Dictionary<string, TypeBuilder> typeBuilders = new Dictionary<string, TypeBuilder>(StringComparer.Ordinal);
		Dictionary<string, Type> createdTypes = new Dictionary<string, Type>(StringComparer.Ordinal);

		public SerializedTypes(ModuleBuilder moduleBuilder) {
			this.moduleBuilder = moduleBuilder;
			CreateTypeBuilders();
			InitializeEnums();
			InitializeTypes();
			CreateTypes();
		}

		void CreateTypeBuilders() {
			foreach (var info in enumInfos)
				Add(info.name, moduleBuilder.DefineEnum(info.name, TypeAttributes.Public, info.underlyingType));
			foreach (var info in typeInfos)
				Add(info.name, moduleBuilder.DefineType(info.name, TypeAttributes.Public, info.baseType.Get(this)));
		}

		CustomAttributeBuilder CreateDataContractAttribute(string ns, string name, bool isReference) {
			var dcAttr = Type.GetType("System.Runtime.Serialization.DataContractAttribute," + serializationAssemblyname);
			var ctor = dcAttr.GetConstructor(Type.EmptyTypes);
			var propCreator = new PropertyInfoCreator(dcAttr);
			propCreator.Add("Namespace", ns);
			propCreator.Add("Name", name);
			propCreator.Add("IsReference", isReference);
			return new CustomAttributeBuilder(ctor, new object[0], propCreator.Properties, propCreator.Values);
		}

		CustomAttributeBuilder CreateEnumMemberAttribute(string value) {
			var emAttr = Type.GetType("System.Runtime.Serialization.EnumMemberAttribute," + serializationAssemblyname);
			var ctor = emAttr.GetConstructor(Type.EmptyTypes);
			var propCreator = new PropertyInfoCreator(emAttr);
			propCreator.Add("Value", value);
			return new CustomAttributeBuilder(ctor, new object[0], propCreator.Properties, propCreator.Values);
		}

		CustomAttributeBuilder CreateDataMemberAttribute(string name, bool emitDefaultValue) {
			var dmAttr = Type.GetType("System.Runtime.Serialization.DataMemberAttribute," + serializationAssemblyname);
			var ctor = dmAttr.GetConstructor(Type.EmptyTypes);
			var propCreator = new PropertyInfoCreator(dmAttr);
			propCreator.Add("Name", name);
			propCreator.Add("EmitDefaultValue", emitDefaultValue);
			return new CustomAttributeBuilder(ctor, new object[0], propCreator.Properties, propCreator.Values);
		}

		void Add(string name, EnumBuilder builder) {
			if (enumBuilders.ContainsKey(name))
				throw new ApplicationException($"Enum {name} already exists");
			enumBuilders[name] = builder;
		}

		void Add(string name, TypeBuilder builder) {
			if (typeBuilders.ContainsKey(name))
				throw new ApplicationException($"Type {name} already exists");
			typeBuilders[name] = builder;
		}

		void InitializeEnums() {
			foreach (var info in enumInfos) {
				var builder = enumBuilders[info.name];
				builder.SetCustomAttribute(CreateDataContractAttribute(info.dcNamespace, info.dcName, false));
				foreach (var fieldInfo in info.fieldInfos) {
					var fieldBuilder = builder.DefineLiteral(fieldInfo.name, fieldInfo.value);
					fieldBuilder.SetCustomAttribute(CreateEnumMemberAttribute(fieldInfo.emValue));
				}
			}
		}

		void InitializeTypes() {
			foreach (var info in typeInfos) {
				var builder = typeBuilders[info.name];
				builder.SetCustomAttribute(CreateDataContractAttribute(info.dcNamespace, info.dcName, true));
				foreach (var fieldInfo in info.fieldInfos) {
					var fieldBuilder = builder.DefineField(fieldInfo.name, fieldInfo.type.Get(this), FieldAttributes.Public);
					fieldBuilder.SetCustomAttribute(CreateDataMemberAttribute(fieldInfo.dmName, false));
				}
			}
		}

		void CreateTypes() {
			foreach (var info in enumInfos) {
				var builder = enumBuilders[info.name];
#if NETFRAMEWORK
				var type = builder.CreateType();
#else
				var type = builder.CreateTypeInfo();
#endif
				createdTypes[info.name] = type;
			}
			foreach (var info in typeInfos)
				createdTypes[info.name] = typeBuilders[info.name].CreateType();
			moduleBuilder = null;
			enumBuilders = null;
			typeBuilders = null;
		}

		public Type GetBuilderType(string name) {
			if (enumBuilders.TryGetValue(name, out var enumBuilder))
				return enumBuilder;

			if (typeBuilders.TryGetValue(name, out var typeBuilder))
				return typeBuilder;

			throw new ApplicationException($"Could not find type {name}");
		}

		Type GetType(string name) => createdTypes[name];

		public object Deserialize(byte[] data) {
			var serializerType = Type.GetType("System.Runtime.Serialization.DataContractSerializer," + serializationAssemblyname);
			if (serializerType == null)
				throw new ApplicationException("You need .NET 3.0 or later to decrypt the assembly");
			var quotasType = Type.GetType("System.Xml.XmlDictionaryReaderQuotas," + serializationAssemblyname);
			var serializerCtor = serializerType.GetConstructor(new Type[] { typeof(Type), typeof(IEnumerable<Type>) });
			var serializer = serializerCtor.Invoke(new object[] { GetType("AllTypes"), new Type[] {
				GetType("MemberTypes1"),
				GetType("Instruction"),
				GetType("InstructionType"),
				GetType("InstructionLabel"),
				GetType("LocalVariable"),
				GetType("ParameterDef"),
				GetType("TypeDef"),
				GetType("MemberDef"),
				GetType("MethodDef"),
				GetType("EventDef"),
				GetType("PropertyDef"),
				GetType("ParameterRef"),
				GetType("TypeRef"),
				GetType("MemberRef"),
				GetType("MethodRef"),
			}});

			var xmlReaderType = Type.GetType("System.Xml.XmlDictionaryReader," + serializationAssemblyname);
			var createReaderMethod = xmlReaderType.GetMethod("CreateBinaryReader", new Type[] { typeof(Stream), quotasType });
			var xmlReader = createReaderMethod.Invoke(null, new object[] {
				new MemoryStream(data),
				quotasType.InvokeMember("Max", BindingFlags.GetProperty, null, null, new object[0]),
			});
			using ((IDisposable)xmlReader) {
				var readObjectMethod = serializerType.GetMethod("ReadObject", new Type[] { xmlReaderType });
				return readObjectMethod.Invoke(serializer, new object[] { xmlReader });
			}
		}
	}
}
