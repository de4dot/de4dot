/*
    Copyright (C) 2011 de4dot@gmail.com

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
using Mono.Cecil;

namespace de4dot.deobfuscators.SmartAssembly {
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

		public bool canDecryptResource(EmbeddedResource resource) {
			var info = getEmbeddedAssemblyInfo(resource);
			if (info == null || !info.isCompressed)
				return true;
			return resourceDecrypter.CanDecrypt;
		}

		public IEnumerable<Tuple<AssemblyResolverInfo.EmbeddedAssemblyInfo, byte[]>> getDecryptedResources() {
			var returned = new Dictionary<Resource, bool>();
			foreach (var info in assemblyResolverInfo.EmbeddedAssemblyInfos) {
				if (info.resource == null) {
					Log.w("Could not find embedded resource {0}", Utils.toCsharpString(info.resourceName));
					continue;
				}
				if (returned.ContainsKey(info.resource))
					continue;
				returned[info.resource] = true;

				yield return new Tuple<AssemblyResolverInfo.EmbeddedAssemblyInfo, byte[]> {
					Item1 = info,
					Item2 = decryptResource(info),
				};
			}
		}

		AssemblyResolverInfo.EmbeddedAssemblyInfo getEmbeddedAssemblyInfo(EmbeddedResource resource) {
			foreach (var info in assemblyResolverInfo.EmbeddedAssemblyInfos) {
				if (info.resource == resource)
					return info;
			}

			return null;
		}

		public byte[] removeDecryptedResource(EmbeddedResource resource) {
			if (resource == null)
				return null;

			var info = getEmbeddedAssemblyInfo(resource);
			if (info == null)
				return null;

			var data = decryptResource(info);
			if (!assemblyResolverInfo.EmbeddedAssemblyInfos.Remove(info))
				throw new ApplicationException(string.Format("Could not remove resource {0}", Utils.toCsharpString(info.resourceName)));
			return data;
		}

		byte[] decryptResource(AssemblyResolverInfo.EmbeddedAssemblyInfo info) {
			if (info.isCompressed)
				return resourceDecrypter.decrypt(info.resource);
			else
				return info.resource.GetResourceData();
		}
	}
}
