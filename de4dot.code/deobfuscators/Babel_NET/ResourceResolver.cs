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
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Babel_NET {
	class ResourceResolver {
		ModuleDefinition module;
		ResourceDecrypter resourceDecrypter;
		ISimpleDeobfuscator simpleDeobfuscator;
		TypeDefinition resolverType;
		MethodDefinition registerMethod;
		EmbeddedResource encryptedResource;
		bool hasXorKeys;
		int xorKey1, xorKey2;

		public bool Detected {
			get { return resolverType != null; }
		}

		public TypeDefinition Type {
			get { return resolverType; }
		}

		public MethodDefinition InitMethod {
			get { return registerMethod; }
		}

		public ResourceResolver(ModuleDefinition module, ResourceDecrypter resourceDecrypter, ISimpleDeobfuscator simpleDeobfuscator) {
			this.module = module;
			this.resourceDecrypter = resourceDecrypter;
			this.simpleDeobfuscator = simpleDeobfuscator;
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
				if (!new FieldTypes(type).all(requiredTypes))
					continue;

				MethodDefinition regMethod, handler;
				if (!BabelUtils.findRegisterMethod(type, out regMethod, out handler))
					continue;

				var resource = BabelUtils.findEmbeddedResource(module, type);
				if (resource == null)
					continue;

				var decryptMethod = findDecryptMethod(type);
				if (decryptMethod == null)
					throw new ApplicationException("Couldn't find resource type decrypt method");
				resourceDecrypter.DecryptMethod = ResourceDecrypter.findDecrypterMethod(decryptMethod);
				initXorKeys(decryptMethod);

				resolverType = type;
				registerMethod = regMethod;
				encryptedResource = resource;
				return;
			}
		}

		static MethodDefinition findDecryptMethod(TypeDefinition type) {
			foreach (var method in type.Methods) {
				if (!DotNetUtils.isMethod(method, "System.Reflection.Assembly", "(System.IO.Stream)"))
					continue;
				return method;
			}
			return null;
		}

		void initXorKeys(MethodDefinition method) {
			simpleDeobfuscator.deobfuscate(method);
			var ints = new List<int>();
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				var callvirt = instrs[i];
				if (callvirt.OpCode.Code != Code.Callvirt)
					continue;
				var calledMethod = callvirt.Operand as MethodReference;
				if (calledMethod == null)
					continue;
				if (calledMethod.FullName != "System.Int32 System.IO.BinaryReader::ReadInt32()")
					continue;

				var ldci4 = instrs[i + 1];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;

				if (instrs[i + 2].OpCode.Code != Code.Xor)
					continue;

				ints.Add(DotNetUtils.getLdcI4Value(ldci4));
			}

			if (ints.Count == 2) {
				hasXorKeys = true;
				xorKey1 = ints[0];
				xorKey2 = ints[1];
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
			var decrypted = resourceDecrypter.decrypt(encryptedResource.GetResourceData());
			var reader = new BinaryReader(new MemoryStream(decrypted));

			int numResources = reader.ReadInt32() ^ xorKey1;
			for (int i = 0; i < numResources; i++)
				reader.ReadString();

			int len;
			if (hasXorKeys)
				len = reader.ReadInt32() ^ xorKey2;
			else
				len = (int)(reader.BaseStream.Length - reader.BaseStream.Position);
			return reader.ReadBytes(len);
		}
	}
}
