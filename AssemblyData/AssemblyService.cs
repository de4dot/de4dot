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
using System.Threading;
using dnlib.DotNet;
using de4dot.blocks;
using de4dot.mdecrypt;

namespace AssemblyData {
	public class AssemblyService : MarshalByRefObject, IAssemblyService {
		IStringDecrypter stringDecrypter = null;
		ManualResetEvent exitEvent = new ManualResetEvent(false);
		Assembly assembly = null;
		AssemblyResolver assemblyResolver = new AssemblyResolver();
		bool installCompileMethodCalled = false;

		public void DoNothing() {
		}

		public void Exit() {
			exitEvent.Set();
		}

		public void WaitExit() {
			exitEvent.WaitOne();
		}

		public override object InitializeLifetimeService() {
			return null;
		}

		void CheckStringDecrypter() {
			if (stringDecrypter == null)
				throw new ApplicationException("setStringDecrypterType() hasn't been called yet.");
		}

		void CheckAssembly() {
			if (assembly == null)
				throw new ApplicationException("loadAssembly() hasn't been called yet.");
		}

		public void LoadAssembly(string filename) {
			if (assembly != null)
				throw new ApplicationException("Only one assembly can be explicitly loaded");
			try {
				assembly = assemblyResolver.Load(filename);
			}
			catch (BadImageFormatException) {
				throw new ApplicationException(string.Format("Could not load assembly {0}. Maybe it's 32-bit or 64-bit only?", filename));
			}
		}

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
				throw new ApplicationException(string.Format("Unknown StringDecrypterType {0}", type));
			}
		}

		public int DefineStringDecrypter(int methodToken) {
			CheckStringDecrypter();
			var methodInfo = FindMethod(methodToken);
			if (methodInfo == null)
				throw new ApplicationException(string.Format("Could not find method {0:X8}", methodToken));
			if (methodInfo.ReturnType != typeof(string) && methodInfo.ReturnType != typeof(object))
				throw new ApplicationException(string.Format("Method return type must be string or object: {0}", methodInfo));
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
				var method = module.ResolveMethod(methodToken) as MethodInfo;
				if (method != null)
					return method;
			}

			return null;
		}

		public void InstallCompileMethod(DecryptMethodsInfo decryptMethodsInfo) {
			if (installCompileMethodCalled)
				throw new ApplicationException("installCompileMethod() has already been called");
			installCompileMethodCalled = true;
			DynamicMethodsDecrypter.Instance.DecryptMethodsInfo = decryptMethodsInfo;
			DynamicMethodsDecrypter.Instance.InstallCompileMethod();
		}

		public void LoadObfuscator(string filename) {
			LoadAssembly(filename);
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
