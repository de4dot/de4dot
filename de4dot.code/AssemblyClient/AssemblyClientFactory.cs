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

using dnlib.DotNet;
using AssemblyData;

namespace de4dot.code.AssemblyClient {
	public interface IAssemblyClientFactory {
		IAssemblyClient Create(AssemblyServiceType serviceType);
	}

	public class SameAppDomainAssemblyClientFactory : IAssemblyClientFactory {
		public IAssemblyClient Create(AssemblyServiceType serviceType) {
			return new AssemblyClient(new SameAppDomainAssemblyServerLoader(serviceType));
		}
	}

	public class NewAppDomainAssemblyClientFactory : IAssemblyClientFactory {
		public IAssemblyClient Create(AssemblyServiceType serviceType) {
			return new AssemblyClient(new NewAppDomainAssemblyServerLoader(serviceType));
		}
	}

	public class NewProcessAssemblyClientFactory : IAssemblyClientFactory {
		ServerClrVersion serverVersion;

		public NewProcessAssemblyClientFactory() {
			this.serverVersion = ServerClrVersion.CLR_ANY_ANYCPU;
		}

		public NewProcessAssemblyClientFactory(ServerClrVersion serverVersion) {
			this.serverVersion = serverVersion;
		}

		public IAssemblyClient Create(AssemblyServiceType serviceType, ModuleDef module) {
			return new AssemblyClient(new NewProcessAssemblyServerLoader(serviceType, GetServerClrVersion(module)));
		}

		public IAssemblyClient Create(AssemblyServiceType serviceType) {
			return new AssemblyClient(new NewProcessAssemblyServerLoader(serviceType, serverVersion));
		}

		public static ServerClrVersion GetServerClrVersion(ModuleDef module) {
			switch (module.GetPointerSize()) {
			default:
			case 4:
				if (module.IsClr40)
					return ServerClrVersion.CLR_v40_x86;
				return ServerClrVersion.CLR_v20_x86;

			case 8:
				if (module.IsClr40)
					return ServerClrVersion.CLR_v40_x64;
				return ServerClrVersion.CLR_v20_x64;
			}
		}
	}
}
