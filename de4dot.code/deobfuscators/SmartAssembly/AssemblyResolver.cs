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
using dnlib.DotNet;

namespace de4dot.code.deobfuscators.SmartAssembly {
	class AssemblyResolver {
		ResourceDecrypter resourceDecrypter;
		AssemblyResolverInfo assemblyResolverInfo;

		public AssemblyResolver(ResourceDecrypter resourceDecrypter, AssemblyResolverInfo assemblyResolverInfo) {
			this.resourceDecrypter = resourceDecrypter;
			this.assemblyResolverInfo = assemblyResolverInfo;
		}

		public bool resolveResources() {
			return assemblyResolverInfo.resolveResources();
		}

		public bool canDecryptResource(EmbeddedAssemblyInfo info) {
			if (info == null || !info.isCompressed)
				return true;
			return resourceDecrypter.CanDecrypt;
		}

		public IEnumerable<Tuple<EmbeddedAssemblyInfo, byte[]>> getDecryptedResources() {
			var returned = new Dictionary<Resource, bool>();
			foreach (var info in assemblyResolverInfo.EmbeddedAssemblyInfos) {
				if (info.resource == null) {
					Logger.w("Could not find embedded resource {0}", Utils.toCsharpString(info.resourceName));
					continue;
				}
				if (returned.ContainsKey(info.resource))
					continue;
				returned[info.resource] = true;

				yield return new Tuple<EmbeddedAssemblyInfo, byte[]> {
					Item1 = info,
					Item2 = decryptResource(info),
				};
			}
		}

		public byte[] removeDecryptedResource(EmbeddedAssemblyInfo info) {
			if (info == null)
				return null;

			var data = decryptResource(info);
			if (!assemblyResolverInfo.removeEmbeddedAssemblyInfo(info))
				throw new ApplicationException(string.Format("Could not remove resource {0}", Utils.toCsharpString(info.resourceName)));
			return data;
		}

		byte[] decryptResource(EmbeddedAssemblyInfo info) {
			if (info.isCompressed)
				return resourceDecrypter.decrypt(info.resource);
			else
				return info.resource.GetResourceData();
		}
	}
}
