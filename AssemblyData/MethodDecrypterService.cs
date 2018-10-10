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
using de4dot.blocks;
using de4dot.mdecrypt;

namespace AssemblyData {
	class MethodDecrypterService : AssemblyService, IMethodDecrypterService {
		bool installCompileMethodCalled = false;

		public void InstallCompileMethod(DecryptMethodsInfo decryptMethodsInfo) {
			if (installCompileMethodCalled)
				throw new ApplicationException("installCompileMethod() has already been called");
			installCompileMethodCalled = true;
			DynamicMethodsDecrypter.Instance.DecryptMethodsInfo = decryptMethodsInfo;
			DynamicMethodsDecrypter.Instance.InstallCompileMethod();
		}

		public void LoadObfuscator(string filename) {
			LoadAssemblyInternal(filename);
			DynamicMethodsDecrypter.Instance.Module = assembly.ManifestModule;
			DynamicMethodsDecrypter.Instance.LoadObfuscator();
		}

		public bool CanDecryptMethods() {
			CheckAssembly();
			return DynamicMethodsDecrypter.Instance.CanDecryptMethods();
		}

		public DumpedMethods DecryptMethods() {
			CheckAssembly();
			return DynamicMethodsDecrypter.Instance.DecryptMethods();
		}
	}
}
