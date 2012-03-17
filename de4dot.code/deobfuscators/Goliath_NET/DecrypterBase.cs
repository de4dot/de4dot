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

namespace de4dot.code.deobfuscators.Goliath_NET {
	abstract class DecrypterBase {
		protected ModuleDefinition module;
		EmbeddedResource encryptedResource;
		TypeDefinition decrypterType;
		TypeDefinition delegateType;
		TypeDefinition delegateInitType;
		protected BinaryReader decryptedReader;
		MethodDefinitionAndDeclaringTypeDict<Info> decrypterMethods = new MethodDefinitionAndDeclaringTypeDict<Info>();

		protected class Info {
			public MethodDefinition method;
			public int offset;
			public bool referenced = false;
			public Info(MethodDefinition method, int offset) {
				this.method = method;
				this.offset = offset;
			}
		}

		public bool Detected {
			get { return encryptedResource != null; }
		}

		public Resource EncryptedResource {
			get { return encryptedResource; }
		}

		public TypeDefinition Type {
			get { return decrypterType; }
		}

		public TypeDefinition DelegateInitType {
			get { return delegateInitType ?? findDelegateInitType();}
		}

		public TypeDefinition DelegateType {
			get { return delegateType; }
		}

		public IEnumerable<TypeDefinition> DecrypterTypes {
			get {
				var types = new TypeDefinitionDict<TypeDefinition>();
				foreach (var info in decrypterMethods.getValues()) {
					if (info.referenced)
						types.add(info.method.DeclaringType, info.method.DeclaringType);
				}
				return types.getValues();
			}
		}

		public DecrypterBase(ModuleDefinition module) {
			this.module = module;
		}

		protected Info getInfo(MethodDefinition method) {
			var info = decrypterMethods.find(method);
			if (info == null)
				return null;

			info.referenced = true;
			return info;
		}

		public void find() {
			foreach (var tmp in module.Resources) {
				var resource = tmp as EmbeddedResource;
				if (resource == null)
					continue;
				if (!resource.Name.EndsWith(".resources", StringComparison.Ordinal))
					continue;
				string ns, name;
				splitTypeName(resource.Name.Substring(0, resource.Name.Length - 10), out ns, out name);
				var typeRef = new TypeReference(ns, name, module, module);
				var type = DotNetUtils.getType(module, typeRef);
				if (type == null)
					continue;
				if (!checkDecrypterType(type))
					continue;

				encryptedResource = resource;
				decrypterType = type;
				break;
			}
		}

		protected abstract bool checkDecrypterType(TypeDefinition type);

		void splitTypeName(string fullName, out string ns, out string name) {
			int index = fullName.LastIndexOf('.');
			if (index < 0) {
				ns = "";
				name = fullName;
			}
			else {
				ns = fullName.Substring(0, index);
				name = fullName.Substring(index + 1);
			}
		}

		public void initialize() {
			if (encryptedResource == null)
				return;

			decryptedReader = new BinaryReader(new MemoryStream(decrypt(encryptedResource.GetResourceData())));

			delegateType = null;
			foreach (var type in module.GetTypes()) {
				var cctor = DotNetUtils.getMethod(type, ".cctor");
				if (cctor == null)
					continue;

				if (type.Fields.Count != 1)
					continue;
				var field = type.Fields[0];
				var tmpDelegateType = DotNetUtils.getType(module, field.FieldType);
				if (tmpDelegateType == null)
					continue;

				if (!checkDelegateType(tmpDelegateType))
					continue;
				if (delegateType != null && delegateType != tmpDelegateType)
					continue;

				if (!checkCctor(cctor))
					continue;

				delegateType = tmpDelegateType;

				foreach (var method in type.Methods) {
					if (method.Name == ".cctor")
						continue;
					if (!method.IsStatic || method.Body == null)
						continue;
					if (method.Parameters.Count != 0)
						continue;
					if (method.MethodReturnType.ReturnType.FullName == "System.Void")
						continue;
					var info = getDecrypterInfo(method, field);
					if (info == null)
						continue;

					decrypterMethods.add(info.method, info);
				}
			}
		}

		Info getDecrypterInfo(MethodDefinition method, FieldDefinition delegateField) {
			try {
				int index = 0;
				var instrs = method.Body.Instructions;
				if (instrs[index].OpCode.Code != Code.Ldsfld)
					return null;
				var field = instrs[index++].Operand as FieldDefinition;
				if (field != delegateField)
					return null;

				if (!DotNetUtils.isLdcI4(instrs[index]))
					return null;
				int offset = DotNetUtils.getLdcI4Value(instrs[index++]);

				if (instrs[index].OpCode.Code != Code.Call && instrs[index].OpCode.Code != Code.Callvirt)
					return null;
				var calledMethod = instrs[index++].Operand as MethodReference;
				if (calledMethod.Name != "Invoke")
					return null;

				if (instrs[index].OpCode.Code == Code.Unbox_Any)
					index++;

				if (instrs[index++].OpCode.Code != Code.Ret)
					return null;

				return new Info(method, offset);
			}
			catch (ArgumentOutOfRangeException) {
				return null;
			}
		}

		bool checkCctor(MethodDefinition cctor) {
			var ldtokenType = getLdtokenType(cctor);
			if (!MemberReferenceHelper.compareTypes(ldtokenType, cctor.DeclaringType))
				return false;

			MethodDefinition initMethod = null;
			foreach (var method in DotNetUtils.getCalledMethods(module, cctor)) {
				if (DotNetUtils.isMethod(method, "System.Void", "(System.Type)")) {
					initMethod = method;
					break;
				}
			}
			if (initMethod == null || initMethod.Body == null)
				return false;

			return true;
		}

		static TypeReference getLdtokenType(MethodDefinition method) {
			if (method == null || method.Body == null)
				return null;
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Ldtoken)
					continue;
				return instr.Operand as TypeReference;
			}
			return null;
		}

		bool checkDelegateType(TypeDefinition type) {
			if (!DotNetUtils.derivesFromDelegate(type))
				return false;
			var invoke = DotNetUtils.getMethod(type, "Invoke");
			if (invoke == null)
				return false;
			return checkDelegateInvokeMethod(invoke);
		}

		protected abstract bool checkDelegateInvokeMethod(MethodDefinition invokeMethod);

		byte[] decrypt(byte[] encryptedData) {
			const int KEY_LEN = 0x100;
			if (encryptedData.Length < KEY_LEN)
				throw new ApplicationException("Invalid encrypted data length");
			var decryptedData = new byte[encryptedData.Length - KEY_LEN];
			var pkt = module.Assembly.Name.PublicKeyToken;
			if (pkt == null || pkt.Length == 0)
				pkt = new byte[8];

			for (int i = 0, j = 0, ki = 0; i < decryptedData.Length; i++) {
				ki = (ki + 1) % (KEY_LEN - 1);
				j = (j + encryptedData[ki] + pkt[i % 8]) % (KEY_LEN - 1);
				var tmp = encryptedData[j];
				encryptedData[j] = encryptedData[ki];
				encryptedData[ki] = tmp;
				decryptedData[i] = (byte)(encryptedData[KEY_LEN + i] ^ encryptedData[(encryptedData[j] + encryptedData[ki]) % (KEY_LEN - 1)]);
			}

			return decryptedData;
		}

		TypeDefinition findDelegateInitType() {
			if (delegateType == null)
				return null;

			foreach (var type in module.Types) {
				if (type.HasProperties || type.HasEvents || type.HasFields)
					continue;

				foreach (var method in type.Methods) {
					if (!method.IsStatic || method.IsPrivate || method.Body == null)
						continue;
					var ldtokenType = getLdtokenType(method);
					if (ldtokenType == null)
						continue;
					if (!MemberReferenceHelper.compareTypes(ldtokenType, delegateType))
						continue;

					delegateInitType = type;
					return delegateInitType;
				}
			}

			return null;
		}

		public IEnumerable<MethodDefinition> getMethods() {
			var list = new List<MethodDefinition>(decrypterMethods.Count);
			foreach (var info in decrypterMethods.getValues())
				list.Add(info.method);
			return list;
		}
	}
}
