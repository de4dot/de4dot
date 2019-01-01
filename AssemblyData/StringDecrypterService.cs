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

namespace AssemblyData {
	class StringDecrypterService : AssemblyService, IStringDecrypterService {
		IStringDecrypter stringDecrypter = null;

		void CheckStringDecrypter() {
			if (stringDecrypter == null)
				throw new ApplicationException("SetStringDecrypterType() hasn't been called yet.");
		}

		public void LoadAssembly(string filename) => LoadAssemblyInternal(filename);

		public void SetStringDecrypterType(StringDecrypterType type) {
			if (stringDecrypter != null)
				throw new ApplicationException("StringDecrypterType already set");

			switch (type) {
			case StringDecrypterType.Delegate:
				stringDecrypter = new DelegateStringDecrypter();
				break;

			case StringDecrypterType.Emulate:
				stringDecrypter = new EmuStringDecrypter();
				break;

			default:
				throw new ApplicationException($"Unknown StringDecrypterType {type}");
			}
		}

		public int DefineStringDecrypter(int methodToken) {
			CheckStringDecrypter();
			var methodInfo = FindMethod(methodToken);
			if (methodInfo == null)
				throw new ApplicationException($"Could not find method {methodToken:X8}");
			if (methodInfo.ReturnType != typeof(string) && methodInfo.ReturnType != typeof(object))
				throw new ApplicationException($"Method return type must be string or object: {methodInfo}");
			return stringDecrypter.DefineStringDecrypter(methodInfo);
		}

		public object[] DecryptStrings(int stringDecrypterMethod, object[] args, int callerToken) {
			CheckStringDecrypter();
			var caller = GetCaller(callerToken);
			foreach (var arg in args)
				SimpleData.Unpack((object[])arg);
			return SimpleData.Pack(stringDecrypter.DecryptStrings(stringDecrypterMethod, args, caller));
		}

		MethodBase GetCaller(int callerToken) {
			try {
				return assembly.GetModules()[0].ResolveMethod(callerToken);
			}
			catch {
				return null;
			}
		}

		MethodInfo FindMethod(int methodToken) {
			CheckAssembly();

			foreach (var module in assembly.GetModules()) {
				if (module.ResolveMethod(methodToken) is MethodInfo method)
					return method;
			}

			return null;
		}
	}
}
