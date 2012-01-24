/*
    Copyright (C) 2011-2012 de4dot@gmail.com

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
using System.IO;
using System.Text.RegularExpressions;
using Mono.Cecil;

namespace de4dot.code.deobfuscators.DeepSea {
	class AssemblyResolver : ResolverBase {
		public class AssemblyInfo {
			public byte[] data;
			public string fullName;
			public string simpleName;
			public string extension;
			public EmbeddedResource resource;

			public AssemblyInfo(byte[] data, string fullName, string simpleName, string extension, EmbeddedResource resource) {
				this.data = data;
				this.fullName = fullName;
				this.simpleName = simpleName;
				this.extension = extension;
				this.resource = resource;
			}
		}

		public AssemblyResolver(ModuleDefinition module, ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob)
			: base(module, simpleDeobfuscator, deob) {
		}

		static string[] handlerLocalTypes = new string[] {
			"System.Byte[]",
			"System.Security.Cryptography.SHA1CryptoServiceProvider",
			"System.IO.Compression.DeflateStream",
			"System.IO.MemoryStream",
			"System.IO.Stream",
			"System.Reflection.Assembly",
			"System.String",
		};
		protected override bool checkHandlerMethodInternal(MethodDefinition handler) {
			return new LocalTypes(handler).all(handlerLocalTypes);
		}

		public IEnumerable<AssemblyInfo> getAssemblyInfos() {
			var infos = new List<AssemblyInfo>();

			foreach (var tmp in module.Resources) {
				var resource = tmp as EmbeddedResource;
				if (resource == null)
					continue;
				if (!Regex.IsMatch(resource.Name, @"^[0-9A-F]{40}$"))
					continue;
				var info = getAssemblyInfos(resource);
				if (info == null)
					continue;
				infos.Add(info);
			}

			return infos;
		}

		AssemblyInfo getAssemblyInfos(EmbeddedResource resource) {
			try {
				var decrypted = decryptResourceV3(resource);
				var asm = AssemblyDefinition.ReadAssembly(new MemoryStream(decrypted));
				var fullName = asm.Name.FullName;
				var simpleName = asm.Name.Name;
				var extension = DeobUtils.getExtension(asm.Modules[0].Kind);
				return new AssemblyInfo(decrypted, fullName, simpleName, extension, resource);
			}
			catch (Exception) {
				return null;
			}
		}
	}
}
