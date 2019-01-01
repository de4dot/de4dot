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
using System.Threading;

namespace AssemblyData {
	public abstract class AssemblyService : MarshalByRefObject, IAssemblyService {
		ManualResetEvent exitEvent = new ManualResetEvent(false);
		protected Assembly assembly = null;
		AssemblyResolver assemblyResolver = new AssemblyResolver();

		public static AssemblyService Create(AssemblyServiceType serviceType) {
			switch (serviceType) {
			case AssemblyServiceType.StringDecrypter:
				return new StringDecrypterService();

			case AssemblyServiceType.MethodDecrypter:
				return new MethodDecrypterService();

			case AssemblyServiceType.Generic:
				return new GenericService();

			default:
				throw new ArgumentException("Invalid assembly service type");
			}
		}

		public static Type GetType(AssemblyServiceType serviceType) {
			switch (serviceType) {
			case AssemblyServiceType.StringDecrypter:
				return typeof(StringDecrypterService);

			case AssemblyServiceType.MethodDecrypter:
				return typeof(MethodDecrypterService);

			case AssemblyServiceType.Generic:
				return typeof(GenericService);

			default:
				throw new ArgumentException("Invalid assembly service type");
			}
		}

		public void DoNothing() { }
		public virtual void Exit() => exitEvent.Set();
		public void WaitExit() => exitEvent.WaitOne();
		public override object InitializeLifetimeService() => null;

		protected void CheckAssembly() {
			if (assembly == null)
				throw new ApplicationException("LoadAssembly() hasn't been called yet.");
		}

		protected void LoadAssemblyInternal(string filename) {
			if (assembly != null)
				throw new ApplicationException("Only one assembly can be explicitly loaded");
			try {
				assembly = assemblyResolver.Load(filename);
			}
			catch (BadImageFormatException ex) {
				throw new ApplicationException($"Could not load assembly {filename}. Maybe it's 32-bit or 64-bit only?", ex);
			}
		}
	}
}
