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
using System.Reflection;
using Mono.Cecil;
using de4dot.blocks;

namespace AssemblyData.methodsrewriter {
	static class Resolver {
		static Dictionary<string, AssemblyResolver> assemblyResolvers = new Dictionary<string, AssemblyResolver>(StringComparer.Ordinal);
		static Dictionary<Module, MModule> modules = new Dictionary<Module, MModule>();

		public static MModule loadAssembly(Module module) {
			MModule info;
			if (modules.TryGetValue(module, out info))
				return info;

			info = new MModule(module, ModuleDefinition.ReadModule(module.FullyQualifiedName));
			modules[module] = info;
			return info;
		}

		static MModule getModule(ModuleDefinition moduleDefinition) {
			foreach (var mm in modules.Values) {
				if (mm.moduleDefinition == moduleDefinition)
					return mm;
			}
			return null;
		}

		static MModule getModule(AssemblyNameReference assemblyRef) {
			foreach (var mm in modules.Values) {
				var asm = mm.moduleDefinition.Assembly;
				if (asm != null && asm.Name.FullName == assemblyRef.FullName)
					return mm;
			}
			return null;
		}

		public static MModule getModule(IMetadataScope scope) {
			if (scope is ModuleDefinition)
				return getModule((ModuleDefinition)scope);
			else if (scope is AssemblyNameReference)
				return getModule((AssemblyNameReference)scope);

			return null;
		}

		public static MType getType(TypeReference typeReference) {
			if (typeReference == null)
				return null;
			var module = getModule(typeReference.Scope);
			if (module != null)
				return module.getType(typeReference);
			return null;
		}

		public static MMethod getMethod(MethodReference methodReference) {
			if (methodReference == null)
				return null;
			var module = getModule(methodReference.DeclaringType.Scope);
			if (module != null)
				return module.getMethod(methodReference);
			return null;
		}

		public static MField getField(FieldReference fieldReference) {
			if (fieldReference == null)
				return null;
			var module = getModule(fieldReference.DeclaringType.Scope);
			if (module != null)
				return module.getField(fieldReference);
			return null;
		}

		public static object getRtObject(MemberReference memberReference) {
			if (memberReference == null)
				return null;
			else if (memberReference is TypeReference)
				return getRtType((TypeReference)memberReference);
			else if (memberReference is FieldReference)
				return getRtField((FieldReference)memberReference);
			else if (memberReference is MethodReference)
				return getRtMethod((MethodReference)memberReference);

			throw new ApplicationException(string.Format("Unknown MemberReference: {0}", memberReference));
		}

		public static Type getRtType(TypeReference typeReference) {
			var mtype = getType(typeReference);
			if (mtype != null)
				return mtype.type;

			return Resolver.resolve(typeReference);
		}

		public static FieldInfo getRtField(FieldReference fieldReference) {
			var mfield = getField(fieldReference);
			if (mfield != null)
				return mfield.fieldInfo;

			return Resolver.resolve(fieldReference);
		}

		public static MethodBase getRtMethod(MethodReference methodReference) {
			var mmethod = getMethod(methodReference);
			if (mmethod != null)
				return mmethod.methodBase;

			return Resolver.resolve(methodReference);
		}

		static AssemblyResolver getAssemblyResolver(TypeReference type) {
			var asmName = DotNetUtils.getFullAssemblyName(type);
			AssemblyResolver resolver;
			if (!assemblyResolvers.TryGetValue(asmName, out resolver))
				assemblyResolvers[asmName] = resolver = new AssemblyResolver(asmName);
			return resolver;
		}

		static Type resolve(TypeReference typeReference) {
			if (typeReference == null)
				return null;
			var elemType = typeReference.GetElementType();
			var resolver = getAssemblyResolver(elemType);
			var resolvedType = resolver.resolve(elemType);
			if (resolvedType != null)
				return fixType(typeReference, resolvedType);
			throw new ApplicationException(string.Format("Could not resolve type {0} ({1:X8}) in assembly {2}", typeReference, typeReference.MetadataToken.ToUInt32(), resolver));
		}

		static FieldInfo resolve(FieldReference fieldReference) {
			if (fieldReference == null)
				return null;
			var resolver = getAssemblyResolver(fieldReference.DeclaringType);
			var fieldInfo = resolver.resolve(fieldReference);
			if (fieldInfo != null)
				return fieldInfo;
			throw new ApplicationException(string.Format("Could not resolve field {0} ({1:X8}) in assembly {2}", fieldReference, fieldReference.MetadataToken.ToUInt32(), resolver));
		}

		static MethodBase resolve(MethodReference methodReference) {
			if (methodReference == null)
				return null;
			var resolver = getAssemblyResolver(methodReference.DeclaringType);
			var methodBase = resolver.resolve(methodReference);
			if (methodBase != null)
				return methodBase;
			throw new ApplicationException(string.Format("Could not resolve method {0} ({1:X8}) in assembly {2}", methodReference, methodReference.MetadataToken.ToUInt32(), resolver));
		}

		static Type fixType(TypeReference typeReference, Type type) {
			while (typeReference is TypeSpecification) {
				var ts = (TypeSpecification)typeReference;

				if (typeReference is ArrayType) {
					var arrayType = (ArrayType)typeReference;
					if (arrayType.IsVector)
						type = type.MakeArrayType();
					else
						type = type.MakeArrayType(arrayType.Rank);
				}
				else if (typeReference is ByReferenceType) {
					type = type.MakeByRefType();
				}
				else if (typeReference is PointerType) {
					type = type.MakePointerType();
				}
				else if (typeReference is GenericInstanceType) {
					var git = (GenericInstanceType)typeReference;
					var args = new Type[git.GenericArguments.Count];
					bool isGenericTypeDef = true;
					for (int i = 0; i < args.Length; i++) {
						var typeRef = git.GenericArguments[i];
						if (!(typeRef.GetElementType() is GenericParameter))
							isGenericTypeDef = false;
						args[i] = Resolver.resolve(typeRef);
					}
					if (!isGenericTypeDef)
						type = type.MakeGenericType(args);
				}

				typeReference = ts.ElementType;
			}
			return type;
		}
	}
}
