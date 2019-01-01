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
using System.Runtime.Remoting;
using System.Runtime.Serialization;
using System.Threading;
using AssemblyData;

#if !NETFRAMEWORK
namespace System.Runtime.Remoting {
	class RemotingException : SystemException {
	}
}
#endif

namespace de4dot.code.AssemblyClient {
	public sealed class AssemblyClient : IAssemblyClient {
		const int WAIT_TIME_BEFORE_CONNECTING = 1000;
		const int MAX_CONNECT_WAIT_TIME_MS = 2000;
		IAssemblyServerLoader loader;
		IAssemblyService service;
		DateTime serverLoadedTime;

		public IAssemblyService Service => service;
		public IStringDecrypterService StringDecrypterService => (IStringDecrypterService)service;
		public IMethodDecrypterService MethodDecrypterService => (IMethodDecrypterService)service;
		public IGenericService GenericService => (IGenericService)service;
		public AssemblyClient(IAssemblyServerLoader loader) => this.loader = loader;

		public void Connect() {
			loader.LoadServer();
			service = loader.CreateService();
			serverLoadedTime = DateTime.UtcNow;
		}

		public void WaitConnected() {
			// If we don't wait here, we'll sometimes get stuck in doNothing(). Make sure the
			// server has had time to start... This only seems to be needed when starting a
			// server in a different process, though.
			var loadedTime = DateTime.UtcNow - serverLoadedTime;
			var waitTime = WAIT_TIME_BEFORE_CONNECTING - (int)loadedTime.TotalMilliseconds;
			if (waitTime > 0)
				Thread.Sleep(waitTime);

			var startTime = DateTime.UtcNow;
			while (true) {
				try {
					service.DoNothing();
					break;
				}
				catch (RemotingException) {
					// Couldn't connect
				}
				var elapsedTime = DateTime.UtcNow - startTime;
				if (elapsedTime.TotalMilliseconds >= MAX_CONNECT_WAIT_TIME_MS)
					throw new ApplicationException("Could not connect to server");
				Thread.Sleep(20);
			}
		}

		public void Dispose() {
			if (service != null) {
				try {
					service.Exit();
				}
				catch (RemotingException) {
					// Couldn't connect
				}
				catch (SerializationException) {
					// For this: "End of Stream encountered before parsing was completed."
				}
				service = null;
			}
			if (loader != null) {
				loader.Dispose();
				loader = null;
			}
		}
	}
}
