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
using System.Text;
using dnlib.IO;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Confuser {
	class StringDecrypter : IVersionProvider {
		ModuleDefMD module;
		MethodDef decryptMethod;
		EmbeddedResource resource;
		uint magic1, magic2, key1;
		IBinaryReader reader;
		ConfuserVersion version = ConfuserVersion.Unknown;
		Decrypter decrypter;

		enum ConfuserVersion {
			Unknown,
			v10_r42915,
			v10_r48832,
			v11_r49299,
			v13_r55604_safe,
			v14_r58802_safe,
			v14_r58802_dynamic,
			// The string decrypter "confusion" was disabled from 1.5 r60785 and it was
			// replaced by the constants "confusion".
		}

		abstract class Decrypter {
			protected StringDecrypter stringDecrypter;

			protected Decrypter(StringDecrypter stringDecrypter) {
				this.stringDecrypter = stringDecrypter;
			}

			public abstract string Decrypt(MethodDef caller, int magic);
		}

		class Decrypter_v10_r42915 : Decrypter {
			int? key;

			public Decrypter_v10_r42915(StringDecrypter stringDecrypter)
				: this(stringDecrypter, null) {
			}

			public Decrypter_v10_r42915(StringDecrypter stringDecrypter, int? key)
				: base(stringDecrypter) {
				this.key = key;
			}

			public override string Decrypt(MethodDef caller, int magic) {
				var reader = stringDecrypter.reader;
				reader.Position = (caller.MDToken.ToInt32() ^ magic) - stringDecrypter.magic1;
				int len = reader.ReadInt32() ^ (int)~stringDecrypter.magic2;
				var bytes = reader.ReadBytes(len);
				var rand = new Random(key == null ? caller.MDToken.ToInt32() : key.Value);

				int mask = 0;
				for (int i = 0; i < bytes.Length; i++) {
					byte b = bytes[i];
					bytes[i] = (byte)(b ^ (rand.Next() & mask));
					mask += b;
				}
				return Encoding.UTF8.GetString(bytes);
			}
		}

		class Decrypter_v10_r48832 : Decrypter {
			PolyConstantsReader constReader;

			public Decrypter_v10_r48832(StringDecrypter stringDecrypter)
				: base(stringDecrypter) {
			}

			public override string Decrypt(MethodDef caller, int magic) {
				var reader = stringDecrypter.reader;
				reader.Position = (caller.MDToken.ToInt32() ^ magic) - stringDecrypter.magic1;
				int len = reader.ReadInt32() ^ (int)~stringDecrypter.magic2;
				var rand = new Random(caller.MDToken.ToInt32());

				var instrs = stringDecrypter.decryptMethod.Body.Instructions;
				constReader = new PolyConstantsReader(instrs, false);
				int polyIndex = ConfuserUtils.FindCallMethod(instrs, 0, Code.Callvirt, "System.Int64 System.IO.BinaryReader::ReadInt64()");
				if (polyIndex < 0)
					throw new ApplicationException("Could not find start of decrypt code");

				var decrypted = new byte[len];
				for (int i = 0; i < len; i += 8) {
					constReader.Arg = reader.ReadInt64();
					int index = polyIndex;
					long val;
					if (!constReader.GetInt64(ref index, out val) || instrs[index].OpCode.Code != Code.Conv_I8)
						throw new ApplicationException("Could not get string int64 value");
					Array.Copy(BitConverter.GetBytes(val ^ rand.Next()), 0, decrypted, i, Math.Min(8, len - i));
				}

				return Encoding.Unicode.GetString(decrypted);
			}
		}

		class Decrypter_v11_r49299 : Decrypter {
			public Decrypter_v11_r49299(StringDecrypter stringDecrypter)
				: base(stringDecrypter) {
			}

			public override string Decrypt(MethodDef caller, int magic) {
				var reader = stringDecrypter.reader;
				reader.Position = (caller.MDToken.ToInt32() ^ magic) - stringDecrypter.magic1;
				int len = reader.ReadInt32() ^ (int)~stringDecrypter.magic2;
				var decrypted = new byte[len];

				int startIndex, endIndex;
				if (!FindPolyStartEndIndexes(out startIndex, out endIndex))
					throw new ApplicationException("Could not get start/end indexes");

				var constReader = new Arg64ConstantsReader(stringDecrypter.decryptMethod.Body.Instructions, false);
				ConfuserUtils.DecryptCompressedInt32Data(constReader, startIndex, endIndex, reader, decrypted);
				return Encoding.Unicode.GetString(decrypted);
			}

			bool FindPolyStartEndIndexes(out int startIndex, out int endIndex) {
				startIndex = 0;
				endIndex = 0;

				var local = FindLocal(stringDecrypter.decryptMethod);
				if (local == null)
					return false;

				if ((endIndex = FindEndIndex(stringDecrypter.decryptMethod)) < 0)
					return false;

				if ((startIndex = FindStartIndex(stringDecrypter.decryptMethod, endIndex)) < 0)
					return false;

				return true;
			}

			static Local FindLocal(MethodDef method) {
				var instrs = method.Body.Instructions;
				for (int i = 0; i < instrs.Count - 3; i++) {
					if (instrs[i].OpCode.Code != Code.And)
						continue;
					if (instrs[i + 1].OpCode.Code != Code.Shl)
						continue;
					if (instrs[i + 2].OpCode.Code != Code.Or)
						continue;
					if (!instrs[i + 3].IsStloc())
						continue;
					return instrs[i + 3].GetLocal(method.Body.Variables);
				}
				return null;
			}

			static int FindEndIndex(MethodDef method) {
				var instrs = method.Body.Instructions;
				for (int i = 0; i < instrs.Count - 5; i++) {
					if (instrs[i].OpCode.Code != Code.Conv_U1)
						continue;
					if (instrs[i + 1].OpCode.Code != Code.Stelem_I1)
						continue;
					if (!instrs[i + 2].IsLdloc())
						continue;
					if (!instrs[i + 3].IsLdcI4())
						continue;
					if (instrs[i + 4].OpCode.Code != Code.Add)
						continue;
					if (!instrs[i + 5].IsStloc())
						continue;
					return i;
				}
				return -1;
			}

			static int FindStartIndex(MethodDef method, int endIndex) {
				var instrs = method.Body.Instructions;
				for (int i = endIndex; i >= 0; i--) {
					var instr = instrs[i];
					if (instr.OpCode.FlowControl != FlowControl.Next)
						break;
					if (instr.OpCode.Code == Code.Conv_I8)
						return i;
				}
				return -1;
			}
		}

		class PolyConstantsReader : ConstantsReader {
			long arg;

			public long Arg {
				get { return arg; }
				set { arg = value; }
			}

			public PolyConstantsReader(IList<Instruction> instrs, bool emulateConvInstrs)
				: base(instrs, emulateConvInstrs) {
			}

			protected override bool ProcessInstructionInt64(ref int index, Stack<ConstantInfo<long>> stack) {
				int i = index;

				if (instructions[i].IsLdloc()) {
					i++;
					if (i >= instructions.Count)
						return false;
				}
				var callvirt = instructions[i];
				if (callvirt.OpCode.Code != Code.Callvirt)
					return false;
				var calledMethod = callvirt.Operand as IMethod;
				if (calledMethod == null || calledMethod.FullName != "System.Int64 System.IO.BinaryReader::ReadInt64()")
					return false;

				stack.Push(new ConstantInfo<long>(index, arg));
				index = i + 1;
				return true;
			}
		}

		public EmbeddedResource Resource {
			get { return resource; }
		}

		public MethodDef Method {
			get { return decryptMethod; }
		}

		public bool Detected {
			get { return decryptMethod != null; }
		}

		public StringDecrypter(ModuleDefMD module) {
			this.module = module;
		}

		static string[] requiredLocals = new string[] {
			"System.Byte[]",
			"System.IO.BinaryReader",
			"System.Reflection.Assembly",
		};
		public void Find(ISimpleDeobfuscator simpleDeobfuscator) {
			var type = DotNetUtils.GetModuleType(module);
			if (type == null)
				return;
			foreach (var method in type.Methods) {
				if (!method.IsStatic || method.Body == null)
					continue;
				if (!DotNetUtils.IsMethod(method, "System.String", "(System.Int32)"))
					continue;
				var localTypes = new LocalTypes(method);
				if (!localTypes.All(requiredLocals))
					continue;

				simpleDeobfuscator.Deobfuscate(method);

				bool foundOldMagic1;
				if (FindMagic1(method, out magic1))
					foundOldMagic1 = true;
				else if (FindNewMagic1(method, out magic1))
					foundOldMagic1 = false;
				else
					continue;
				if (!FindMagic2(method, out magic2))
					continue;

				version = ConfuserVersion.Unknown;
				if (DotNetUtils.CallsMethod(method, "System.Text.Encoding System.Text.Encoding::get_UTF8()")) {
					if (foundOldMagic1) {
						if (DotNetUtils.CallsMethod(method, "System.Object System.AppDomain::GetData(System.String)"))
							version = ConfuserVersion.v13_r55604_safe;
						else
							version = ConfuserVersion.v10_r42915;
					}
					else {
						if (!FindSafeKey1(method, out key1))
							continue;
						version = ConfuserVersion.v14_r58802_safe;
					}
				}
				else if (!localTypes.Exists("System.Random")) {
					if (foundOldMagic1)
						version = ConfuserVersion.v11_r49299;
					else
						version = ConfuserVersion.v14_r58802_dynamic;
				}
				else if (localTypes.Exists("System.Collections.Generic.Dictionary`2<System.Int32,System.String>"))
					version = ConfuserVersion.v10_r48832;
				if (version == ConfuserVersion.Unknown)
					continue;

				decryptMethod = method;
				break;
			}
		}

		EmbeddedResource FindResource(MethodDef method) {
			return DotNetUtils.GetResource(module, DotNetUtils.GetCodeStrings(method)) as EmbeddedResource;
		}

		static bool FindMagic1(MethodDef method, out uint magic) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				int index = ConfuserUtils.FindCallMethod(instrs, i, Code.Callvirt, "System.Byte[] System.IO.BinaryReader::ReadBytes(System.Int32)");
				if (index < 0)
					break;
				if (index < 4)
					continue;

				index -= 4;
				if (!instrs[index].IsLdarg())
					continue;
				if (instrs[index + 1].OpCode.Code != Code.Xor)
					continue;
				var ldci4 = instrs[index + 2];
				if (!ldci4.IsLdcI4())
					continue;
				if (instrs[index + 3].OpCode.Code != Code.Sub)
					continue;

				magic = (uint)ldci4.GetLdcI4Value();
				return true;
			}
			magic = 0;
			return false;
		}

		static bool FindNewMagic1(MethodDef method, out uint magic) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 4; i++) {
				if (instrs[i].OpCode.Code != Code.Ldarg_0)
					continue;
				if (instrs[i + 1].OpCode.Code != Code.Xor)
					continue;
				var ldci4 = instrs[i + 2];
				if (!ldci4.IsLdcI4())
					continue;
				if (instrs[i + 3].OpCode.Code != Code.Sub)
					continue;
				if (!instrs[i + 4].IsStloc())
					continue;

				magic = (uint)ldci4.GetLdcI4Value();
				return true;
			}
			magic = 0;
			return false;
		}

		static bool FindMagic2(MethodDef method, out uint magic) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				int index = ConfuserUtils.FindCallMethod(instrs, i, Code.Callvirt, "System.UInt32 System.IO.BinaryReader::ReadUInt32()");
				if (index < 0)
					break;
				if (index + 4 >= instrs.Count)
					continue;

				if (instrs[index + 1].OpCode.Code != Code.Not)
					continue;
				var ldci4 = instrs[index + 2];
				if (!ldci4.IsLdcI4())
					continue;
				if (instrs[index + 3].OpCode.Code != Code.Xor)
					continue;
				if (!instrs[index + 4].IsStloc())
					continue;

				magic = (uint)ldci4.GetLdcI4Value();
				return true;
			}
			magic = 0;
			return false;
		}

		static bool FindSafeKey1(MethodDef method, out uint key) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				int index = ConfuserUtils.FindCallMethod(instrs, i, Code.Newobj, "System.Void System.Random::.ctor(System.Int32)");
				if (index < 0)
					break;
				if (index == 0)
					continue;

				var ldci4 = instrs[index - 1];
				if (!ldci4.IsLdcI4())
					continue;

				key = (uint)ldci4.GetLdcI4Value();
				return true;
			}
			key = 0;
			return false;
		}

		public void Initialize() {
			if (decryptMethod == null)
				return;

			resource = FindResource(decryptMethod);
			if (resource == null)
				throw new ApplicationException("Could not find encrypted strings resource");
			reader = MemoryImageStream.Create(DeobUtils.Inflate(resource.GetResourceData(), true));

			switch (version) {
			case ConfuserVersion.v10_r42915:
			case ConfuserVersion.v13_r55604_safe:
				decrypter = new Decrypter_v10_r42915(this);
				break;

			case ConfuserVersion.v10_r48832:
				decrypter = new Decrypter_v10_r48832(this);
				break;

			case ConfuserVersion.v11_r49299:
			case ConfuserVersion.v14_r58802_dynamic:
				decrypter = new Decrypter_v11_r49299(this);
				break;

			case ConfuserVersion.v14_r58802_safe:
				decrypter = new Decrypter_v10_r42915(this, (int)key1);
				break;

			default:
				throw new ApplicationException("Invalid version");
			}
		}

		public string Decrypt(MethodDef caller, int magic) {
			return decrypter.Decrypt(caller, magic);
		}

		public bool GetRevisionRange(out int minRev, out int maxRev) {
			switch (version) {
			case ConfuserVersion.Unknown:
				minRev = maxRev = 0;
				return false;

			case ConfuserVersion.v10_r42915:
				minRev = 42915;
				maxRev = 48771;
				return true;

			case ConfuserVersion.v10_r48832:
				minRev = 48832;
				maxRev = 49238;
				return true;

			case ConfuserVersion.v11_r49299:
				minRev = 49299;
				maxRev = 58741;
				return true;

			case ConfuserVersion.v13_r55604_safe:
				minRev = 55604;
				maxRev = 58741;
				return true;

			case ConfuserVersion.v14_r58802_safe:
			case ConfuserVersion.v14_r58802_dynamic:
				minRev = 58802;
				maxRev = 60408;
				return true;

			default: throw new ApplicationException("Invalid version");
			}
		}
	}
}
