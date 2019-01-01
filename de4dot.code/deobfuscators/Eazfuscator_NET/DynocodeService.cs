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
using System.Reflection;
using AssemblyData;

namespace de4dot.code.deobfuscators.Eazfuscator_NET {
	class DynocodeService : IUserGenericService {
		public const int MSG_CREATE_ENUMERABLE = 0;
		public const int MSG_WRITE_ENUMERABLE_FIELD = 1;
		public const int MSG_CREATE_ENUMERATOR = 2;
		public const int MSG_CALL_GET_CURRENT = 3;
		public const int MSG_CALL_MOVE_NEXT = 4;

		Module reflObfModule;
		object ienumerable = null;
		object ienumerator = null;
		MethodInfo mi_get_Current;
		MethodInfo mi_MoveNext;

		[CreateUserGenericService]
		public static IUserGenericService Create() => new DynocodeService();

		public void AssemblyLoaded(Assembly assembly) => reflObfModule = assembly.ManifestModule;

		public object HandleMessage(int msg, object[] args) {
			switch (msg) {
			case MSG_CREATE_ENUMERABLE:
				CreateEnumerable((uint)args[0], args[1] as object[]);
				return true;

			case MSG_WRITE_ENUMERABLE_FIELD:
				WriteEnumerableField((uint)args[0], args[1] as object);
				return true;

			case MSG_CREATE_ENUMERATOR:
				CreateEnumerator();
				return true;

			case MSG_CALL_GET_CURRENT:
				return CallGetCurrent();

			case MSG_CALL_MOVE_NEXT:
				return CallMoveNext();

			default:
				throw new ApplicationException($"Invalid msg: {msg:X8}");
			}
		}

		void CreateEnumerable(uint ctorToken, object[] args) {
			var ctor = reflObfModule.ResolveMethod((int)ctorToken) as ConstructorInfo;
			if (ctor == null)
				throw new ApplicationException($"Invalid ctor with token: {ctorToken:X8}");
			ienumerable = ctor.Invoke(args);
		}

		void WriteEnumerableField(uint fieldToken, object value) {
			var field = reflObfModule.ResolveField((int)fieldToken);
			if (field == null)
				throw new ApplicationException($"Invalid field: {fieldToken:X8}");
			field.SetValue(ienumerable, value);
		}

		void CreateEnumerator() {
			foreach (var method in ienumerable.GetType().GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) {
				if (method.GetParameters().Length != 0)
					continue;
				var retType = method.ReturnType;
				if (!retType.IsGenericType)
					continue;
				var genArgs = retType.GetGenericArguments();
				if (genArgs.Length != 1)
					continue;
				if (genArgs[0] != typeof(int))
					continue;
				if (!FindEnumeratorMethods(retType))
					continue;

				ienumerator = method.Invoke(ienumerable, null);
				return;
			}

			throw new ApplicationException("No GetEnumerator() method found");
		}

		bool FindEnumeratorMethods(Type type) {
			mi_get_Current = null;
			mi_MoveNext = null;

			foreach (var method in ienumerable.GetType().GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) {
				if (Is_get_Current(method)) {
					if (mi_get_Current != null)
						return false;
					mi_get_Current = method;
					continue;
				}

				if (Is_MoveNext(method)) {
					if (mi_MoveNext != null)
						return false;
					mi_MoveNext = method;
					continue;
				}
			}

			return mi_get_Current != null && mi_MoveNext != null;
		}

		static bool Is_get_Current(MethodInfo method) {
			if (method.GetParameters().Length != 0)
				return false;
			if (method.ReturnType != typeof(int))
				return false;

			return true;
		}

		static bool Is_MoveNext(MethodInfo method) {
			if (method.GetParameters().Length != 0)
				return false;
			if (method.ReturnType != typeof(bool))
				return false;

			return true;
		}

		int CallGetCurrent() => (int)mi_get_Current.Invoke(ienumerator, null);
		bool CallMoveNext() => (bool)mi_MoveNext.Invoke(ienumerator, null);
		public void Dispose() { }
	}
}
