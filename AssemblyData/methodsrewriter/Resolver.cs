/*
    Copyright (C) 2011 de4dot@gmail.com

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
	public static class Resolver {
		static Dictionary<string, AssemblyResolver> assemblyResolvers = new Dictionary<string, AssemblyResolver>(StringComparer.Ordinal);

		static AssemblyResolver getAssemblyResolver(IMetadataScope scope) {
			var asmName = DotNetUtils.getFullAssemblyName(scope);
			AssemblyResolver resolver;
			if (!assemblyResolvers.TryGetValue(asmName, out resolver))
				assemblyResolvers[asmName] = resolver = new AssemblyResolver(asmName);
			return resolver;
		}

		public static Type resolve(TypeReference typeReference) {
			var elemType = typeReference.GetElementType();
			var resolver = getAssemblyResolver(elemType.Scope);
			var resolvedType = resolver.resolve(elemType);
			if (resolvedType != null)
				return fixType(typeReference, resolvedType);
			throw new ApplicationException(string.Format("Could not resolve type {0} ({1:X8}) in assembly {2}", typeReference, typeReference.MetadataToken.ToUInt32(), resolver));
		}

		public static FieldInfo resolve(FieldReference fieldReference) {
			var resolver = getAssemblyResolver(fieldReference.DeclaringType.Scope);
			var fieldInfo = resolver.resolve(fieldReference);
			if (fieldInfo != null)
				return fieldInfo;
			throw new ApplicationException(string.Format("Could not resolve field {0} ({1:X8}) in assembly {2}", fieldReference, fieldReference.MetadataToken.ToUInt32(), resolver));
		}

		public static MethodBase resolve(MethodReference methodReference) {
			var resolver = getAssemblyResolver(methodReference.DeclaringType.Scope);
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
