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
using Mono.Cecil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Babel_NET {
	class ResourceResolver {
		ModuleDefinition module;
		TypeDefinition resolverType;
		MethodDefinition registerMethod;
		EmbeddedResource encryptedResource;

		public bool Detected {
			get { return resolverType != null; }
		}

		public TypeDefinition Type {
			get { return resolverType; }
		}

		public MethodDefinition InitMethod {
			get { return registerMethod; }
		}

		public ResourceResolver(ModuleDefinition module) {
			this.module = module;
		}

		public void find() {
			var requiredTypes = new string[] {
				"System.Reflection.Assembly",
				"System.Object",
				"System.Int32",
				"System.String[]",
			};
			foreach (var type in module.Types) {
				if (type.HasEvents)
					continue;
				if (!new FieldTypes(type).exactly(requiredTypes))
					continue;

				MethodDefinition regMethod, handler;
				if (!BabelUtils.findRegisterMethod(type, out regMethod, out handler))
					continue;

				var resource = BabelUtils.findEmbeddedResource(module, type);
				if (resource == null)
					continue;

				resolverType = type;
				registerMethod = regMethod;
				encryptedResource = resource;
				return;
			}
		}

		public EmbeddedResource mergeResources() {
			if (encryptedResource == null)
				return null;
			DeobUtils.decryptAndAddResources(module, encryptedResource.Name, () => decryptResourceAssembly());
			var result = encryptedResource;
			encryptedResource = null;
			return result;
		}

		byte[] decryptResourceAssembly() {
			var decrypted = new ResourceDecrypter(module).decrypt(encryptedResource.GetResourceData());
			var reader = new BinaryReader(new MemoryStream(decrypted));
			int numResources = reader.ReadInt32();
			for (int i = 0; i < numResources; i++)
				reader.ReadString();
			return reader.ReadBytes((int)(reader.BaseStream.Length - reader.BaseStream.Position));
		}
	}
}
