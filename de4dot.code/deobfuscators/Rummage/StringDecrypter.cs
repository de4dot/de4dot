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
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Rummage {
	class StringDecrypter {
		ModuleDefinition module;
		MethodDefinition stringDecrypterMethod;
		FieldDefinitionAndDeclaringTypeDict<StringInfo> stringInfos = new FieldDefinitionAndDeclaringTypeDict<StringInfo>();
		int fileDispl;
		uint[] key;
		BinaryReader reader;

		class StringInfo {
			public readonly FieldDefinition field;
			public readonly int stringId;
			public string decrypted;

			public StringInfo(FieldDefinition field, int stringId) {
				this.field = field;
				this.stringId = stringId;
			}

			public override string ToString() {
				if (decrypted != null)
					return string.Format("{0:X8} - {1}", stringId, Utils.toCsharpString(decrypted));
				return string.Format("{0:X8}", stringId);
			}
		}

		public TypeDefinition Type {
			get { return stringDecrypterMethod != null ? stringDecrypterMethod.DeclaringType : null; }
		}

		public IEnumerable<TypeDefinition> OtherTypes {
			get {
				var list = new List<TypeDefinition>(stringInfos.Count);
				foreach (var info in stringInfos.getValues())
					list.Add(info.field.DeclaringType);
				return list;
			}
		}

		public bool Detected {
			get { return stringDecrypterMethod != null; }
		}

		public StringDecrypter(ModuleDefinition module) {
			this.module = module;
		}

		public void find() {
			foreach (var type in module.Types) {
				var method = checkType(type);
				if (method == null)
					continue;
				if (!getDispl(method, ref fileDispl))
					continue;

				stringDecrypterMethod = method;
				break;
			}
		}

		static readonly string[] requiredFields = new string[] {
			"System.UInt32[]",
		};
		static readonly string[] requiredLocals = new string[] {
			"System.Byte[]",
			"System.Int32",
			"System.IO.FileStream",
		};
		static MethodDefinition checkType(TypeDefinition type) {
			if (!new FieldTypes(type).exactly(requiredFields))
				return null;
			var cctor = DotNetUtils.getMethod(type, ".cctor");
			if (cctor == null)
				return null;
			if (!new LocalTypes(cctor).all(requiredLocals))
				return null;

			return checkMethods(type);
		}

		static MethodDefinition checkMethods(TypeDefinition type) {
			MethodDefinition cctor = null, decrypterMethod = null;
			foreach (var method in type.Methods) {
				if (!method.IsStatic || method.Body == null)
					return null;
				if (method.Name == ".cctor")
					cctor = method;
				else if (DotNetUtils.isMethod(method, "System.String", "(System.Int32)"))
					decrypterMethod = method;
				else
					return null;
			}
			if (cctor == null || decrypterMethod == null)
				return null;

			return decrypterMethod;
		}

		static bool getDispl(MethodDefinition method, ref int displ) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 2; i++) {
				var mul = instrs[i];
				if (mul.OpCode.Code != Code.Mul)
					continue;

				var ldci4 = instrs[i + 1];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;

				var sub = instrs[i + 2];
				if (sub.OpCode.Code != Code.Sub)
					continue;

				displ = DotNetUtils.getLdcI4Value(ldci4);
				return true;
			}

			return false;
		}

		public void initialize() {
			reader = new BinaryReader(new FileStream(module.FullyQualifiedName, FileMode.Open, FileAccess.Read, FileShare.Read));
			initKey();

			foreach (var type in module.Types)
				initType(type);
		}

		void initKey() {
			reader.BaseStream.Position = reader.BaseStream.Length - 48;
			key = new uint[4];
			for (int i = 0; i < key.Length; i++)
				key[i] = reader.ReadUInt32();
		}

		void initType(TypeDefinition type) {
			var cctor = DotNetUtils.getMethod(type, ".cctor");
			if (cctor == null)
				return;
			var info = getStringInfo(cctor);
			if (info == null)
				return;

			stringInfos.add(info.field, info);
		}

		StringInfo getStringInfo(MethodDefinition method) {
			if (method == null || method.Body == null)
				return null;
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 2; i++) {
				var ldci4 = instrs[i];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;
				int stringId = DotNetUtils.getLdcI4Value(ldci4);

				var call = instrs[i + 1];
				if (call.OpCode.Code != Code.Call)
					continue;
				var calledMethod = call.Operand as MethodReference;
				if (!MemberReferenceHelper.compareMethodReferenceAndDeclaringType(stringDecrypterMethod, calledMethod))
					continue;

				var stsfld = instrs[i + 2];
				if (stsfld.OpCode.Code != Code.Stsfld)
					continue;
				var field = stsfld.Operand as FieldDefinition;
				if (field == null)
					continue;

				return new StringInfo(field, stringId);
			}

			return null;
		}

		public void deobfuscate(Blocks blocks) {
			foreach (var block in blocks.MethodBlocks.getAllBlocks()) {
				var instrs = block.Instructions;
				for (int i = 0; i < instrs.Count; i++) {
					var instr = instrs[i];

					if (instr.OpCode.Code != Code.Ldsfld)
						continue;

					var field = instr.Operand as FieldReference;
					if (field == null)
						continue;
					var info = stringInfos.find(field);
					if (info == null)
						continue;
					var decrypted = decrypt(info);

					instrs[i] = new Instr(Instruction.Create(OpCodes.Ldstr, decrypted));
					Log.v("Decrypted string: {0}", Utils.toCsharpString(decrypted));
				}
			}
		}

		string decrypt(StringInfo info) {
			if (info.decrypted == null)
				info.decrypted = decrypt(info.stringId);

			return info.decrypted;
		}

		string decrypt(int stringId) {
			reader.BaseStream.Position = reader.BaseStream.Length + (stringId * 4 - fileDispl);

			uint v0 = reader.ReadUInt32();
			uint v1 = reader.ReadUInt32();
			DeobUtils.xteaDecrypt(ref v0, ref v1, key, 32);
			int utf8Length = (int)v0;
			var decrypted = new uint[(utf8Length + 11) / 8 * 2 - 1];
			decrypted[0] = v1;
			for (int i = 1; i + 1 < decrypted.Length; i += 2) {
				v0 = reader.ReadUInt32();
				v1 = reader.ReadUInt32();
				DeobUtils.xteaDecrypt(ref v0, ref v1, key, 32);
				decrypted[i] = v0;
				decrypted[i + 1] = v1;
			}

			var utf8 = new byte[utf8Length];
			Buffer.BlockCopy(decrypted, 0, utf8, 0, utf8.Length);
			return Encoding.UTF8.GetString(utf8);
		}
	}
}
