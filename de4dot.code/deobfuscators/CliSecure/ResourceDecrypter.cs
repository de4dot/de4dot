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

using System.IO;
using System.Security.Cryptography;
using System.Text;
using Mono.Cecil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.CliSecure {
	class ResourceDecrypter {
		ModuleDefinition module;
		TypeDefinition rsrcType;
		MethodDefinition rsrcRrrMethod;
		MethodDefinition rsrcDecryptMethod;

		public TypeDefinition Type {
			get { return rsrcType; }
		}

		public MethodDefinition RsrcRrrMethod {
			get { return rsrcRrrMethod; }
		}

		public ResourceDecrypter(ModuleDefinition module) {
			this.module = module;
		}

		public void find() {
			findResourceType();
		}

		void findResourceType() {
			foreach (var type in module.Types) {
				MethodDefinition rrrMethod = null;
				MethodDefinition decryptMethod = null;

				foreach (var method in type.Methods) {
					if (method.Name == "rrr" && DotNetUtils.isMethod(method, "System.Void", "()"))
						rrrMethod = method;
					else if (DotNetUtils.isMethod(method, "System.Reflection.Assembly", "(System.Object,System.ResolveEventArgs)"))
						decryptMethod = method;
				}
				if (rrrMethod == null || decryptMethod == null)
					continue;

				var methodCalls = DotNetUtils.getMethodCallCounts(rrrMethod);
				if (methodCalls.count("System.Void System.ResolveEventHandler::.ctor(System.Object,System.IntPtr)") != 1)
					continue;

				rsrcType = type;
				rsrcRrrMethod = rrrMethod;
				rsrcDecryptMethod = decryptMethod;
				return;
			}
		}

		public EmbeddedResource mergeResources() {
			if (rsrcDecryptMethod == null)
				return null;
			var resource = DotNetUtils.getResource(module, DotNetUtils.getCodeStrings(rsrcDecryptMethod)) as EmbeddedResource;
			if (resource == null)
				return null;
			DeobUtils.decryptAndAddResources(module, resource.Name, () => decryptResource(resource));
			return resource;
		}

		byte[] decryptResource(EmbeddedResource resource) {
			using (var rsrcStream = resource.GetResourceStream()) {
				using (var reader = new BinaryReader(rsrcStream)) {
					var key = reader.ReadString();
					var data = reader.ReadBytes((int)(rsrcStream.Length - rsrcStream.Position));
					var cryptoTransform = new DESCryptoServiceProvider {
						Key = Encoding.ASCII.GetBytes(key),
						IV = Encoding.ASCII.GetBytes(key),
					}.CreateDecryptor();
					var memStream = new MemoryStream(data);
					using (var reader2 = new BinaryReader(new CryptoStream(memStream, cryptoTransform, CryptoStreamMode.Read))) {
						return reader2.ReadBytes((int)memStream.Length);
					}
				}
			}
		}
	}
}
