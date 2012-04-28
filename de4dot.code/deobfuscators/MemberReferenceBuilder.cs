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
using Mono.Cecil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators {
	class MemberReferenceBuilder {
		ModuleDefinition module;
		Dictionary<TypeReferenceKey, TypeReference> createdTypes = new Dictionary<TypeReferenceKey, TypeReference>();

		public MemberReferenceBuilder(ModuleDefinition module) {
			this.module = module;
		}

		public IMetadataScope CorLib {
			get { return module.TypeSystem.Corlib; }
		}

		public TypeReference Object {
			get { return module.TypeSystem.Object; }
		}

		public TypeReference Void {
			get { return module.TypeSystem.Void; }
		}

		public TypeReference Boolean {
			get { return module.TypeSystem.Boolean; }
		}

		public TypeReference Char {
			get { return module.TypeSystem.Char; }
		}

		public TypeReference SByte {
			get { return module.TypeSystem.SByte; }
		}

		public TypeReference Byte {
			get { return module.TypeSystem.Byte; }
		}

		public TypeReference Int16 {
			get { return module.TypeSystem.Int16; }
		}

		public TypeReference UInt16 {
			get { return module.TypeSystem.UInt16; }
		}

		public TypeReference Int32 {
			get { return module.TypeSystem.Int32; }
		}

		public TypeReference UInt32 {
			get { return module.TypeSystem.UInt32; }
		}

		public TypeReference Int64 {
			get { return module.TypeSystem.Int64; }
		}

		public TypeReference UInt64 {
			get { return module.TypeSystem.UInt64; }
		}

		public TypeReference Single {
			get { return module.TypeSystem.Single; }
		}

		public TypeReference Double {
			get { return module.TypeSystem.Double; }
		}

		public TypeReference IntPtr {
			get { return module.TypeSystem.IntPtr; }
		}

		public TypeReference UIntPtr {
			get { return module.TypeSystem.UIntPtr; }
		}

		public TypeReference String {
			get { return module.TypeSystem.String; }
		}

		public TypeReference TypedReference {
			get { return module.TypeSystem.TypedReference; }
		}

		public TypeReference type(string ns, string name, string asmSimpleName) {
			return type(ns, name, findAssemblyReference(asmSimpleName));
		}

		public TypeReference type(string ns, string name) {
			return type(ns, name, CorLib);
		}

		public TypeReference type(string ns, string name, IMetadataScope asmRef) {
			return type(false, ns, name, asmRef);
		}

		public TypeReference valueType(string ns, string name, string asmSimpleName) {
			return valueType(ns, name, findAssemblyReference(asmSimpleName));
		}

		public TypeReference valueType(string ns, string name) {
			return valueType(ns, name, CorLib);
		}

		public TypeReference valueType(string ns, string name, IMetadataScope asmRef) {
			return type(true, ns, name, asmRef);
		}

		public TypeReference type(bool isValueType, string ns, string name, IMetadataScope asmRef) {
			var typeRef = new TypeReference(ns, name, module, asmRef);
			typeRef.IsValueType = isValueType;
			return add(isValueType, typeRef);
		}

		public TypeReference array(TypeReference typeRef) {
			return add(false, new ArrayType(typeRef));
		}

		TypeReference add(bool isValueType, TypeReference typeRef) {
			var key = new TypeReferenceKey(typeRef);
			TypeReference createdTypeRef;
			if (createdTypes.TryGetValue(key, out createdTypeRef)) {
				if (createdTypeRef.IsValueType != isValueType)
					throw new ApplicationException(string.Format("Type {0}'s IsValueType is not correct", createdTypeRef));
				return createdTypeRef;
			}
			createdTypes[key] = typeRef;
			return typeRef;
		}

		public MethodReference instanceMethod(string name, TypeReference declaringType, TypeReference returnType, params TypeReference[] args) {
			return method(true, name, declaringType, returnType, args);
		}

		public MethodReference staticMethod(string name, TypeReference declaringType, TypeReference returnType, params TypeReference[] args) {
			return method(false, name, declaringType, returnType, args);
		}

		public MethodReference method(bool isInstance, string name, TypeReference declaringType, TypeReference returnType, params TypeReference[] args) {
			var method = new MethodReference(name, returnType, declaringType);
			method.HasThis = isInstance;
			foreach (var arg in args)
				method.Parameters.Add(new ParameterDefinition(arg));
			return method;
		}

		AssemblyNameReference findAssemblyReference(string asmSimpleName) {
			AssemblyNameReference asmRef = null;
			foreach (var asmRef2 in findAssemblyReferences(asmSimpleName)) {
				if (asmRef == null || asmRef.Version == null || (asmRef2.Version != null && asmRef2.Version > asmRef.Version))
					asmRef = asmRef2;
			}
			if (asmRef == null)
				throw new ApplicationException(string.Format("Could not find assembly {0} in assembly references", asmSimpleName));
			return asmRef;
		}

		List<AssemblyNameReference> findAssemblyReferences(string asmSimpleName) {
			var asmRefs = new List<AssemblyNameReference>();
			foreach (var asmRef in module.AssemblyReferences) {
				if (asmRef.Name == asmSimpleName)
					asmRefs.Add(asmRef);
			}
			return asmRefs;
		}
	}
}
