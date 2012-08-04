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

namespace de4dot.code.deobfuscators.Confuser {
	class ResourceDecrypter {
		ModuleDefinition module;
		ISimpleDeobfuscator simpleDeobfuscator;
		MethodDefinition handler;
		MethodDefinition installMethod;
		EmbeddedResource resource;
		Dictionary<FieldDefinition, bool> fields = new Dictionary<FieldDefinition, bool>();
		byte key0, key1;
		ConfuserVersion version = ConfuserVersion.Unknown;

		enum ConfuserVersion {
			Unknown,
			v14_r55802,
			v17_r73404,
			v17_r73822,
			vXX,
		}

		public IEnumerable<FieldDefinition> Fields {
			get { return fields.Keys; }
		}

		public MethodDefinition Handler {
			get { return handler; }
		}

		public bool Detected {
			get { return handler != null; }
		}

		public ResourceDecrypter(ModuleDefinition module, ISimpleDeobfuscator simpleDeobfuscator) {
			this.module = module;
			this.simpleDeobfuscator = simpleDeobfuscator;
		}

		public void find() {
			if (checkMethod(DotNetUtils.getModuleTypeCctor(module)))
				return;
		}

		bool checkMethod(MethodDefinition method) {
			if (method == null || method.Body == null)
				return false;
			if (!DotNetUtils.callsMethod(method, "System.Void System.AppDomain::add_ResourceResolve(System.ResolveEventHandler)"))
				return false;
			simpleDeobfuscator.deobfuscate(method, true);
			fields.Clear();

			var tmpHandler = getHandler(method);
			if (tmpHandler == null || tmpHandler.DeclaringType != method.DeclaringType)
				return false;

			var tmpResource = findResource(tmpHandler);
			if (tmpResource == null)
				return false;

			simpleDeobfuscator.deobfuscate(tmpHandler, true);
			ConfuserVersion tmpVersion = ConfuserVersion.Unknown;
			if (DotNetUtils.callsMethod(tmpHandler, "System.Object System.AppDomain::GetData(System.String)")) {
				if (!DotNetUtils.callsMethod(tmpHandler, "System.Void System.Buffer::BlockCopy(System.Array,System.Int32,System.Array,System.Int32,System.Int32)")) {
					if (!findKey0Key1_v14_r55802(tmpHandler, out key0, out key1))
						return false;
					tmpVersion = ConfuserVersion.v14_r55802;
				}
				else if (findKey0_v17_r73404(tmpHandler, out key0) && findKey1_v17_r73404(tmpHandler, out key1))
					tmpVersion = ConfuserVersion.v17_r73404;
				else
					return false;
			}
			else {
				if (addFields(findFields(tmpHandler, method.DeclaringType)) != 1)
					return false;

				if (findKey0_v17_r73404(tmpHandler, out key0) && findKey1_v17_r73404(tmpHandler, out key1))
					tmpVersion = ConfuserVersion.v17_r73822;
				else if (findKey0_vXX(tmpHandler, out key0) && findKey1_vXX(tmpHandler, out key1))
					tmpVersion = ConfuserVersion.vXX;
				else
					return false;
			}

			handler = tmpHandler;
			resource = tmpResource;
			installMethod = method;
			version = tmpVersion;
			return true;
		}

		static MethodDefinition getHandler(MethodDefinition method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 2; i++) {
				var ldftn = instrs[i];
				if (ldftn.OpCode.Code != Code.Ldftn)
					continue;
				var handler = ldftn.Operand as MethodDefinition;
				if (handler == null)
					continue;

				var newobj = instrs[i + 1];
				if (newobj.OpCode.Code != Code.Newobj)
					continue;

				var callvirt = instrs[i + 2];
				if (callvirt.OpCode.Code != Code.Callvirt)
					continue;
				var calledMethod = callvirt.Operand as MethodReference;
				if (calledMethod == null)
					continue;
				if (calledMethod.FullName != "System.Void System.AppDomain::add_ResourceResolve(System.ResolveEventHandler)")
					continue;

				return handler;
			}
			return null;
		}

		int addFields(IEnumerable<FieldDefinition> moreFields) {
			int count = 0;
			foreach (var field in moreFields) {
				if (addField(field))
					count++;
			}
			return count;
		}

		bool addField(FieldDefinition field) {
			if (field == null)
				return false;
			if (fields.ContainsKey(field))
				return false;
			fields[field] = true;
			return true;
		}

		static IEnumerable<FieldDefinition> findFields(MethodDefinition method, TypeDefinition declaringType) {
			var fields = new List<FieldDefinition>();
			foreach (var instr in method.Body.Instructions) {
				var field = instr.Operand as FieldDefinition;
				if (field != null && field.DeclaringType == declaringType)
					fields.Add(field);
			}
			return fields;
		}

		EmbeddedResource findResource(MethodDefinition method) {
			return DotNetUtils.getResource(module, DotNetUtils.getCodeStrings(method)) as EmbeddedResource;
		}

		static bool findKey0_vXX(MethodDefinition method, out byte key0) {
			var instrs = method.Body.Instructions;
			for (int index = 0; index < instrs.Count; index++) {
				index = ConfuserUtils.findCallMethod(instrs, index, Code.Callvirt, "System.Int32 System.IO.Stream::Read(System.Byte[],System.Int32,System.Int32)");
				if (index < 0)
					break;

				if (index + 4 >= instrs.Count)
					break;
				index++;

				if (instrs[index++].OpCode.Code != Code.Pop)
					continue;
				var ldci4 = instrs[index++];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;
				if (instrs[index++].OpCode.Code != Code.Conv_U1)
					continue;
				if (!DotNetUtils.isStloc(instrs[index++]))
					continue;

				key0 = (byte)DotNetUtils.getLdcI4Value(ldci4);
				return true;
			}

			key0 = 0;
			return false;
		}

		static bool findKey1_vXX(MethodDefinition method, out byte key1) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 4; i++) {
				int index = i;
				if (!DotNetUtils.isLdloc(instrs[index++]))
					continue;
				var ldci4_1 = instrs[index++];
				if (!DotNetUtils.isLdcI4(ldci4_1))
					continue;
				if (instrs[index++].OpCode.Code != Code.Mul)
					continue;
				var ldci4_2 = instrs[index++];
				if (!DotNetUtils.isLdcI4(ldci4_2) || DotNetUtils.getLdcI4Value(ldci4_2) != 0x100)
					continue;
				if (instrs[index++].OpCode.Code != Code.Rem)
					continue;

				key1 = (byte)DotNetUtils.getLdcI4Value(ldci4_1);
				return true;
			}

			key1 = 0;
			return false;
		}

		static bool findKey0Key1_v14_r55802(MethodDefinition method, out byte key0, out byte key1) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 5; i++) {
				if (!DotNetUtils.isLdcI4(instrs[i]))
					continue;
				if (instrs[i + 1].OpCode.Code != Code.Add)
					continue;
				if (instrs[i + 2].OpCode.Code != Code.Ldelem_U1)
					continue;
				var ldci4_1 = instrs[i + 3];
				if (!DotNetUtils.isLdcI4(ldci4_1))
					continue;
				if (instrs[i + 4].OpCode.Code != Code.Xor)
					continue;
				var ldci4_2 = instrs[i + 5];
				if (!DotNetUtils.isLdcI4(ldci4_2))
					continue;

				key0 = (byte)DotNetUtils.getLdcI4Value(ldci4_1);
				key1 = (byte)DotNetUtils.getLdcI4Value(ldci4_2);
				return true;
			}
			key0 = 0;
			key1 = 0;
			return false;
		}

		static bool findKey0_v17_r73404(MethodDefinition method, out byte key) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 3; i++) {
				int index = ConfuserUtils.findCallMethod(instrs, i, Code.Callvirt, "System.Byte[] System.IO.BinaryReader::ReadBytes(System.Int32)");
				if (index < 0)
					break;
				if (index + 3 >= instrs.Count)
					break;

				if (!DotNetUtils.isStloc(instrs[index + 1]))
					continue;
				var ldci4 = instrs[index + 2];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;
				if (!DotNetUtils.isStloc(instrs[index + 3]))
					continue;

				key = (byte)DotNetUtils.getLdcI4Value(ldci4);
				return true;
			}
			key = 0;
			return false;
		}

		static bool findKey1_v17_r73404(MethodDefinition method, out byte key) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 3; i++) {
				var ldci4_1 = instrs[i];
				if (!DotNetUtils.isLdcI4(ldci4_1))
					continue;
				if (instrs[i + 1].OpCode.Code != Code.Mul)
					continue;
				var ldci4_2 = instrs[i + 2];
				if (!DotNetUtils.isLdcI4(ldci4_2) || DotNetUtils.getLdcI4Value(ldci4_2) != 0x100)
					continue;
				if (instrs[i + 3].OpCode.Code != Code.Rem)
					continue;

				key = (byte)DotNetUtils.getLdcI4Value(ldci4_1);
				return true;
			}
			key = 0;
			return false;
		}

		public EmbeddedResource mergeResources() {
			if (resource == null)
				return null;
			DeobUtils.decryptAndAddResources(module, resource.Name, () => decryptResource());
			var tmpResource = resource;
			resource = null;
			return tmpResource;
		}

		byte[] decryptResource() {
			switch (version) {
			case ConfuserVersion.v14_r55802: return decrypt_v14_r55802();
			case ConfuserVersion.v17_r73404: return decrypt_v17_r73404();
			case ConfuserVersion.v17_r73822: return decrypt_v17_r73404();
			case ConfuserVersion.vXX: return decrypt_vXX();
			default: throw new ApplicationException("Unknown version");
			}
		}

		byte[] decrypt_v14_r55802() {
			var reader = new BinaryReader(new MemoryStream(DeobUtils.inflate(resource.GetResourceData(), true)));
			var encypted = reader.ReadBytes(reader.ReadInt32());
			if ((encypted.Length & 1) != 0)
				throw new ApplicationException("Invalid resource data length");
			var decrypted = new byte[encypted.Length / 2];
			for (int i = 0; i < decrypted.Length; i++)
				decrypted[i] = (byte)((encypted[i * 2 + 1] ^ key0) * key1 + (encypted[i * 2] ^ key0));
			reader = new BinaryReader(new MemoryStream(DeobUtils.inflate(decrypted, true)));
			return reader.ReadBytes(reader.ReadInt32());
		}

		byte[] decrypt_v17_r73404() {
			var reader = new BinaryReader(new MemoryStream(DeobUtils.inflate(resource.GetResourceData(), true)));
			var decrypted = reader.ReadBytes(reader.ReadInt32());
			byte k = key0;
			for (int i = 0; i < decrypted.Length; i++) {
				decrypted[i] ^= k;
				k *= key1;
			}
			return decrypted;
		}

		byte[] decrypt_vXX() {
			var encrypted = resource.GetResourceData();
			byte k = key0;
			for (int i = 0; i < encrypted.Length; i++) {
				encrypted[i] ^= k;
				k *= key1;
			}
			var reader = new BinaryReader(new MemoryStream(DeobUtils.inflate(encrypted, true)));
			return reader.ReadBytes(reader.ReadInt32());
		}

		public void deobfuscate(Blocks blocks) {
			if (blocks.Method != installMethod)
				return;
			ConfuserUtils.removeResourceHookCode(blocks, handler);
		}
	}
}
