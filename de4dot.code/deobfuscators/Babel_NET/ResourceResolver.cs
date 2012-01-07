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

using System.IO;
using Mono.Cecil;
using Mono.Cecil.Cil;
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
				if (!findRegisterMethod(type, out regMethod, out handler))
					continue;

				var resource = findEmbeddedResource(type);
				if (resource == null)
					continue;

				resolverType = type;
				registerMethod = regMethod;
				encryptedResource = resource;
				return;
			}
		}

		bool findRegisterMethod(TypeDefinition type, out MethodDefinition regMethod, out MethodDefinition handler) {
			foreach (var method in type.Methods) {
				if (!method.IsStatic || method.Body == null)
					continue;
				if (method.Body.ExceptionHandlers.Count != 1)
					continue;

				foreach (var instr in method.Body.Instructions) {
					if (instr.OpCode.Code != Code.Ldftn)
						continue;
					var handlerRef = instr.Operand as MethodReference;
					if (handlerRef == null)
						continue;
					if (!DotNetUtils.isMethod(handlerRef, "System.Reflection.Assembly", "(System.Object,System.ResolveEventArgs)"))
						continue;
					if (!MemberReferenceHelper.compareTypes(type, handlerRef.DeclaringType))
						continue;
					handler = DotNetUtils.getMethod(type, handlerRef);
					if (handler == null)
						continue;
					if (handler.Body == null || handler.Body.ExceptionHandlers.Count != 1)
						continue;

					regMethod = method;
					return true;
				}
			}

			regMethod = null;
			handler = null;
			return false;
		}

		EmbeddedResource findEmbeddedResource(TypeDefinition type) {
			foreach (var method in type.Methods) {
				if (!DotNetUtils.isMethod(method, "System.String", "()"))
					continue;
				foreach (var s in DotNetUtils.getCodeStrings(method)) {
					var resource = DotNetUtils.getResource(module, s) as EmbeddedResource;
					if (resource != null)
						return resource;
				}
			}
			return null;
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
