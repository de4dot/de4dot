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
using System.Reflection.Emit;
using System.Collections.Generic;

namespace AssemblyData {
	class DelegateStringDecrypter : IStringDecrypter {
		delegate string DecryptString(object[] args);
		List<DecryptString> stringDecryptMethods = new List<DecryptString>();

		public int DefineStringDecrypter(MethodInfo method) {
			stringDecryptMethods.Add(BuildDynamicMethod(method));
			return stringDecryptMethods.Count - 1;
		}

		public object[] DecryptStrings(int stringDecrypterMethod, object[] args, MethodBase caller) {
			if (stringDecrypterMethod > stringDecryptMethods.Count)
				throw new ApplicationException("Invalid string decrypter method");

			var rv = new object[args.Length];
			var stringDecrypter = stringDecryptMethods[stringDecrypterMethod];
			for (int i = 0; i < args.Length; i++)
				rv[i] = stringDecrypter((object[])args[i]);
			return rv;
		}

		DecryptString BuildDynamicMethod(MethodInfo method) {
			var dm = new DynamicMethod("", typeof(string), new Type[] { typeof(object[]) }, typeof(DelegateStringDecrypter), true);
			Utils.AddCallStringDecrypterMethodInstructions(method, dm.GetILGenerator());
			return (DecryptString)dm.CreateDelegate(typeof(DecryptString));
		}
	}
}
