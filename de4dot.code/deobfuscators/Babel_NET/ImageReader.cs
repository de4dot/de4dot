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
using System.Text;
using dnlib.IO;
using dnlib.DotNet;
using de4dot.blocks;

using CR = System.Runtime.InteropServices;
using DR = dnlib.DotNet;

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

		ModuleDefMD module;
		internal DataReader reader;
		string[] strings;
		AssemblyRef[] assemblyNames;
		Dictionary<string, int> methodOffsets;
		List<TypeSig> typeRefs;
		MemberRefConverter memberRefConverter;

		public ImageReader(IDeobfuscatorContext deobfuscatorContext, ModuleDefMD module, byte[] data) {
			this.module = module;
			reader = ByteArrayDataReaderFactory.CreateReader(data);
			memberRefConverter = new MemberRefConverter(module);
		}

		public bool Initialize() {
			if (reader.ReadInt32() != METHODS_SIG)
				return false;

			int metadataOffset = GetMetadataOffset();
			if (metadataOffset < 0)
				return false;
			uint pos = (uint)metadataOffset + 4;
			reader.Position = pos;
			int version = reader.ReadInt16();	// major, minor
			if (version == 0x0001) {
				InitializeV10();
				return true;
			}

			reader.Position = pos;
			InitializeV55();
			return true;
		}

		void InitializeV10() {
			reader.ReadInt16();
			int methodNamesOffset = (int)reader.ReadInt64();
			int typeRefsOffset = (int)reader.ReadInt64();
			int assemblyRefsOffset = (int)reader.ReadInt64();
			int stringsOffset = (int)reader.ReadInt64();

			InitializeStrings(stringsOffset);
			InitializeAssemblyNames(assemblyRefsOffset);
			InitializeMethodNames(methodNamesOffset);
			InitializeTypeRefs(typeRefsOffset);
		}

		void InitializeV55() {
			int methodNamesOffset = (int)reader.ReadInt64() ^ METADATA_SIG;
			int typeRefsOffset = (int)reader.ReadInt64() ^ (METADATA_SIG << 1);
			int assemblyRefsOffset = (int)reader.ReadInt64() ^ ((METADATA_SIG << 1) + 1);
			int stringsOffset = (int)reader.ReadInt64() ^ (((METADATA_SIG << 1) + 1) << 1);

			InitializeStrings(stringsOffset);
			InitializeAssemblyNames(assemblyRefsOffset);
			InitializeMethodNames(methodNamesOffset);
			InitializeTypeRefs(typeRefsOffset);
		}

		public void Restore(string name, MethodDef method) {
			var babelMethod = GetMethod(name);
			var body = method.Body;

			body.MaxStack = babelMethod.MaxStack;
			body.InitLocals = babelMethod.InitLocals;

			body.Variables.Clear();
			foreach (var local in babelMethod.Locals)
				body.Variables.Add(local);

			var toNewOperand = new Dictionary<object, object>();
			if (babelMethod.ThisParameter != null)
				toNewOperand[babelMethod.ThisParameter] = method.Parameters[0];
			for (int i = 0; i < babelMethod.Parameters.Length; i++)
				toNewOperand[babelMethod.Parameters[i]] = method.Parameters[i + method.Parameters.MethodSigIndexBase];

			body.Instructions.Clear();
			foreach (var instr in babelMethod.Instructions) {
				if (instr.Operand != null && toNewOperand.TryGetValue(instr.Operand, out object newOperand))
					instr.Operand = newOperand;
				body.Instructions.Add(instr);
			}

			body.ExceptionHandlers.Clear();
			foreach (var eh in babelMethod.ExceptionHandlers)
				body.ExceptionHandlers.Add(eh);
		}

		BabelMethodDef GetMethod(string name) {
			int offset = methodOffsets[name];
			methodOffsets.Remove(name);
			reader.Position = (uint)offset;
			return new MethodDefReader(this).Read();
		}

		public string ReadString() => strings[ReadVariableLengthInt32()];
		public TypeSig ReadTypeSig() => typeRefs[ReadVariableLengthInt32()];

		public TypeSig[] ReadTypeSigs() {
			var refs = new TypeSig[ReadVariableLengthInt32()];
			for (int i = 0; i < refs.Length; i++)
				refs[i] = ReadTypeSig();
			return refs;
		}

		public IField ReadFieldRef() {
			var name = ReadString();
			var declaringType = ReadTypeSig();

			var fields = GetFields(Resolve(declaringType), name);
			if (fields == null || fields.Count != 1)
				throw new ApplicationException($"Couldn't find one field named '{name}' in type {Utils.RemoveNewlines(declaringType)}");

			return memberRefConverter.Convert(fields[0]);
		}

		static List<FieldDef> GetFields(TypeDef type, string name) {
			if (type == null)
				return null;
			return new List<FieldDef>(type.FindFields(name));
		}

		public IMethod ReadMethodRef() {
			var babelMethodRef = new MethodRefReader(this).Read();

			var method = GetMethodRef(babelMethodRef);
			if (method == null)
				throw new ApplicationException($"Could not find method '{Utils.RemoveNewlines(babelMethodRef.Name)}' in type '{Utils.RemoveNewlines(babelMethodRef.DeclaringType)}'");

			var git = babelMethodRef.DeclaringType.ToGenericInstSig();
			if (git == null)
				return method;

			var mr = new MemberRefUser(module, method.Name, method.MethodSig.Clone(), babelMethodRef.DeclaringType.ToTypeDefOrRef());
			return module.UpdateRowId(mr);
		}

		IMethod GetMethodRef(BabelMethodreference babelMethodRef) {
			var declaringType = Resolve(babelMethodRef.DeclaringType);
			if (declaringType == null)
				return null;

			var methods = GetMethods(declaringType, babelMethodRef);
			if (methods.Count != 1)
				throw new ApplicationException($"Couldn't find one method named '{babelMethodRef.Name}' in type {Utils.RemoveNewlines(declaringType)}");

			return methods[0];
		}

		List<IMethod> GetMethods(TypeDef declaringType, BabelMethodreference babelMethodRef) {
			var methods = new List<IMethod>();

			var gis = babelMethodRef.DeclaringType as GenericInstSig;
			var gim = babelMethodRef.GenericArguments;
			foreach (var method in declaringType.Methods) {
				if (CompareMethod(GenericArgsSubstitutor.Create(method, gis, gim), babelMethodRef)) {
					if (!babelMethodRef.IsGenericMethod)
						methods.Add(memberRefConverter.Convert(method));
					else {
						var gim2 = new GenericInstMethodSig(babelMethodRef.GenericArguments);
						var ms = module.UpdateRowId(new MethodSpecUser(memberRefConverter.Convert(method), gim2));
						methods.Add(ms);
					}
				}
			}

			return methods;
		}

		bool CompareMethod(IMethod method, BabelMethodreference babelMethodRef) {
			var sig = method.MethodSig;
			if (sig.Params.Count != babelMethodRef.Parameters.Length)
				return false;
			if (method.Name != babelMethodRef.Name)
				return false;
			if (sig.HasThis != babelMethodRef.HasThis)
				return false;
			if (sig.GenParamCount != babelMethodRef.GenericArguments.Length)
				return false;

			if (!new SigComparer().Equals(sig.RetType, babelMethodRef.ReturnType))
				return false;

			for (int i = 0; i < babelMethodRef.Parameters.Length; i++) {
				if (!new SigComparer().Equals(sig.Params[i], babelMethodRef.Parameters[i].Type))
					return false;
			}

			return true;
		}

		TypeDef Resolve(TypeSig type) {
			type = type.RemovePinnedAndModifiers();

			if (type is GenericInstSig gis)
				type = gis.GenericType;

			var tdrs = type as TypeDefOrRefSig;
			if (tdrs == null)
				return null;

			var td = tdrs.TypeDef;
			if (td != null)
				return td;

			var tr = tdrs.TypeRef;
			if (tr != null)
				return tr.Resolve();

			return null;
		}

		public MethodSig ReadCallSite() {
			var returnType = ReadTypeSig();
			var paramTypes = ReadTypeSigs();
			var callingConvention = (CR.CallingConvention)reader.ReadInt32();

			return new MethodSig(ConvertCallingConvention(callingConvention), 0, returnType, paramTypes);
		}

		static DR.CallingConvention ConvertCallingConvention(CR.CallingConvention callingConvention) {
			switch (callingConvention) {
			case CR.CallingConvention.Winapi:	return DR.CallingConvention.Default;
			case CR.CallingConvention.Cdecl:	return DR.CallingConvention.C;
			case CR.CallingConvention.StdCall:	return DR.CallingConvention.StdCall;
			case CR.CallingConvention.ThisCall:	return DR.CallingConvention.ThisCall;
			case CR.CallingConvention.FastCall:	return DR.CallingConvention.FastCall;
			default: throw new ApplicationException($"Unknown CallingConvention {callingConvention}");
			}
		}

		void InitializeStrings(int headerOffset) {
			reader.Position = (uint)headerOffset;
			if (reader.ReadInt32() != STRINGS_SIG)
				throw new ApplicationException("Invalid strings sig");

			strings = new string[ReadVariableLengthInt32()];
			for (int i = 0; i < strings.Length; i++)
				strings[i] = reader.ReadSerializedString();
		}

		void InitializeAssemblyNames(int headerOffset) {
			reader.Position = (uint)headerOffset;
			if (reader.ReadInt32() != ASSEMBLY_NAMES_SIG)
				throw new ApplicationException("Invalid assembly names sig");

			assemblyNames = new AssemblyRef[ReadVariableLengthInt32()];
			for (int i = 0; i < assemblyNames.Length; i++)
				assemblyNames[i] = module.UpdateRowId(new AssemblyRefUser(new AssemblyNameInfo(ReadString())));
		}

		void InitializeMethodNames(int headerOffset) {
			reader.Position = (uint)headerOffset;
			if (reader.ReadInt32() != METHOD_NAMES_SIG)
				throw new ApplicationException("Invalid methods sig");

			int numMethods = ReadVariableLengthInt32();
			methodOffsets = new Dictionary<string, int>(numMethods, StringComparer.Ordinal);
			for (int i = 0; i < numMethods; i++) {
				var methodName = ReadString();
				methodOffsets[methodName] = ReadVariableLengthInt32();
			}
		}

		void InitializeTypeRefs(int headerOffset) {
			reader.Position = (uint)headerOffset;
			if (reader.ReadInt32() != TYPEREFS_SIG)
				throw new ApplicationException("Invalid typerefs sig");

			int numTypeRefs = reader.ReadInt32();
			typeRefs = new List<TypeSig>(numTypeRefs + 1);
			typeRefs.Add(null);
			var genericArgFixes = new Dictionary<GenericInstSig, List<int>>();
			for (int i = 0; i < numTypeRefs; i++) {
				var typeId = (TypeId)reader.ReadByte();
				switch (typeId) {
				case TypeId.TypeRef:
					typeRefs.Add(ReadTypeRef());
					break;

				case TypeId.GenericInstance:
					List<int> genericArgs;
					var git = ReadGenericInstanceType(out genericArgs);
					typeRefs.Add(git);
					genericArgFixes[git] = genericArgs;
					break;

				case TypeId.Pointer:
					typeRefs.Add(ReadPointerType());
					break;

				case TypeId.Array:
					typeRefs.Add(ReadArrayType());
					break;

				case TypeId.ByRef:
					typeRefs.Add(ReadByRefType());
					break;

				default:
					throw new ApplicationException($"Unknown type id {(int)typeId}");
				}
			}

			foreach (var kv in genericArgFixes) {
				var git = kv.Key;
				foreach (var typeNum in kv.Value)
					git.GenericArguments.Add(typeRefs[typeNum]);
			}
		}

		TypeSig ReadTypeRef() {
			ParseReflectionTypeName(ReadString(), out string ns, out string name);
			var asmRef = assemblyNames[ReadVariableLengthInt32()];
			var declaringType = ReadTypeSig();
			var typeRef = new TypeRefUser(module, ns, name);
			if (declaringType != null)
				typeRef.ResolutionScope = GetTypeRef(declaringType);
			else
				typeRef.ResolutionScope = asmRef;

			return memberRefConverter.Convert(typeRef);
		}

		TypeRef GetTypeRef(TypeSig type) {
			var tdr = type as TypeDefOrRefSig;
			if (tdr == null)
				throw new ApplicationException("Not a type ref");
			if (tdr.TypeRef != null)
				return tdr.TypeRef;
			var td = tdr.TypeDef;
			if (td != null)
				return new Importer(module).Import(td) as TypeRef;
			throw new ApplicationException("Not a type ref");
		}

		static void ParseReflectionTypeName(string fullName, out string ns, out string name) {
			int index = GetLastChar(fullName, '.');
			if (index < 0) {
				ns = "";
				name = fullName;
			}
			else {
				ns = UnEscape(fullName.Substring(0, index));
				name = fullName.Substring(index + 1);
			}

			index = GetLastChar(name, '+');
			if (index < 0)
				name = UnEscape(name);
			else {
				ns = "";
				name = UnEscape(name.Substring(index + 1));
			}
		}

		static int GetLastChar(string name, char c) {
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

		static string UnEscape(string s) {
			var sb = new StringBuilder(s.Length);
			for (int i = 0; i < s.Length; i++) {
				if (s[i] == '\\' && i + 1 < s.Length)
					i++;
				sb.Append(s[i]);
			}
			return sb.ToString();
		}

		GenericInstSig ReadGenericInstanceType(out List<int> genericArgs) {
			var git = new GenericInstSig(ReadTypeSig() as ClassOrValueTypeSig);
			int numArgs = ReadVariableLengthInt32();
			genericArgs = new List<int>(numArgs);
			for (int i = 0; i < numArgs; i++)
				genericArgs.Add(ReadVariableLengthInt32());
			return git;
		}

		PtrSig ReadPointerType() => new PtrSig(ReadTypeSig());

		TypeSig ReadArrayType() {
			var typeSig = ReadTypeSig();
			int rank = ReadVariableLengthInt32();
			if (rank == 1)
				return new SZArraySig(typeSig);
			return new ArraySig(typeSig, rank);
		}

		ByRefSig ReadByRefType() => new ByRefSig(ReadTypeSig());

		public uint ReadVariableLengthUInt32() => reader.ReadCompressedUInt32();
		public int ReadVariableLengthInt32() => (int)reader.ReadCompressedUInt32();

		int GetMetadataOffset() {
			reader.Position = reader.Length - 4;
			for (int i = 0; i < 30; i++) {
				if (reader.ReadInt32() == METADATA_SIG)
					return (int)reader.Position - 4;
				reader.Position -= 8;
			}
			return -1;
		}
	}
}
