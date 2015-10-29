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
using System.Reflection;
using dnlib.DotNet;
using de4dot.blocks;

namespace AssemblyData.methodsrewriter {
	static class Resolver {
		static Dictionary<string, AssemblyResolver> assemblyResolvers = new Dictionary<string, AssemblyResolver>(StringComparer.Ordinal);
		static Dictionary<Module, MModule> modules = new Dictionary<Module, MModule>();

		public static MModule LoadAssembly(Module module) {
			MModule info;
			if (modules.TryGetValue(module, out info))
				return info;

			info = new MModule(module, ModuleDefMD.Load(module.FullyQualifiedName));
			modules[module] = info;
			return info;
		}

		static MModule GetModule(ModuleDef moduleDef) {
			foreach (var mm in modules.Values) {
				if (mm.moduleDef == moduleDef)
					return mm;
			}
			return null;
		}

		static MModule GetModule(AssemblyRef asmRef) {
			foreach (var mm in modules.Values) {
				var asm = mm.moduleDef.Assembly;
				if (asm != null && asm.FullName == asmRef.FullName)
					return mm;
			}
			return null;
		}

		public static MModule GetModule(IScope scope) {
			if (scope.ScopeType == ScopeType.ModuleDef)
				return GetModule((ModuleDef)scope);
			else if (scope.ScopeType == ScopeType.AssemblyRef)
				return GetModule((AssemblyRef)scope);

			return null;
		}

		public static MType GetType(IType typeRef) {
			if (typeRef == null)
				return null;
			var module = GetModule(typeRef.Scope);
			if (module != null)
				return module.GetType(typeRef);
			return null;
		}

		public static MMethod GetMethod(IMethod methodRef) {
			if (methodRef == null)
				return null;
			var module = GetModule(methodRef.DeclaringType.Scope);
			if (module != null)
				return module.GetMethod(methodRef);
			return null;
		}

		public static MField GetField(IField fieldRef) {
			if (fieldRef == null)
				return null;
			var module = GetModule(fieldRef.DeclaringType.Scope);
			if (module != null)
				return module.GetField(fieldRef);
			return null;
		}

		public static object GetRtObject(ITokenOperand memberRef) {
			if (memberRef == null)
				return null;
			var tdr = memberRef as ITypeDefOrRef;
			if (tdr != null)
				return GetRtType(tdr);
			var field = memberRef as IField;
			if (field != null && field.FieldSig != null)
				return GetRtField(field);
			var method = memberRef as IMethod;
			if (method != null && method.MethodSig != null)
				return GetRtMethod(method);

			throw new ApplicationException(string.Format("Unknown MemberRef: {0}", memberRef));
		}

		public static Type GetRtType(IType typeRef) {
			var mtype = GetType(typeRef);
			if (mtype != null)
				return mtype.type;

			return Resolver.Resolve(typeRef);
		}

		public static FieldInfo GetRtField(IField fieldRef) {
			var mfield = GetField(fieldRef);
			if (mfield != null)
				return mfield.fieldInfo;

			return Resolver.Resolve(fieldRef);
		}

		public static MethodBase GetRtMethod(IMethod methodRef) {
			var mmethod = GetMethod(methodRef);
			if (mmethod != null)
				return mmethod.methodBase;

			return Resolver.Resolve(methodRef);
		}

		static AssemblyResolver GetAssemblyResolver(ITypeDefOrRef type) {
			var asmName = type.DefinitionAssembly.FullName;
			AssemblyResolver resolver;
			if (!assemblyResolvers.TryGetValue(asmName, out resolver))
				assemblyResolvers[asmName] = resolver = new AssemblyResolver(asmName);
			return resolver;
		}

		static Type Resolve(IType typeRef) {
			if (typeRef == null)
				return null;
			var scopeType = typeRef.ScopeType;
			var resolver = GetAssemblyResolver(scopeType);
			var resolvedType = resolver.Resolve(scopeType);
			if (resolvedType != null)
				return FixType(typeRef, resolvedType);
			throw new ApplicationException(string.Format("Could not resolve type {0} ({1:X8}) in assembly {2}", typeRef, typeRef.MDToken.Raw, resolver));
		}

		static FieldInfo Resolve(IField fieldRef) {
			if (fieldRef == null)
				return null;
			var resolver = GetAssemblyResolver(fieldRef.DeclaringType);
			var fieldInfo = resolver.Resolve(fieldRef);
			if (fieldInfo != null)
				return fieldInfo;
			throw new ApplicationException(string.Format("Could not resolve field {0} ({1:X8}) in assembly {2}", fieldRef, fieldRef.MDToken.Raw, resolver));
		}

		static MethodBase Resolve(IMethod methodRef) {
			if (methodRef == null)
				return null;
			var resolver = GetAssemblyResolver(methodRef.DeclaringType);
			var methodBase = resolver.Resolve(methodRef);
			if (methodBase != null)
				return methodBase;
			throw new ApplicationException(string.Format("Could not resolve method {0} ({1:X8}) in assembly {2}", methodRef, methodRef.MDToken.Raw, resolver));
		}

		static Type FixType(IType typeRef, Type type) {
			var sig = typeRef as TypeSig;
			if (sig == null) {
				var ts = typeRef as TypeSpec;
				if (ts != null)
					sig = ts.TypeSig;
			}
			while (sig != null) {
				switch (sig.ElementType) {
				case ElementType.SZArray:
					type = type.MakeArrayType();
					break;

				case ElementType.Array:
					type = type.MakeArrayType((int)((ArraySig)sig).Rank);
					break;

				case ElementType.ByRef:
					type = type.MakeByRefType();
					break;

				case ElementType.Ptr:
					type = type.MakePointerType();
					break;

				case ElementType.GenericInst:
					var git = (GenericInstSig)sig;
					var args = new Type[git.GenericArguments.Count];
					bool isGenericTypeDef = true;
					for (int i = 0; i < args.Length; i++) {
						var arg = git.GenericArguments[i];
						if (!(arg is GenericSig))
							isGenericTypeDef = false;
						args[i] = Resolver.Resolve(arg);
					}
					if (!isGenericTypeDef)
						type = type.MakeGenericType(args);
					break;

				default:
					break;
				}

				sig = sig.Next;
			}
			return type;
		}
	}
}
