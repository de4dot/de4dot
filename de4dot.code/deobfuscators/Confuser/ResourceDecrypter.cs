/*
    Copyright (C) 2011-2015 de4dot@gmail.com

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
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Confuser {
	class ResourceDecrypter : IVersionProvider {
		ModuleDefMD module;
		ISimpleDeobfuscator simpleDeobfuscator;
		MethodDef handler;
		MethodDef installMethod;
		TypeDef lzmaType;
		EmbeddedResource resource;
		Dictionary<FieldDef, bool> fields = new Dictionary<FieldDef, bool>();
		byte key0, key1;
		ConfuserVersion version = ConfuserVersion.Unknown;

		enum ConfuserVersion {
			Unknown,
			v14_r55802,
			v17_r73404,
			v17_r73822,
			v18_r75367,
			v18_r75369,
			v19_r77172,
		}

		public IEnumerable<FieldDef> Fields => fields.Keys;
		public MethodDef Handler => handler;
		public TypeDef LzmaType => lzmaType;
		public bool Detected => handler != null;

		public ResourceDecrypter(ModuleDefMD module, ISimpleDeobfuscator simpleDeobfuscator) {
			this.module = module;
			this.simpleDeobfuscator = simpleDeobfuscator;
		}

		public void Find() {
			if (CheckMethod(DotNetUtils.GetModuleTypeCctor(module)))
				return;
		}

		bool CheckMethod(MethodDef method) {
			if (method == null || method.Body == null)
				return false;
			if (!DotNetUtils.CallsMethod(method, "System.Void System.AppDomain::add_ResourceResolve(System.ResolveEventHandler)"))
				return false;
			simpleDeobfuscator.Deobfuscate(method, SimpleDeobfuscatorFlags.Force | SimpleDeobfuscatorFlags.DisableConstantsFolderExtraInstrs);
			fields.Clear();

			var tmpHandler = GetHandler(method);
			if (tmpHandler == null || tmpHandler.DeclaringType != method.DeclaringType)
				return false;

			var tmpResource = FindResource(tmpHandler);
			if (tmpResource == null)
				return false;

			simpleDeobfuscator.Deobfuscate(tmpHandler, SimpleDeobfuscatorFlags.Force | SimpleDeobfuscatorFlags.DisableConstantsFolderExtraInstrs);
			var tmpVersion = ConfuserVersion.Unknown;
			if (DotNetUtils.CallsMethod(tmpHandler, "System.Object System.AppDomain::GetData(System.String)")) {
				if (!DotNetUtils.CallsMethod(tmpHandler, "System.Void System.Buffer::BlockCopy(System.Array,System.Int32,System.Array,System.Int32,System.Int32)")) {
					if (!FindKey0Key1_v14_r55802(tmpHandler, out key0, out key1))
						return false;
					tmpVersion = ConfuserVersion.v14_r55802;
				}
				else if (FindKey0_v17_r73404(tmpHandler, out key0) && FindKey1_v17_r73404(tmpHandler, out key1))
					tmpVersion = ConfuserVersion.v17_r73404;
				else
					return false;
			}
			else {
				if (AddFields(FindFields(tmpHandler, method.DeclaringType)) != 1)
					return false;

				if (FindKey0_v17_r73404(tmpHandler, out key0) && FindKey1_v17_r73404(tmpHandler, out key1))
					tmpVersion = ConfuserVersion.v17_r73822;
				else if (FindKey0_v18_r75367(tmpHandler, out key0) && FindKey1_v17_r73404(tmpHandler, out key1))
					tmpVersion = ConfuserVersion.v18_r75367;
				else if (FindKey0_v18_r75369(tmpHandler, out key0) && FindKey1_v18_r75369(tmpHandler, out key1)) {
					lzmaType = ConfuserUtils.FindLzmaType(tmpHandler);
					if (lzmaType == null)
						tmpVersion = ConfuserVersion.v18_r75369;
					else
						tmpVersion = ConfuserVersion.v19_r77172;
				}
				else
					return false;
			}

			handler = tmpHandler;
			resource = tmpResource;
			installMethod = method;
			version = tmpVersion;
			return true;
		}

		static MethodDef GetHandler(MethodDef method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 2; i++) {
				var ldftn = instrs[i];
				if (ldftn.OpCode.Code != Code.Ldftn)
					continue;
				var handler = ldftn.Operand as MethodDef;
				if (handler == null)
					continue;

				var newobj = instrs[i + 1];
				if (newobj.OpCode.Code != Code.Newobj)
					continue;

				var callvirt = instrs[i + 2];
				if (callvirt.OpCode.Code != Code.Callvirt)
					continue;
				var calledMethod = callvirt.Operand as IMethod;
				if (calledMethod == null)
					continue;
				if (calledMethod.FullName != "System.Void System.AppDomain::add_ResourceResolve(System.ResolveEventHandler)")
					continue;

				return handler;
			}
			return null;
		}

		int AddFields(IEnumerable<FieldDef> moreFields) {
			int count = 0;
			foreach (var field in moreFields) {
				if (AddField(field))
					count++;
			}
			return count;
		}

		bool AddField(FieldDef field) {
			if (field == null)
				return false;
			if (fields.ContainsKey(field))
				return false;
			fields[field] = true;
			return true;
		}

		static IEnumerable<FieldDef> FindFields(MethodDef method, TypeDef declaringType) {
			var fields = new List<FieldDef>();
			foreach (var instr in method.Body.Instructions) {
				if (instr.Operand is FieldDef field && field.DeclaringType == declaringType)
					fields.Add(field);
			}
			return fields;
		}

		EmbeddedResource FindResource(MethodDef method) => DotNetUtils.GetResource(module, DotNetUtils.GetCodeStrings(method)) as EmbeddedResource;

		static bool FindKey0_v18_r75367(MethodDef method, out byte key0) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				i = ConfuserUtils.FindCallMethod(instrs, i, Code.Callvirt, "System.Int32 System.IO.Stream::Read(System.Byte[],System.Int32,System.Int32)");
				if (i < 0)
					break;
				if (i + 3 >= instrs.Count)
					break;

				if (instrs[i + 1].OpCode.Code != Code.Pop)
					continue;
				var ldci4 = instrs[i + 2];
				if (!ldci4.IsLdcI4())
					continue;
				if (!instrs[i + 3].IsStloc())
					continue;

				key0 = (byte)ldci4.GetLdcI4Value();
				return true;
			}

			key0 = 0;
			return false;
		}

		static bool FindKey0_v18_r75369(MethodDef method, out byte key0) {
			var instrs = method.Body.Instructions;
			for (int index = 0; index < instrs.Count; index++) {
				index = ConfuserUtils.FindCallMethod(instrs, index, Code.Callvirt, "System.Int32 System.IO.Stream::Read(System.Byte[],System.Int32,System.Int32)");
				if (index < 0)
					break;

				if (index + 4 >= instrs.Count)
					break;
				index++;

				if (instrs[index++].OpCode.Code != Code.Pop)
					continue;
				var ldci4 = instrs[index++];
				if (!ldci4.IsLdcI4())
					continue;
				if (instrs[index++].OpCode.Code != Code.Conv_U1)
					continue;
				if (!instrs[index++].IsStloc())
					continue;

				key0 = (byte)ldci4.GetLdcI4Value();
				return true;
			}

			key0 = 0;
			return false;
		}

		static bool FindKey1_v18_r75369(MethodDef method, out byte key1) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 4; i++) {
				int index = i;
				if (!instrs[index++].IsLdloc())
					continue;
				var ldci4_1 = instrs[index++];
				if (!ldci4_1.IsLdcI4())
					continue;
				if (instrs[index++].OpCode.Code != Code.Mul)
					continue;
				var ldci4_2 = instrs[index++];
				if (!ldci4_2.IsLdcI4() || ldci4_2.GetLdcI4Value() != 0x100)
					continue;
				if (instrs[index++].OpCode.Code != Code.Rem)
					continue;

				key1 = (byte)ldci4_1.GetLdcI4Value();
				return true;
			}

			key1 = 0;
			return false;
		}

		static bool FindKey0Key1_v14_r55802(MethodDef method, out byte key0, out byte key1) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 5; i++) {
				if (!instrs[i].IsLdcI4())
					continue;
				if (instrs[i + 1].OpCode.Code != Code.Add)
					continue;
				if (instrs[i + 2].OpCode.Code != Code.Ldelem_U1)
					continue;
				var ldci4_1 = instrs[i + 3];
				if (!ldci4_1.IsLdcI4())
					continue;
				if (instrs[i + 4].OpCode.Code != Code.Xor)
					continue;
				var ldci4_2 = instrs[i + 5];
				if (!ldci4_2.IsLdcI4())
					continue;

				key0 = (byte)ldci4_1.GetLdcI4Value();
				key1 = (byte)ldci4_2.GetLdcI4Value();
				return true;
			}
			key0 = 0;
			key1 = 0;
			return false;
		}

		static bool FindKey0_v17_r73404(MethodDef method, out byte key) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 3; i++) {
				int index = ConfuserUtils.FindCallMethod(instrs, i, Code.Callvirt, "System.Byte[] System.IO.BinaryReader::ReadBytes(System.Int32)");
				if (index < 0)
					break;
				if (index + 3 >= instrs.Count)
					break;

				if (!instrs[index + 1].IsStloc())
					continue;
				var ldci4 = instrs[index + 2];
				if (!ldci4.IsLdcI4())
					continue;
				if (!instrs[index + 3].IsStloc())
					continue;

				key = (byte)ldci4.GetLdcI4Value();
				return true;
			}
			key = 0;
			return false;
		}

		static bool FindKey1_v17_r73404(MethodDef method, out byte key) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 3; i++) {
				var ldci4_1 = instrs[i];
				if (!ldci4_1.IsLdcI4())
					continue;
				if (instrs[i + 1].OpCode.Code != Code.Mul)
					continue;
				var ldci4_2 = instrs[i + 2];
				if (!ldci4_2.IsLdcI4() || ldci4_2.GetLdcI4Value() != 0x100)
					continue;
				if (instrs[i + 3].OpCode.Code != Code.Rem)
					continue;

				key = (byte)ldci4_1.GetLdcI4Value();
				return true;
			}
			key = 0;
			return false;
		}

		public EmbeddedResource MergeResources() {
			if (resource == null)
				return null;
			DeobUtils.DecryptAndAddResources(module, resource.Name.String, () => DecryptResource());
			var tmpResource = resource;
			resource = null;
			return tmpResource;
		}

		byte[] Decompress(byte[] compressed) {
			if (lzmaType != null)
				return ConfuserUtils.SevenZipDecompress(compressed);
			return DeobUtils.Inflate(compressed, true);
		}

		byte[] DecryptXor(byte[] data) {
			byte k = key0;
			for (int i = 0; i < data.Length; i++) {
				data[i] ^= k;
				k *= key1;
			}
			return data;
		}

		byte[] DecryptResource() {
			switch (version) {
			case ConfuserVersion.v14_r55802: return Decrypt_v14_r55802();
			case ConfuserVersion.v17_r73404: return Decrypt_v17_r73404();
			case ConfuserVersion.v17_r73822: return Decrypt_v17_r73404();
			case ConfuserVersion.v18_r75367: return Decrypt_v18_r75367();
			case ConfuserVersion.v18_r75369: return Decrypt_v18_r75367();
			case ConfuserVersion.v19_r77172: return Decrypt_v18_r75367();
			default: throw new ApplicationException("Unknown version");
			}
		}

		byte[] Decrypt_v14_r55802() {
			var reader = new BinaryReader(new MemoryStream(Decompress(resource.CreateReader().ToArray())));
			var encypted = reader.ReadBytes(reader.ReadInt32());
			if ((encypted.Length & 1) != 0)
				throw new ApplicationException("Invalid resource data length");
			var decrypted = new byte[encypted.Length / 2];
			for (int i = 0; i < decrypted.Length; i++)
				decrypted[i] = (byte)((encypted[i * 2 + 1] ^ key0) * key1 + (encypted[i * 2] ^ key0));
			reader = new BinaryReader(new MemoryStream(Decompress(decrypted)));
			return reader.ReadBytes(reader.ReadInt32());
		}

		byte[] Decrypt_v17_r73404() {
			var reader = new BinaryReader(new MemoryStream(Decompress(resource.CreateReader().ToArray())));
			return DecryptXor(reader.ReadBytes(reader.ReadInt32()));
		}

		byte[] Decrypt_v18_r75367() {
			var encrypted = DecryptXor(resource.CreateReader().ToArray());
			var reader = new BinaryReader(new MemoryStream(Decompress(encrypted)));
			return reader.ReadBytes(reader.ReadInt32());
		}

		public void Deobfuscate(Blocks blocks) {
			if (blocks.Method != installMethod)
				return;
			ConfuserUtils.RemoveResourceHookCode(blocks, handler);
		}

		public bool GetRevisionRange(out int minRev, out int maxRev) {
			switch (version) {
			case ConfuserVersion.Unknown:
				minRev = maxRev = 0;
				return false;

			case ConfuserVersion.v14_r55802:
				minRev = 55802;
				maxRev = 72989;
				return true;

			case ConfuserVersion.v17_r73404:
				minRev = 73404;
				maxRev = 73791;
				return true;

			case ConfuserVersion.v17_r73822:
				minRev = 73822;
				maxRev = 75349;
				return true;

			case ConfuserVersion.v18_r75367:
				minRev = 75367;
				maxRev = 75367;
				return true;

			case ConfuserVersion.v18_r75369:
				minRev = 75369;
				maxRev = 77124;
				return true;

			case ConfuserVersion.v19_r77172:
				minRev = 77172;
				maxRev = int.MaxValue;
				return true;

			default: throw new ApplicationException("Invalid version");
			}
		}
	}
}
