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

namespace de4dot.code.deobfuscators.CryptoObfuscator {
	class MethodsDecrypter {
		ModuleDefinition module;
		TypeDefinition decrypterType;
		MethodDefinition decryptMethod;
		MethodDefinition decrypterCctor;
		EmbeddedResource resource;
		List<TypeDefinition> delegateTypes = new List<TypeDefinition>();

		public TypeDefinition Type {
			get { return decrypterType; }
		}

		public IEnumerable<TypeDefinition> DelegateTypes {
			get { return delegateTypes; }
		}

		public EmbeddedResource Resource {
			get { return resource; }
		}

		public bool Detected {
			get { return decrypterType != null; }
		}

		public MethodsDecrypter(ModuleDefinition module) {
			this.module = module;
		}

		public void find() {
			foreach (var type in module.Types) {
				if (check(type))
					break;
			}
		}

		static readonly string[] requiredFields = new string[] {
			"System.Byte[]",
			"System.Collections.Generic.Dictionary`2<System.Int32,System.Int32>",
			"System.ModuleHandle",
		};
		bool check(TypeDefinition type) {
			if (type.NestedTypes.Count != 1)
				return false;
			if (type.Fields.Count != 3)
				return false;
			if (!new FieldTypes(type).all(requiredFields))
				return false;

			var cctor = DotNetUtils.getMethod(type, ".cctor");
			if (cctor == null)
				return false;
			var decryptMethodTmp = findDecryptMethod(type);
			if (decryptMethodTmp == null)
				return false;

			decryptMethod = decryptMethodTmp;
			decrypterCctor = cctor;
			decrypterType = type;
			return true;
		}

		static readonly string[] requiredLocals = new string[] {
			"System.Delegate",
			"System.ModuleHandle",
			"System.Reflection.Emit.DynamicILInfo",
			"System.Reflection.Emit.DynamicMethod",
			"System.Reflection.FieldInfo",
			"System.Reflection.FieldInfo[]",
			"System.Reflection.MethodBase",
			"System.Reflection.MethodBody",
			"System.Type",
			"System.Type[]",
		};
		static MethodDefinition findDecryptMethod(TypeDefinition type) {
			foreach (var method in type.Methods) {
				if (!method.IsStatic || method.Body == null)
					continue;
				if (!new LocalTypes(method).all(requiredLocals))
					continue;
				if (!DotNetUtils.isMethod(method, "System.Void", "(System.Int32,System.Int32,System.Int32)"))
					continue;

				return method;
			}
			return null;
		}

		public void decrypt(ResourceDecrypter resourceDecrypter) {
			if (decryptMethod == null)
				return;

			resource = CoUtils.getResource(module, decrypterCctor);
			if (resource == null)
				return;
			var decrypted = resourceDecrypter.decrypt(resource.GetResourceStream());
			var reader = new BinaryReader(new MemoryStream(decrypted));
			int numEncrypted = reader.ReadInt32();
			Log.v("Restoring {0} encrypted methods", numEncrypted);
			Log.indent();
			for (int i = 0; i < numEncrypted; i++) {
				int delegateTypeToken = reader.ReadInt32();
				uint codeOffset = reader.ReadUInt32();
				var origOffset = reader.BaseStream.Position;
				reader.BaseStream.Position = codeOffset;
				decrypt(reader, delegateTypeToken);
				reader.BaseStream.Position = origOffset;
			}
			Log.deIndent();
		}

		void decrypt(BinaryReader reader, int delegateTypeToken) {
			var delegateType = module.LookupToken(delegateTypeToken) as TypeDefinition;
			if (delegateType == null)
				throw new ApplicationException("Couldn't find delegate type");

			int delToken, encMethToken, encDeclToken;
			if (!getTokens(delegateType, out delToken, out encMethToken, out encDeclToken))
				throw new ApplicationException("Could not find encrypted method tokens");
			if (delToken != delegateTypeToken)
				throw new ApplicationException("Invalid delegate type token");
			var encType = module.LookupToken(encDeclToken) as TypeReference;
			if (encType == null)
				throw new ApplicationException("Invalid declaring type token");
			var encMethod = module.LookupToken(encMethToken) as MethodDefinition;
			if (encMethod == null)
				throw new ApplicationException("Invalid encrypted method token");

			var bodyReader = new MethodBodyReader(module, reader);
			bodyReader.read(encMethod);
			bodyReader.restoreMethod(encMethod);
			Log.v("Restored method {0} ({1:X8}). Instrs:{2}, Locals:{3}, Exceptions:{4}",
					Utils.removeNewlines(encMethod.FullName),
					encMethod.MetadataToken.ToInt32(),
					encMethod.Body.Instructions.Count,
					encMethod.Body.Variables.Count,
					encMethod.Body.ExceptionHandlers.Count);
			delegateTypes.Add(delegateType);
		}

		bool getTokens(TypeDefinition delegateType, out int delegateToken, out int encMethodToken, out int encDeclaringTypeToken) {
			delegateToken = 0;
			encMethodToken = 0;
			encDeclaringTypeToken = 0;

			var cctor = DotNetUtils.getMethod(delegateType, ".cctor");
			if (cctor == null)
				return false;

			var instrs = cctor.Body.Instructions;
			for (int i = 0; i < instrs.Count - 3; i++) {
				var ldci4_1 = instrs[i];
				if (!DotNetUtils.isLdcI4(ldci4_1))
					continue;
				var ldci4_2 = instrs[i + 1];
				if (!DotNetUtils.isLdcI4(ldci4_2))
					continue;
				var ldci4_3 = instrs[i + 2];
				if (!DotNetUtils.isLdcI4(ldci4_3))
					continue;
				var call = instrs[i + 3];
				if (call.OpCode.Code != Code.Call)
					continue;
				var calledMethod = call.Operand as MethodDefinition;
				if (calledMethod == null)
					continue;
				if (calledMethod != decryptMethod)
					continue;

				delegateToken = DotNetUtils.getLdcI4Value(ldci4_1);
				encMethodToken = DotNetUtils.getLdcI4Value(ldci4_2);
				encDeclaringTypeToken = DotNetUtils.getLdcI4Value(ldci4_3);
				return true;
			}

			return false;
		}
	}
}
