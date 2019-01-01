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
using System.Text;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Rummage {
	class StringDecrypter {
		ModuleDefMD module;
		FieldDefAndDeclaringTypeDict<StringInfo> stringInfos = new FieldDefAndDeclaringTypeDict<StringInfo>();
		IDecrypter decrypter;

		interface IDecrypter {
			RummageVersion Version { get; }
			MethodDef Method { get; }
			void Initialize();
			string Decrypt(int stringId);
		}

		abstract class DecrypterBaseV11 : IDecrypter {
			RummageVersion version;
			MethodDef decrypterMethod;
			protected int fileDispl;
			protected BinaryReader reader;
			protected uint[] key;

			public RummageVersion Version => version;
			public MethodDef Method => decrypterMethod;

			protected DecrypterBaseV11(RummageVersion version, MethodDef decrypterMethod, int fileDispl) {
				this.version = version;
				this.decrypterMethod = decrypterMethod;
				this.fileDispl = fileDispl;
			}

			public void Initialize() {
				reader = new BinaryReader(new FileStream(decrypterMethod.DeclaringType.Module.Location, FileMode.Open, FileAccess.Read, FileShare.Read));
				InitializeImpl();
			}

			protected abstract void InitializeImpl();

			protected static MethodDef FindDecrypterMethod(TypeDef type) {
				MethodDef cctor = null, decrypterMethod = null;
				foreach (var method in type.Methods) {
					if (!method.IsStatic || method.Body == null)
						return null;
					if (method.Name == ".cctor")
						cctor = method;
					else if (DotNetUtils.IsMethod(method, "System.String", "(System.Int32)"))
						decrypterMethod = method;
					else
						return null;
				}
				if (cctor == null || decrypterMethod == null)
					return null;

				return decrypterMethod;
			}

			public abstract string Decrypt(int stringId);

			protected string DecryptInternal(int stringId) {
				uint v0 = reader.ReadUInt32();
				uint v1 = reader.ReadUInt32();
				DeobUtils.XteaDecrypt(ref v0, ref v1, key, 32);
				int utf8Length = (int)v0;
				var decrypted = new uint[(utf8Length + 11) / 8 * 2 - 1];
				decrypted[0] = v1;
				for (int i = 1; i + 1 < decrypted.Length; i += 2) {
					v0 = reader.ReadUInt32();
					v1 = reader.ReadUInt32();
					DeobUtils.XteaDecrypt(ref v0, ref v1, key, 32);
					decrypted[i] = v0;
					decrypted[i + 1] = v1;
				}

				var utf8 = new byte[utf8Length];
				Buffer.BlockCopy(decrypted, 0, utf8, 0, utf8.Length);
				return Encoding.UTF8.GetString(utf8);
			}
		}

		class DecrypterV11 : DecrypterBaseV11 {
			DecrypterV11(MethodDef decrypterMethod, int fileDispl)
				: base(RummageVersion.V1_1_445, decrypterMethod, fileDispl) {
			}

			public static DecrypterV11 Create(MethodDef cctor) {
				var method = CheckType(cctor);
				if (method == null)
					return null;
				if (!GetDispl(method, out int fileDispl))
					return null;

				return new DecrypterV11(method, fileDispl);
			}

			static readonly string[] requiredFields = new string[] {
				"System.UInt32[]",
			};
			static readonly string[] requiredLocals = new string[] {
				"System.Byte[]",
				"System.Int32",
				"System.IO.FileStream",
			};
			static MethodDef CheckType(MethodDef cctor) {
				var type = cctor.DeclaringType;
				if (!new FieldTypes(type).Exactly(requiredFields))
					return null;
				if (!new LocalTypes(cctor).All(requiredLocals))
					return null;

				return FindDecrypterMethod(type);
			}

			static bool GetDispl(MethodDef method, out int displ) {
				var instrs = method.Body.Instructions;
				for (int i = 0; i < instrs.Count - 2; i++) {
					var mul = instrs[i];
					if (mul.OpCode.Code != Code.Mul)
						continue;

					var ldci4 = instrs[i + 1];
					if (!ldci4.IsLdcI4())
						continue;

					var sub = instrs[i + 2];
					if (sub.OpCode.Code != Code.Sub)
						continue;

					displ = ldci4.GetLdcI4Value();
					return true;
				}

				displ = 0;
				return false;
			}

			protected override void InitializeImpl() => InitKey();

			void InitKey() {
				reader.BaseStream.Position = reader.BaseStream.Length - 48;
				key = new uint[4];
				for (int i = 0; i < key.Length; i++)
					key[i] = reader.ReadUInt32();
			}

			public override string Decrypt(int stringId) {
				reader.BaseStream.Position = reader.BaseStream.Length + (stringId * 4 - fileDispl);
				return DecryptInternal(stringId);
			}
		}

		class DecrypterV21 : DecrypterBaseV11 {
			long baseOffs;

			public DecrypterV21(MethodDef decrypterMethod, int fileDispl)
				: base(RummageVersion.V2_1_729, decrypterMethod, fileDispl) {
			}

			public static DecrypterV21 Create(MethodDef cctor) {
				var method = CheckType(cctor);
				if (method == null)
					return null;
				if (!GetDispl(method, out int fileDispl))
					return null;

				return new DecrypterV21(method, fileDispl);
			}

			static readonly string[] requiredFields = new string[] {
				"System.UInt32[]",
				"System.Int64",
			};
			static readonly string[] requiredLocals = new string[] {
				"System.Byte[]",
				"System.Int32",
				"System.IO.FileStream",
			};
			static MethodDef CheckType(MethodDef cctor) {
				var type = cctor.DeclaringType;
				if (!new FieldTypes(type).Exactly(requiredFields))
					return null;
				if (!new LocalTypes(cctor).All(requiredLocals))
					return null;

				return FindDecrypterMethod(type);
			}

			static bool GetDispl(MethodDef method, out int displ) {
				var instrs = method.Body.Instructions;
				for (int i = 0; i < instrs.Count - 6; i++) {
					var ldci4_1 = instrs[i];
					if (!ldci4_1.IsLdcI4() || ldci4_1.GetLdcI4Value() != 4)
						continue;
					if (instrs[i + 1].OpCode.Code != Code.Mul)
						continue;
					if (instrs[i + 2].OpCode.Code != Code.Conv_I8)
						continue;
					if (instrs[i + 3].OpCode.Code != Code.Add)
						continue;

					var ldci4 = instrs[i + 4];
					if (!ldci4.IsLdcI4())
						continue;

					if (instrs[i + 5].OpCode.Code != Code.Conv_I8)
						continue;
					if (instrs[i + 6].OpCode.Code != Code.Sub)
						continue;

					displ = ldci4.GetLdcI4Value();
					return true;
				}

				displ = 0;
				return false;
			}

			static readonly byte[] magic = new byte[32] {
				0xC9, 0x76, 0xC3, 0x0D, 0xE2, 0x83, 0x72, 0xE4,
				0xD5, 0xC7, 0x35, 0xF8, 0x86, 0xD0, 0x60, 0x69,
				0xEE, 0xE1, 0x4C, 0x5E, 0x07, 0xA1, 0xC1, 0xFE,
				0x61, 0xE3, 0xAA, 0xBC, 0xE4, 0xB1, 0xF0, 0x92,
			};

			protected override void InitializeImpl() {
				baseOffs = InitializeBaseOffs();
				InitKey();
			}

			void InitKey() {
				reader.BaseStream.Position = baseOffs - 16;
				key = new uint[4];
				for (int i = 0; i < key.Length; i++)
					key[i] = reader.ReadUInt32();
			}

			long InitializeBaseOffs() {
				byte[] buf = new byte[0x1000];	// Must be 4096 bytes
				reader.BaseStream.Position = reader.BaseStream.Length - buf.Length;
				while (true) {
					if (reader.Read(buf, 0, buf.Length) != buf.Length)
						throw new ApplicationException("Could not read");

					for (int bi = buf.Length - 1; bi > magic.Length; ) {
						int mi = magic.Length - 1;
						if (buf[bi--] != magic[mi--] ||
							buf[bi] != magic[mi--])
							continue;
						while (true) {
							if (buf[--bi] != magic[mi--])
								break;
							if (mi == -1)
								return reader.BaseStream.Position - buf.Length + bi;
						}
					}

					reader.BaseStream.Position -= buf.Length * 2 - 0x20;
				}
			}

			public override string Decrypt(int stringId) {
				reader.BaseStream.Position = baseOffs + stringId * 4 - fileDispl;
				return DecryptInternal(stringId);
			}
		}

		class StringInfo {
			public readonly FieldDef field;
			public readonly int stringId;
			public string decrypted;

			public StringInfo(FieldDef field, int stringId) {
				this.field = field;
				this.stringId = stringId;
			}

			public override string ToString() {
				if (decrypted != null)
					return $"{stringId:X8} - {Utils.ToCsharpString(decrypted)}";
				return $"{stringId:X8}";
			}
		}

		public RummageVersion Version => decrypter == null ? RummageVersion.Unknown : decrypter.Version;
		public TypeDef Type => decrypter?.Method.DeclaringType;

		public IEnumerable<TypeDef> OtherTypes {
			get {
				var list = new List<TypeDef>(stringInfos.Count);
				foreach (var info in stringInfos.GetValues())
					list.Add(info.field.DeclaringType);
				return list;
			}
		}

		public bool Detected => decrypter != null;
		public StringDecrypter(ModuleDefMD module) => this.module = module;

		public void Find() {
			foreach (var type in module.GetTypes()) {
				var cctor = type.FindStaticConstructor();
				if (cctor == null)
					continue;

				decrypter = DecrypterV11.Create(cctor);
				if (decrypter != null)
					break;

				decrypter = DecrypterV21.Create(cctor);
				if (decrypter != null)
					break;
			}
		}

		public void Initialize() {
			if (decrypter == null)
				return;

			decrypter.Initialize();
			foreach (var type in module.Types)
				InitType(type);
		}

		void InitType(TypeDef type) {
			var cctor = type.FindStaticConstructor();
			if (cctor == null)
				return;
			var info = GetStringInfo(cctor);
			if (info == null)
				return;

			stringInfos.Add(info.field, info);
		}

		StringInfo GetStringInfo(MethodDef method) {
			if (method == null || method.Body == null)
				return null;
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 2; i++) {
				var ldci4 = instrs[i];
				if (!ldci4.IsLdcI4())
					continue;
				int stringId = ldci4.GetLdcI4Value();

				var call = instrs[i + 1];
				if (call.OpCode.Code != Code.Call)
					continue;
				var calledMethod = call.Operand as IMethod;
				if (!MethodEqualityComparer.CompareDeclaringTypes.Equals(decrypter.Method, calledMethod))
					continue;

				var stsfld = instrs[i + 2];
				if (stsfld.OpCode.Code != Code.Stsfld)
					continue;
				var field = stsfld.Operand as FieldDef;
				if (field == null)
					continue;

				return new StringInfo(field, stringId);
			}

			return null;
		}

		public void Deobfuscate(Blocks blocks) {
			if (decrypter == null)
				return;
			foreach (var block in blocks.MethodBlocks.GetAllBlocks()) {
				var instrs = block.Instructions;
				for (int i = 0; i < instrs.Count; i++) {
					var instr = instrs[i];

					if (instr.OpCode.Code != Code.Ldsfld)
						continue;

					var field = instr.Operand as IField;
					if (field == null)
						continue;
					var info = stringInfos.Find(field);
					if (info == null)
						continue;
					var decrypted = Decrypt(info);

					instrs[i] = new Instr(OpCodes.Ldstr.ToInstruction(decrypted));
					Logger.v("Decrypted string: {0}", Utils.ToCsharpString(decrypted));
				}
			}
		}

		string Decrypt(StringInfo info) {
			if (info.decrypted == null)
				info.decrypted = decrypter.Decrypt(info.stringId);

			return info.decrypted;
		}
	}
}
