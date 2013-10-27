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
using AssemblyData;

namespace de4dot.code.deobfuscators.Eazfuscator_NET {
	class DynocodeService : IUserGenericService {
		public const int MSG_CREATE_ENUMERABLE = 0;
		public const int MSG_WRITE_ENUMERABLE_FIELD = 1;
		public const int MSG_CREATE_ENUMERATOR = 2;
		public const int MSG_CALL_GET_CURRENT = 3;
		public const int MSG_CALL_MOVE_NEXT = 4;

		Module reflObfModule;
		IEnumerable<int> ienumerable = null;
		IEnumerator<int> ienumerator = null;

		[CreateUserGenericService]
		public static IUserGenericService Create() {
			return new DynocodeService();
		}

		public void AssemblyLoaded(Assembly assembly) {
			this.reflObfModule = assembly.ManifestModule;
		}

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
				throw new ApplicationException(string.Format("Invalid msg: {0:X8}", msg));
			}
		}

		void CreateEnumerable(uint ctorToken, object[] args) {
			var ctor = reflObfModule.ResolveMethod((int)ctorToken) as ConstructorInfo;
			if (ctor == null)
				throw new ApplicationException(string.Format("Invalid ctor with token: {0:X8}", ctorToken));
			ienumerable = (IEnumerable<int>)ctor.Invoke(args);
		}

		void WriteEnumerableField(uint fieldToken, object value) {
			var field = reflObfModule.ResolveField((int)fieldToken);
			if (field == null)
				throw new ApplicationException(string.Format("Invalid field: {0:X8}", fieldToken));
			field.SetValue(ienumerable, value);
		}

		void CreateEnumerator() {
			ienumerator = ienumerable.GetEnumerator();
		}

		int CallGetCurrent() {
			return ienumerator.Current;
		}

		bool CallMoveNext() {
			return ienumerator.MoveNext();
		}

		public void Dispose() {
		}
	}
}
