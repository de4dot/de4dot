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
	static class ResolverUtils {
		public static bool compareTypes(Type a, IType b) {
			return new SigComparer().Equals(a, b);
		}

		public static bool compareFields(FieldInfo a, IField b) {
			return new SigComparer().Equals(a, b);
		}

		public static bool hasThis(MethodBase method) {
			return (method.CallingConvention & CallingConventions.HasThis) != 0;
		}

		public static bool explicitThis(MethodBase method) {
			return (method.CallingConvention & CallingConventions.ExplicitThis) != 0;
		}

		public static bool compareMethods(MethodBase a, IMethod b) {
			return new SigComparer().Equals(a, b);
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

		public static Type makeInstanceType(Type type, ITypeDefOrRef typeRef) {
			var ts = typeRef as TypeSpec;
			if (ts == null)
				return type;
			var git = ts.TypeSig as GenericInstSig;
			if (git == null)
				return type;
			var types = new Type[git.GenericArguments.Count];
			bool isTypeDef = true;
			for (int i = 0; i < git.GenericArguments.Count; i++) {
				var arg = git.GenericArguments[i];
				if (!(arg is GenericSig))
					isTypeDef = false;
				types[i] = Resolver.getRtType(arg);
			}
			if (isTypeDef)
				return type;
			return type.MakeGenericType(types);
		}
	}
}
