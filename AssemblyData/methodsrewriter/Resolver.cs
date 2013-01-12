/*
    Copyright (C) 2011-2013 de4dot@gmail.com

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

		public static MModule loadAssembly(Module module) {
			MModule info;
			if (modules.TryGetValue(module, out info))
				return info;

			info = new MModule(module, ModuleDefMD.Load(module.FullyQualifiedName));
			modules[module] = info;
			return info;
		}

		static MModule getModule(ModuleDef moduleDef) {
			foreach (var mm in modules.Values) {
				if (mm.moduleDef == moduleDef)
					return mm;
			}
			return null;
		}

		static MModule getModule(AssemblyRef asmRef) {
			foreach (var mm in modules.Values) {
				var asm = mm.moduleDef.Assembly;
				if (asm != null && asm.FullName == asmRef.FullName)
					return mm;
			}
			return null;
		}

		public static MModule getModule(IScope scope) {
			if (scope.ScopeType == ScopeType.ModuleDef)
				return getModule((ModuleDef)scope);
			else if (scope.ScopeType == ScopeType.AssemblyRef)
				return getModule((AssemblyRef)scope);

			return null;
		}

		public static MType getType(IType typeRef) {
			if (typeRef == null)
				return null;
			var module = getModule(typeRef.Scope);
			if (module != null)
				return module.getType(typeRef);
			return null;
		}

		public static MMethod getMethod(IMethod methodRef) {
			if (methodRef == null)
				return null;
			var module = getModule(methodRef.DeclaringType.Scope);
			if (module != null)
				return module.getMethod(methodRef);
			return null;
		}

		public static MField getField(IField fieldRef) {
			if (fieldRef == null)
				return null;
			var module = getModule(fieldRef.DeclaringType.Scope);
			if (module != null)
				return module.getField(fieldRef);
			return null;
		}

		public static object getRtObject(ITokenOperand memberRef) {
			if (memberRef == null)
				return null;
			var tdr = memberRef as ITypeDefOrRef;
			if (tdr != null)
				return getRtType(tdr);
			var field = memberRef as IField;
			if (field != null && field.FieldSig != null)
				return getRtField(field);
			var method = memberRef as IMethod;
			if (method != null && method.MethodSig != null)
				return getRtMethod(method);

			throw new ApplicationException(string.Format("Unknown MemberRef: {0}", memberRef));
		}

		public static Type getRtType(IType typeRef) {
			var mtype = getType(typeRef);
			if (mtype != null)
				return mtype.type;

			return Resolver.resolve(typeRef);
		}

		public static FieldInfo getRtField(IField fieldRef) {
			var mfield = getField(fieldRef);
			if (mfield != null)
				return mfield.fieldInfo;

			return Resolver.resolve(fieldRef);
		}

		public static MethodBase getRtMethod(IMethod methodRef) {
			var mmethod = getMethod(methodRef);
			if (mmethod != null)
				return mmethod.methodBase;

			return Resolver.resolve(methodRef);
		}

		static AssemblyResolver getAssemblyResolver(ITypeDefOrRef type) {
			var asmName = type.DefinitionAssembly.FullName;
			AssemblyResolver resolver;
			if (!assemblyResolvers.TryGetValue(asmName, out resolver))
				assemblyResolvers[asmName] = resolver = new AssemblyResolver(asmName);
			return resolver;
		}

		static Type resolve(IType typeRef) {
			if (typeRef == null)
				return null;
			var scopeType = typeRef.ScopeType;
			var resolver = getAssemblyResolver(scopeType);
			var resolvedType = resolver.resolve(scopeType);
			if (resolvedType != null)
				return fixType(typeRef, resolvedType);
			throw new ApplicationException(string.Format("Could not resolve type {0} ({1:X8}) in assembly {2}", typeRef, typeRef.MDToken.Raw, resolver));
		}

		static FieldInfo resolve(IField fieldRef) {
			if (fieldRef == null)
				return null;
			var resolver = getAssemblyResolver(fieldRef.DeclaringType);
			var fieldInfo = resolver.resolve(fieldRef);
			if (fieldInfo != null)
				return fieldInfo;
			throw new ApplicationException(string.Format("Could not resolve field {0} ({1:X8}) in assembly {2}", fieldRef, fieldRef.MDToken.Raw, resolver));
		}

		static MethodBase resolve(IMethod methodRef) {
			if (methodRef == null)
				return null;
			var resolver = getAssemblyResolver(methodRef.DeclaringType);
			var methodBase = resolver.resolve(methodRef);
			if (methodBase != null)
				return methodBase;
			throw new ApplicationException(string.Format("Could not resolve method {0} ({1:X8}) in assembly {2}", methodRef, methodRef.MDToken.Raw, resolver));
		}

		static Type fixType(IType typeRef, Type type) {
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
						args[i] = Resolver.resolve(arg);
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
