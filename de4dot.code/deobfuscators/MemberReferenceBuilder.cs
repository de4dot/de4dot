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
using dnlib.DotNet;

namespace de4dot.code.deobfuscators {
	public class MemberRefBuilder {
		ModuleDefMD module;
		Dictionary<TypeSig, TypeSig> createdTypes = new Dictionary<TypeSig, TypeSig>(TypeEqualityComparer.Instance);

		public MemberRefBuilder(ModuleDefMD module) => this.module = module;

		public AssemblyRef CorLib => module.CorLibTypes.AssemblyRef;
		public CorLibTypeSig Object => module.CorLibTypes.Object;
		public CorLibTypeSig Void => module.CorLibTypes.Void;
		public CorLibTypeSig Boolean => module.CorLibTypes.Boolean;
		public CorLibTypeSig Char => module.CorLibTypes.Char;
		public CorLibTypeSig SByte => module.CorLibTypes.SByte;
		public CorLibTypeSig Byte => module.CorLibTypes.Byte;
		public CorLibTypeSig Int16 => module.CorLibTypes.Int16;
		public CorLibTypeSig UInt16 => module.CorLibTypes.UInt16;
		public CorLibTypeSig Int32 => module.CorLibTypes.Int32;
		public CorLibTypeSig UInt32 => module.CorLibTypes.UInt32;
		public CorLibTypeSig Int64 => module.CorLibTypes.Int64;
		public CorLibTypeSig UInt64 => module.CorLibTypes.UInt64;
		public CorLibTypeSig Single => module.CorLibTypes.Single;
		public CorLibTypeSig Double => module.CorLibTypes.Double;
		public CorLibTypeSig IntPtr => module.CorLibTypes.IntPtr;
		public CorLibTypeSig UIntPtr => module.CorLibTypes.UIntPtr;
		public CorLibTypeSig String => module.CorLibTypes.String;
		public CorLibTypeSig TypedReference => module.CorLibTypes.TypedReference;

		public ClassSig Type(string ns, string name, string asmSimpleName) => Type(ns, name, FindAssemblyRef(asmSimpleName));
		public ClassSig Type(string ns, string name) => Type(ns, name, CorLib);
		public ClassSig Type(string ns, string name, AssemblyRef asmRef) => (ClassSig)Type(false, ns, name, asmRef);
		public ValueTypeSig ValueType(string ns, string name, string asmSimpleName) => ValueType(ns, name, FindAssemblyRef(asmSimpleName));
		public ValueTypeSig ValueType(string ns, string name) => ValueType(ns, name, CorLib);
		public ValueTypeSig ValueType(string ns, string name, AssemblyRef asmRef) => (ValueTypeSig)Type(true, ns, name, asmRef);

		public ClassOrValueTypeSig Type(bool isValueType, string ns, string name, IResolutionScope resolutionScope) {
			var typeRef = module.UpdateRowId(new TypeRefUser(module, ns, name, resolutionScope));
			ClassOrValueTypeSig type;
			if (isValueType)
				type = new ValueTypeSig(typeRef);
			else
				type = new ClassSig(typeRef);
			return (ClassOrValueTypeSig)Add(type);
		}

		public SZArraySig Array(TypeSig typeRef) => (SZArraySig)Add(new SZArraySig(typeRef));

		TypeSig Add(TypeSig typeRef) {
			if (createdTypes.TryGetValue(typeRef, out var createdTypeRef)) {
				if (createdTypeRef.ElementType != typeRef.ElementType)
					throw new ApplicationException($"Type {createdTypeRef}'s IsValueType is not correct");
				return createdTypeRef;
			}
			createdTypes[typeRef] = typeRef;
			return typeRef;
		}

		public MemberRef InstanceMethod(string name, IMemberRefParent declaringType, TypeSig returnType, params TypeSig[] args) =>
			Method(true, name, declaringType, returnType, args);

		public MemberRef StaticMethod(string name, IMemberRefParent declaringType, TypeSig returnType, params TypeSig[] args) =>
			Method(false, name, declaringType, returnType, args);

		public MemberRef Method(bool isInstance, string name, IMemberRefParent declaringType, TypeSig returnType, params TypeSig[] args) {
			MethodSig sig;
			if (isInstance)
				sig = MethodSig.CreateInstance(returnType, args);
			else
				sig = MethodSig.CreateStatic(returnType, args);
			return module.UpdateRowId(new MemberRefUser(module, name, sig, declaringType));
		}

		AssemblyRef FindAssemblyRef(string asmSimpleName) {
			var asmRef = module.GetAssemblyRef(asmSimpleName);
			if (asmRef == null)
				throw new ApplicationException($"Could not find assembly {asmSimpleName} in assembly references");
			return asmRef;
		}
	}
}
