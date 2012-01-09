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
	static class ResolverUtils {
		public static bool compareTypes(Type a, TypeReference b) {
			if (a == null && b == null)
				return true;
			if (a == null || b == null)
				return false;

			var type = MemberReferenceHelper.getMemberReferenceType(b);
			switch (type) {
			case CecilType.ArrayType:
				return compareArrayTypes(a, (ArrayType)b);
			case CecilType.ByReferenceType:
				return compareByReferenceTypes(a, (ByReferenceType)b);
			case CecilType.FunctionPointerType:
				return compareFunctionPointerTypes(a, (FunctionPointerType)b);
			case CecilType.GenericInstanceType:
				return compareGenericInstanceTypes(a, (GenericInstanceType)b);
			case CecilType.GenericParameter:
				return compareGenericParameters(a, (GenericParameter)b);
			case CecilType.OptionalModifierType:
				return compareOptionalModifierTypes(a, (OptionalModifierType)b);
			case CecilType.PinnedType:
				return comparePinnedTypes(a, (PinnedType)b);
			case CecilType.PointerType:
				return comparePointerTypes(a, (PointerType)b);
			case CecilType.RequiredModifierType:
				return compareRequiredModifierTypes(a, (RequiredModifierType)b);
			case CecilType.SentinelType:
				return compareSentinelTypes(a, (SentinelType)b);
			case CecilType.TypeDefinition:
				return compareTypeDefinitions(a, (TypeDefinition)b);
			case CecilType.TypeReference:
				return compareTypeReferences(a, (TypeReference)b);
			default:
				throw new ApplicationException(string.Format("Unknown cecil type {0}", type));
			}
		}

		static bool compareArrayTypes(Type a, ArrayType b) {
			if (!a.IsArray)
				return false;
			if (a.GetArrayRank() != b.Rank)
				return false;
			return compareTypes(a.GetElementType(), b.ElementType);
		}

		static bool compareByReferenceTypes(Type a, ByReferenceType b) {
			if (!a.IsByRef)
				return false;
			return compareTypes(a.GetElementType(), b.ElementType);
		}

		static bool compareFunctionPointerTypes(Type a, FunctionPointerType b) {
			return compareTypes(a, b.ElementType);
		}

		static bool compareGenericInstanceTypes(Type a, GenericInstanceType b) {
			if (!a.IsGenericType)
				return false;

			var aGpargs = a.GetGenericArguments();
			var bGpargs = b.GenericArguments;
			if (aGpargs.Length != bGpargs.Count)
				return false;

			for (int i = 0; i < aGpargs.Length; i++) {
				var aArg = aGpargs[i];
				var bArg = bGpargs[i];
				if (aArg.IsGenericParameter)
					continue;
				if (!compareTypes(aArg, bArg))
					return false;
			}

			return compareTypes(a, b.ElementType);
		}

		static bool compareGenericParameters(Type a, GenericParameter b) {
			if (!a.IsGenericParameter)
				return false;
			if (a.GenericParameterPosition != b.Position)
				return false;
			return true;
		}

		static bool compareOptionalModifierTypes(Type a, OptionalModifierType b) {
			return compareTypes(a, b.ElementType);
		}

		static bool comparePinnedTypes(Type a, PinnedType b) {
			return compareTypes(a, b.ElementType);
		}

		static bool comparePointerTypes(Type a, PointerType b) {
			if (!a.IsPointer)
				return false;
			return compareTypes(a.GetElementType(), b.ElementType);
		}

		static bool compareRequiredModifierTypes(Type a, RequiredModifierType b) {
			return compareTypes(a, b.ElementType);
		}

		static bool compareSentinelTypes(Type a, SentinelType b) {
			return compareTypes(a, b.ElementType);
		}

		static bool compareTypeDefinitions(Type a, TypeDefinition b) {
			return compareTypeReferences(a, b);
		}

		static bool compareTypeReferences(Type a, TypeReference b) {
			if (a.IsGenericParameter || a.IsPointer || a.IsByRef || a.IsArray)
				return false;

			if (a.Name != b.Name)
				return false;
			if ((a.Namespace ?? "") != b.Namespace)
				return false;

			var asmRef = DotNetUtils.getAssemblyNameReference(b);
			var asmName = a.Assembly.GetName();
			if (asmRef.Name != asmName.Name)
				return false;

			return compareTypes(a.DeclaringType, b.DeclaringType);
		}

		public static bool compareFields(FieldInfo a, FieldReference b) {
			if (a == null && b == null)
				return true;
			if (a == null || b == null)
				return false;

			return a.Name == b.Name &&
				compareTypes(a.FieldType, b.FieldType);
		}

		public static bool hasThis(MethodBase method) {
			return (method.CallingConvention & CallingConventions.HasThis) != 0;
		}

		public static bool explicitThis(MethodBase method) {
			return (method.CallingConvention & CallingConventions.ExplicitThis) != 0;
		}

		public static bool compareMethods(MethodBase a, MethodReference b) {
			if (a == null && b == null)
				return true;
			if (a == null || b == null)
				return false;

			if (a.Name != b.Name)
				return false;

			if (hasThis(a) != b.HasThis || explicitThis(a) != b.ExplicitThis)
				return false;

			CallingConventions aCallingConvention = a.CallingConvention & (CallingConventions)7;
			switch (b.CallingConvention) {
			case MethodCallingConvention.Default:
				if (aCallingConvention != CallingConventions.Standard && aCallingConvention != CallingConventions.Any)
					return false;
				break;

			case MethodCallingConvention.VarArg:
				if (aCallingConvention != CallingConventions.VarArgs && aCallingConvention != CallingConventions.Any)
					return false;
				break;

			default:
				return false;
			}

			if (!compareTypes(getReturnType(a), b.MethodReturnType.ReturnType))
				return false;

			var aParams = a.GetParameters();
			var bParams = b.Parameters;
			if (aParams.Length != bParams.Count)
				return false;
			for (int i = 0; i < aParams.Length; i++) {
				if (!compareTypes(aParams[i].ParameterType, bParams[i].ParameterType))
					return false;
			}

			var aGparams = getGenericArguments(a);
			var bGparams = b.GenericParameters;
			if (aGparams.Length != bGparams.Count)
				return false;

			return true;
		}

		public static Type getReturnType(MethodBase methodBase) {
			var methodInfo = methodBase as MethodInfo;
			if (methodInfo != null)
				return methodInfo.ReturnType;

			var ctorInfo = methodBase as ConstructorInfo;
			if (ctorInfo != null)
				return typeof(void);

			throw new ApplicationException(string.Format("Could not figure out return type: {0} ({1:X8})", methodBase, methodBase.MetadataToken));
		}

		public static Type[] getGenericArguments(MethodBase methodBase) {
			try {
				return methodBase.GetGenericArguments();
			}
			catch (NotSupportedException) {
				return new Type[0];
			}
		}

		public static IEnumerable<MethodBase> getMethodBases(Type type, BindingFlags flags) {
			if (type.TypeInitializer != null)
				yield return type.TypeInitializer;
			foreach (var ctor in type.GetConstructors(flags))
				yield return ctor;
			foreach (var m in type.GetMethods(flags))
				yield return m;
		}

		class CachedMemberInfo {
			Type type;
			Type memberType;
			public CachedMemberInfo(Type type, Type memberType) {
				this.type = type;
				this.memberType = memberType;
			}

			public override int GetHashCode() {
				return type.GetHashCode() ^ memberType.GetHashCode();
			}

			public override bool Equals(object obj) {
				var other = obj as CachedMemberInfo;
				if (other == null)
					return false;
				return type == other.type && memberType == other.memberType;
			}
		}

		static Dictionary<CachedMemberInfo, FieldInfo> cachedFieldInfos = new Dictionary<CachedMemberInfo, FieldInfo>();
		public static FieldInfo getField(Type type, Type fieldType, BindingFlags flags) {
			var key = new CachedMemberInfo(type, fieldType);
			FieldInfo fieldInfo;
			if (cachedFieldInfos.TryGetValue(key, out fieldInfo))
				return fieldInfo;

			foreach (var field in type.GetFields(flags)) {
				if (field.FieldType == fieldType) {
					cachedFieldInfos[key] = field;
					return field;
				}
			}
			return null;
		}

		public static FieldInfo getFieldThrow(Type type, Type fieldType, BindingFlags flags, string msg) {
			var info = getField(type, fieldType, flags);
			if (info != null)
				return info;
			throw new ApplicationException(msg);
		}

		public static List<FieldInfo> getFields(Type type, Type fieldType, BindingFlags flags) {
			var list = new List<FieldInfo>();
			foreach (var field in type.GetFields(flags)) {
				if (field.FieldType == fieldType)
					list.Add(field);
			}
			return list;
		}

		public static Type makeInstanceType(Type type, TypeReference typeReference) {
			var git = typeReference as GenericInstanceType;
			if (git == null)
				return type;
			var types = new Type[git.GenericArguments.Count];
			bool isTypeDef = true;
			for (int i = 0; i < git.GenericArguments.Count; i++) {
				var arg = git.GenericArguments[i];
				if (!(arg is GenericParameter))
					isTypeDef = false;
				types[i] = Resolver.getRtType(arg);
			}
			if (isTypeDef)
				return type;
			return type.MakeGenericType(types);
		}
	}
}
