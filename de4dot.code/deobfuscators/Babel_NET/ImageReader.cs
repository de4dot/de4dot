/*
    Copyright (C) 2011-2012 de4dot@gmail.com

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
using System.Runtime.InteropServices;
using System.IO;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Babel_NET {
	class ImageReader {
		static int METHODS_SIG			= 0x0000BEBA;
		static int METADATA_SIG			= 0x0100BEBA;
		static int METHOD_NAMES_SIG		= 0x0200BEBA;
		static int ASSEMBLY_NAMES_SIG	= 0x0201BEBA;
		static int TYPEREFS_SIG			= 0x0202BEBA;
		static int STRINGS_SIG			= 0x0203BEBA;

		enum TypeId : byte {
			TypeRef = 0,
			GenericInstance = 1,
			Pointer = 2,
			Array = 3,
			ByRef = 4,
		}

		ModuleDefinition module;
		BinaryReader reader;
		string[] strings;
		AssemblyNameReference[] assemblyNames;
		Dictionary<string, int> methodOffsets;
		List<TypeReference> typeReferences;
		MemberReferenceConverter memberReferenceConverter;
		IDeobfuscatorContext deobfuscatorContext;

		public ImageReader(IDeobfuscatorContext deobfuscatorContext, ModuleDefinition module, byte[] data) {
			this.deobfuscatorContext = deobfuscatorContext;
			this.module = module;
			this.reader = new BinaryReader(new MemoryStream(data));
			this.memberReferenceConverter = new MemberReferenceConverter(module);
		}

		public bool initialize() {
			if (reader.ReadInt32() != METHODS_SIG)
				return false;

			int metadataOffset = getMetadataOffset();
			if (metadataOffset < 0)
				return false;
			long pos = metadataOffset + 4;
			reader.BaseStream.Position = pos;
			int version = reader.ReadInt16();	// major, minor
			if (version == 0x0001) {
				initializeV10();
				return true;
			}

			reader.BaseStream.Position = pos;
			initializeV55();
			return true;
		}

		void initializeV10() {
			reader.ReadInt16();
			int methodNamesOffset = (int)reader.ReadInt64();
			int typeReferencesOffset = (int)reader.ReadInt64();
			int assemblyReferencesOffset = (int)reader.ReadInt64();
			int stringsOffset = (int)reader.ReadInt64();

			initializeStrings(stringsOffset);
			initializeAssemblyNames(assemblyReferencesOffset);
			initializeMethodNames(methodNamesOffset);
			initializeTypeReferences(typeReferencesOffset);
		}

		void initializeV55() {
			int methodNamesOffset = (int)reader.ReadInt64() ^ METADATA_SIG;
			int typeReferencesOffset = (int)reader.ReadInt64() ^ (METADATA_SIG << 1);
			int assemblyReferencesOffset = (int)reader.ReadInt64() ^ ((METADATA_SIG << 1) + 1);
			int stringsOffset = (int)reader.ReadInt64() ^ (((METADATA_SIG << 1) + 1) << 1);

			initializeStrings(stringsOffset);
			initializeAssemblyNames(assemblyReferencesOffset);
			initializeMethodNames(methodNamesOffset);
			initializeTypeReferences(typeReferencesOffset);
		}

		public void restore(string name, MethodDefinition method) {
			var babelMethod = getMethod(name);
			var body = method.Body;

			body.MaxStackSize = babelMethod.MaxStack;
			body.InitLocals = babelMethod.InitLocals;

			body.Variables.Clear();
			foreach (var local in babelMethod.Locals)
				body.Variables.Add(local);

			var toNewOperand = new Dictionary<object, object>();
			if (babelMethod.ThisParameter != null)
				toNewOperand[babelMethod.ThisParameter] = body.ThisParameter;
			for (int i = 0; i < method.Parameters.Count; i++)
				toNewOperand[babelMethod.Parameters[i]] = method.Parameters[i];

			body.Instructions.Clear();
			foreach (var instr in babelMethod.Instructions) {
				object newOperand;
				if (instr.Operand != null && toNewOperand.TryGetValue(instr.Operand, out newOperand))
					instr.Operand = newOperand;
				body.Instructions.Add(instr);
			}

			body.ExceptionHandlers.Clear();
			foreach (var eh in babelMethod.ExceptionHandlers)
				body.ExceptionHandlers.Add(eh);
		}

		BabelMethodDefinition getMethod(string name) {
			int offset = methodOffsets[name];
			methodOffsets.Remove(name);
			reader.BaseStream.Position = offset;
			return new MethodDefinitionReader(this, reader).read();
		}

		public string readString() {
			return strings[readVariableLengthInt32()];
		}

		public TypeReference readTypeReference() {
			return typeReferences[readVariableLengthInt32()];
		}

		public TypeReference[] readTypeReferences() {
			var refs = new TypeReference[readVariableLengthInt32()];
			for (int i = 0; i < refs.Length; i++)
				refs[i] = readTypeReference();
			return refs;
		}

		public FieldReference readFieldReference() {
			var name = readString();
			var declaringType = readTypeReference();

			var fields = getFields(resolve(declaringType), name);
			if (fields == null || fields.Count != 1) {
				throw new ApplicationException(string.Format("Couldn't find one field named '{0}' in type {1}",
								name,
								Utils.removeNewlines(declaringType)));
			}

			return memberReferenceConverter.convert(fields[0]);
		}

		static List<FieldDefinition> getFields(TypeDefinition type, string name) {
			if (type == null)
				return null;
			var fields = new List<FieldDefinition>();
			foreach (var field in type.Fields) {
				if (field.Name == name)
					fields.Add(field);
			}
			return fields;
		}

		public MethodReference readMethodReference() {
			var babelMethodRef = new MethodReferenceReader(this, reader).read();

			var method = getMethodReference(babelMethodRef);
			if (method == null) {
				throw new ApplicationException(string.Format("Could not find method '{0}' in type '{1}'",
							Utils.removeNewlines(babelMethodRef.Name),
							Utils.removeNewlines(babelMethodRef.DeclaringType)));
			}

			var git = babelMethodRef.DeclaringType as GenericInstanceType;
			if (git == null)
				return method;

			var newMethod = memberReferenceConverter.copy(method);
			newMethod.DeclaringType = babelMethodRef.DeclaringType;
			return newMethod;
		}

		MethodReference getMethodReference(BabelMethodreference babelMethodRef) {
			var declaringType = resolve(babelMethodRef.DeclaringType);
			if (declaringType == null)
				return null;

			var methods = getMethods(declaringType, babelMethodRef);
			if (methods.Count != 1) {
				throw new ApplicationException(string.Format("Couldn't find one method named '{0}' in type {1}",
								babelMethodRef.Name,
								Utils.removeNewlines(declaringType)));
			}

			return methods[0];
		}

		List<MethodReference> getMethods(TypeDefinition declaringType, BabelMethodreference babelMethodRef) {
			var methods = new List<MethodReference>();

			var git = babelMethodRef.DeclaringType as GenericInstanceType;
			IGenericInstance gim = babelMethodRef.IsGenericMethod ? babelMethodRef : null;
			foreach (var method in declaringType.Methods) {
				if (compareMethod(MethodReferenceInstance.make(method, git, gim), babelMethodRef)) {
					if (!babelMethodRef.IsGenericMethod)
						methods.Add(memberReferenceConverter.convert(method));
					else {
						var gim2 = new GenericInstanceMethod(memberReferenceConverter.convert(method));
						foreach (var arg in babelMethodRef.GenericArguments)
							gim2.GenericArguments.Add(arg);
						methods.Add(gim2);
					}
				}
			}

			return methods;
		}

		bool compareMethod(MethodReference method, BabelMethodreference babelMethodRef) {
			if (method.Parameters.Count != babelMethodRef.Parameters.Length)
				return false;
			if (method.Name != babelMethodRef.Name)
				return false;
			if (method.HasThis != babelMethodRef.HasThis)
				return false;
			if (method.GenericParameters.Count != babelMethodRef.GenericArguments.Length)
				return false;

			if (!MemberReferenceHelper.compareTypes(method.MethodReturnType.ReturnType, babelMethodRef.ReturnType))
				return false;

			for (int i = 0; i < babelMethodRef.Parameters.Length; i++) {
				if (!MemberReferenceHelper.compareTypes(method.Parameters[i].ParameterType, babelMethodRef.Parameters[i].ParameterType))
					return false;
			}

			return true;
		}

		TypeDefinition resolve(TypeReference type) {
			if (type is TypeDefinition)
				return (TypeDefinition)type;

			if (type.IsGenericInstance)
				type = ((GenericInstanceType)type).ElementType;

			if (type.Module == module && isModuleAssembly(type.Scope))
				return DotNetUtils.getType(module, type);

			return deobfuscatorContext.resolve(type);
		}

		public CallSite readCallSite() {
			var returnType = readTypeReference();
			var paramTypes = readTypeReferences();
			var callingConvention = (CallingConvention)reader.ReadInt32();

			var cs = new CallSite(returnType);
			foreach (var paramType in paramTypes)
				cs.Parameters.Add(new ParameterDefinition(paramType));
			cs.CallingConvention = convertCallingConvention(callingConvention);

			return cs;
		}

		static MethodCallingConvention convertCallingConvention(CallingConvention callingConvention) {
			switch (callingConvention) {
			case CallingConvention.Winapi:		return MethodCallingConvention.Default;
			case CallingConvention.Cdecl:		return MethodCallingConvention.C;
			case CallingConvention.StdCall:		return MethodCallingConvention.StdCall;
			case CallingConvention.ThisCall:	return MethodCallingConvention.ThisCall;
			case CallingConvention.FastCall:	return MethodCallingConvention.FastCall;
			default: throw new ApplicationException(string.Format("Unknown CallingConvention {0}", callingConvention));
			}
		}

		void initializeStrings(int headerOffset) {
			reader.BaseStream.Position = headerOffset;
			if (reader.ReadInt32() != STRINGS_SIG)
				throw new ApplicationException("Invalid strings sig");

			strings = new string[readVariableLengthInt32()];
			for (int i = 0; i < strings.Length; i++)
				strings[i] = reader.ReadString();
		}

		void initializeAssemblyNames(int headerOffset) {
			reader.BaseStream.Position = headerOffset;
			if (reader.ReadInt32() != ASSEMBLY_NAMES_SIG)
				throw new ApplicationException("Invalid assembly names sig");

			assemblyNames = new AssemblyNameReference[readVariableLengthInt32()];
			for (int i = 0; i < assemblyNames.Length; i++)
				assemblyNames[i] = getModuleAssemblyReference(AssemblyNameReference.Parse(readString()));
		}

		bool isModuleAssembly(IMetadataScope scope) {
			return DotNetUtils.isReferenceToModule(module, scope);
		}

		AssemblyNameReference getModuleAssemblyReference(AssemblyNameReference asmRef) {
			if (isModuleAssembly(asmRef))
				return module.Assembly.Name;
			return memberReferenceConverter.convert(asmRef);
		}

		void initializeMethodNames(int headerOffset) {
			reader.BaseStream.Position = headerOffset;
			if (reader.ReadInt32() != METHOD_NAMES_SIG)
				throw new ApplicationException("Invalid methods sig");

			int numMethods = readVariableLengthInt32();
			methodOffsets = new Dictionary<string, int>(numMethods, StringComparer.Ordinal);
			for (int i = 0; i < numMethods; i++) {
				var methodName = readString();
				methodOffsets[methodName] = readVariableLengthInt32();
			}
		}

		void initializeTypeReferences(int headerOffset) {
			reader.BaseStream.Position = headerOffset;
			if (reader.ReadInt32() != TYPEREFS_SIG)
				throw new ApplicationException("Invalid typerefs sig");

			int numTypeRefs = reader.ReadInt32();
			typeReferences = new List<TypeReference>(numTypeRefs + 1);
			typeReferences.Add(null);
			var genericArgFixes = new Dictionary<GenericInstanceType, List<int>>();
			for (int i = 0; i < numTypeRefs; i++) {
				TypeId typeId = (TypeId)reader.ReadByte();
				switch (typeId) {
				case TypeId.TypeRef:
					typeReferences.Add(readTypeRef());
					break;

				case TypeId.GenericInstance:
					List<int> genericArgs;
					var git = readGenericInstanceType(out genericArgs);
					typeReferences.Add(git);
					genericArgFixes[git] = genericArgs;
					break;

				case TypeId.Pointer:
					typeReferences.Add(readPointerType());
					break;

				case TypeId.Array:
					typeReferences.Add(readArrayType());
					break;

				case TypeId.ByRef:
					typeReferences.Add(readByReferenceType());
					break;

				default:
					throw new ApplicationException(string.Format("Unknown type id {0}", (int)typeId));
				}
			}

			foreach (var kv in genericArgFixes) {
				var git = kv.Key;
				foreach (var typeNum in kv.Value)
					git.GenericArguments.Add(typeReferences[typeNum]);
			}
		}

		TypeReference readTypeRef() {
			string ns, name;
			parseReflectionTypeName(readString(), out ns, out name);
			var asmRef = assemblyNames[readVariableLengthInt32()];
			var declaringType = readTypeReference();
			var typeReference = new TypeReference(ns, name, module, asmRef) {
				DeclaringType = declaringType,
			};
			typeReference.UpdateElementType();

			typeReference = memberReferenceConverter.convert(typeReference);
			typeReference.IsValueType = isValueType(typeReference);
			return typeReference;
		}

		bool isValueType(TypeReference typeRef) {
			var typeDef = typeRef as TypeDefinition;
			if (typeDef != null)
				return typeDef.IsValueType;

			if (typeRef.Module == module && isModuleAssembly(typeRef.Scope))
				typeDef = DotNetUtils.getType(module, typeRef);
			else 
				typeDef = resolve(typeRef);
			if (typeDef != null)
				return typeDef.IsValueType;

			Log.w("Could not determine whether type '{0}' is a value type", Utils.removeNewlines(typeRef));
			return false;	// Assume it's a reference type
		}

		static void parseReflectionTypeName(string fullName, out string ns, out string name) {
			int index = getLastChar(fullName, '.');
			if (index < 0) {
				ns = "";
				name = fullName;
			}
			else {
				ns = unEscape(fullName.Substring(0, index));
				name = fullName.Substring(index + 1);
			}

			index = getLastChar(name, '+');
			if (index < 0)
				name = unEscape(name);
			else {
				ns = "";
				name = unEscape(name.Substring(index + 1));
			}
		}

		static int getLastChar(string name, char c) {
			if (string.IsNullOrEmpty(name))
				return -1;
			int index = name.Length - 1;
			while (true) {
				index = name.LastIndexOf(c, index);
				if (index < 0)
					return -1;
				if (index == 0)
					return index;
				if (name[index - 1] != '\\')
					return index;
				index--;
			}
		}

		static string unEscape(string s) {
			var sb = new StringBuilder(s.Length);
			for (int i = 0; i < s.Length; i++) {
				if (s[i] == '\\' && i + 1 < s.Length)
					i++;
				sb.Append(s[i]);
			}
			return sb.ToString();
		}

		GenericInstanceType readGenericInstanceType(out List<int> genericArgs) {
			var git = new GenericInstanceType(readTypeReference());
			int numArgs = readVariableLengthInt32();
			genericArgs = new List<int>(numArgs);
			for (int i = 0; i < numArgs; i++)
				genericArgs.Add(readVariableLengthInt32());
			return git;
		}

		PointerType readPointerType() {
			return new PointerType(readTypeReference());
		}

		ArrayType readArrayType() {
			return new ArrayType(readTypeReference(), readVariableLengthInt32());
		}

		ByReferenceType readByReferenceType() {
			return new ByReferenceType(readTypeReference());
		}

		public int readVariableLengthInt32() {
			return DeobUtils.readVariableLengthInt32(reader);
		}

		int getMetadataOffset() {
			reader.BaseStream.Position = reader.BaseStream.Length - 4;
			for (int i = 0; i < 30; i++) {
				if (reader.ReadInt32() == METADATA_SIG)
					return (int)reader.BaseStream.Position - 4;
				reader.BaseStream.Position -= 8;
			}
			return -1;
		}
	}
}
